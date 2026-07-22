using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AICopilot.Infrastructure.Storage;

public sealed class LocalArtifactWorkspaceFileSetStore(
    IArtifactWorkspaceFileStore workspaceFileStore,
    IPersistenceCommitScope commitScope,
    IPersistenceFileReconciliationLeaseManager leaseManager,
    ILogger<LocalArtifactWorkspaceFileSetStore> logger)
    : IArtifactWorkspaceFileSetStore
{
    private const string JournalRelativeRoot = ".persistence/artifact-file-sets/journal";
    private const string StagingRelativeRoot = ".persistence/artifact-file-sets/staging";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string storageRoot = Path.GetFullPath(workspaceFileStore.GetSettings().RootPath);
    private IPersistenceFileReconciliationLease? activeLease;
    private ArtifactFileSetStage? activeStage;

    public async Task<ArtifactFileSetStage> StageAsync(
        string workspaceCode,
        string operationKind,
        string publishArea,
        IReadOnlyCollection<ArtifactFileSetWriteRequest> files,
        CancellationToken cancellationToken = default,
        ArtifactFileSetAuthority? authority = null)
    {
        if (activeLease is not null)
        {
            throw new InvalidOperationException("The current scope already has a pending artifact file-set stage.");
        }

        ValidateWorkspaceCode(workspaceCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        if (files.Count == 0 || files.Count > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(files), "Artifact file-set must contain between 1 and 256 files.");
        }

        var normalizedPublishArea = NormalizePublishArea(publishArea);
        var normalizedAuthority = NormalizeAuthority(authority);
        var entries = files
            .Select(NormalizeEntry)
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (entries.Select(entry => entry.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
        {
            throw new InvalidOperationException("Artifact file-set contains duplicate target paths.");
        }

        var commitId = commitScope.ReserveCommitId();
        var publishedReference = $"{normalizedPublishArea}/{commitId:N}";
        var stagingReference = $"{workspaceCode}/{StagingRelativeRoot}/{commitId:N}";
        var publishedFiles = entries
            .Select(entry => new ArtifactFileSetPublishedFile(
                $"{publishedReference}/{entry.RelativePath}",
                entry.Content.LongLength,
                entry.MimeType,
                ComputeSha256(entry.Content)))
            .ToArray();
        var manifestJson = JsonSerializer.Serialize(new
        {
            version = "artifact-file-set-v1",
            commitId,
            workspaceCode,
            operationKind = operationKind.Trim(),
            publishedReference,
            authority = new
            {
                normalizedAuthority.TaskId,
                normalizedAuthority.NodeRunId,
                normalizedAuthority.TaskFencingToken,
                normalizedAuthority.NodeFencingToken
            },
            files = publishedFiles.Select(file => new
            {
                file.RelativePath,
                file.FileSize,
                file.MimeType,
                file.Sha256
            })
        }, JsonOptions);
        var stage = new ArtifactFileSetStage(
            commitId,
            workspaceCode,
            operationKind.Trim(),
            stagingReference,
            publishedReference,
            manifestJson,
            ComputeSha256(Encoding.UTF8.GetBytes(manifestJson)),
            publishedFiles,
            DateTimeOffset.UtcNow,
            normalizedAuthority);
        var journalWritten = false;
        try
        {
            activeLease = await leaseManager.TryAcquireAsync(commitId, cancellationToken)
                          ?? throw new InvalidOperationException(
                              "Unable to acquire artifact file-set reconciliation lease.");
            activeStage = stage;
            await WriteJournalAsync(stage, cancellationToken);
            journalWritten = true;
            await WriteAndPublishAsync(stage, entries, cancellationToken);
            if (!await VerifyPublishedAsync(stage, cancellationToken))
            {
                throw new InvalidOperationException("Published artifact file-set failed manifest verification.");
            }

            return stage;
        }
        catch
        {
            if (journalWritten)
            {
                await RollbackBestEffortAsync(stage, CancellationToken.None);
            }
            else
            {
                commitScope.ReleaseCommitId(commitId);
                await ReleaseLeaseAsync(commitId);
            }

            throw;
        }
    }

    public async Task ConfirmBestEffortAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveStage(stage);
        if (commitScope.CurrentCommitId == stage.CommitId)
        {
            throw new InvalidOperationException(
                "Artifact file-set cannot be confirmed before database persistence consumes its commit id.");
        }

        try
        {
            if (!await VerifyPublishedAsync(stage, cancellationToken))
            {
                throw new InvalidOperationException("Committed artifact file-set no longer matches its manifest.");
            }

            DeleteJournal(stage.CommitId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "Artifact file-set confirmation remains pending. CommitId={CommitId}; ErrorType={ErrorType}",
                stage.CommitId,
                exception.GetType().Name);
        }
        finally
        {
            commitScope.ReleaseCommitId(stage.CommitId);
            await ReleaseLeaseAsync(stage.CommitId);
        }
    }

    public async Task RollbackBestEffortAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default)
    {
        EnsureActiveStage(stage);
        try
        {
            DeleteStagePaths(stage, cancellationToken);
            DeleteJournal(stage.CommitId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "Artifact file-set rollback remains pending. CommitId={CommitId}; ErrorType={ErrorType}",
                stage.CommitId,
                exception.GetType().Name);
        }
        finally
        {
            commitScope.ReleaseCommitId(stage.CommitId);
            await ReleaseLeaseAsync(stage.CommitId);
        }
    }

    public async Task LeavePendingAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        EnsureActiveStage(stage);
        commitScope.ReleaseCommitId(stage.CommitId);
        await ReleaseLeaseAsync(stage.CommitId);
    }

    public async Task<bool> VerifyPublishedAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default)
    {
        var publishedRoot = GetWorkspacePath(stage.WorkspaceCode, stage.PublishedReference);
        var manifestPath = GetSafeChildPath(publishedRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        if (!string.Equals(ComputeSha256(manifestBytes), stage.ManifestDigest, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var file in stage.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!file.RelativePath.StartsWith(stage.PublishedReference + "/", StringComparison.Ordinal))
            {
                return false;
            }

            var relativeToPublished = file.RelativePath[(stage.PublishedReference.Length + 1)..];
            var path = GetSafeChildPath(publishedRoot, relativeToPublished);
            if (!File.Exists(path))
            {
                return false;
            }

            var info = new FileInfo(path);
            if (info.Length != file.FileSize ||
                !string.Equals(await ComputeFileSha256Async(path, cancellationToken), file.Sha256, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public async Task<ArtifactFileSetPendingSnapshot> GetPendingAsync(
        int maximumEntries,
        DateTimeOffset createdBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        if (maximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        var journalRoot = GetStoragePath(JournalRelativeRoot);
        if (!Directory.Exists(journalRoot))
        {
            return new ArtifactFileSetPendingSnapshot([], false);
        }

        var stages = new List<(ArtifactFileSetStage Stage, DateTimeOffset LastAttempt)>();
        var unreadable = false;
        foreach (var path in Directory.EnumerateFiles(journalRoot, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stage = JsonSerializer.Deserialize<ArtifactFileSetStage>(
                    await File.ReadAllTextAsync(path, cancellationToken),
                    JsonOptions);
                if (stage is null)
                {
                    unreadable = true;
                    continue;
                }

                var attemptedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                if (stage.CreatedAtUtc <= createdBeforeUtc && attemptedAt <= createdBeforeUtc)
                {
                    stages.Add((stage, attemptedAt));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                unreadable = true;
            }
        }

        return new ArtifactFileSetPendingSnapshot(
            stages
                .OrderBy(item => item.LastAttempt)
                .ThenBy(item => item.Stage.CreatedAtUtc)
                .ThenBy(item => item.Stage.CommitId)
                .Take(maximumEntries)
                .Select(item => item.Stage)
                .ToArray(),
            unreadable);
    }

    public async Task ConfirmPendingAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default)
    {
        if (!await VerifyPublishedAsync(stage, cancellationToken))
        {
            throw new InvalidOperationException("Committed artifact file-set is missing or failed digest verification.");
        }

        DeleteJournal(stage.CommitId);
    }

    public Task RollbackPendingAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default)
    {
        DeleteStagePaths(stage, cancellationToken);
        DeleteJournal(stage.CommitId);
        return Task.CompletedTask;
    }

    public Task MarkPendingAttemptedAsync(
        Guid commitId,
        DateTimeOffset attemptedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetJournalPath(commitId);
        if (File.Exists(path))
        {
            File.SetLastWriteTimeUtc(path, attemptedAtUtc.UtcDateTime);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsPendingAsync(
        Guid commitId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetJournalPath(commitId)));
    }

    private async Task WriteAndPublishAsync(
        ArtifactFileSetStage stage,
        IReadOnlyCollection<ArtifactFileSetWriteRequest> entries,
        CancellationToken cancellationToken)
    {
        var stagingRoot = GetStoragePath(stage.StagingReference);
        var publishedRoot = GetWorkspacePath(stage.WorkspaceCode, stage.PublishedReference);
        if (Directory.Exists(stagingRoot))
        {
            Directory.Delete(stagingRoot, recursive: true);
        }

        Directory.CreateDirectory(stagingRoot);
        EnsureNoSymbolicLinks(storageRoot, stagingRoot);
        foreach (var entry in entries)
        {
            var path = GetSafeChildPath(stagingRoot, entry.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            EnsureNoSymbolicLinks(stagingRoot, Path.GetDirectoryName(path)!);
            await WriteDurablyAsync(path, entry.Content, cancellationToken);
        }

        await WriteDurablyAsync(
            GetSafeChildPath(stagingRoot, "manifest.json"),
            Encoding.UTF8.GetBytes(stage.ManifestJson),
            cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(publishedRoot)!);
        EnsureNoSymbolicLinks(storageRoot, Path.GetDirectoryName(publishedRoot)!);
        if (Directory.Exists(publishedRoot))
        {
            if (!await VerifyPublishedAsync(stage, cancellationToken))
            {
                throw new InvalidOperationException("Artifact file-set publish target already exists with a different manifest.");
            }

            Directory.Delete(stagingRoot, recursive: true);
            return;
        }

        Directory.Move(stagingRoot, publishedRoot);
    }

    private async Task WriteJournalAsync(ArtifactFileSetStage stage, CancellationToken cancellationToken)
    {
        var journalRoot = GetStoragePath(JournalRelativeRoot);
        Directory.CreateDirectory(journalRoot);
        EnsureNoSymbolicLinks(storageRoot, journalRoot);
        var target = GetJournalPath(stage.CommitId);
        if (File.Exists(target))
        {
            var existing = JsonSerializer.Deserialize<ArtifactFileSetStage>(
                await File.ReadAllTextAsync(target, cancellationToken),
                JsonOptions);
            if (existing is not null &&
                existing.CommitId == stage.CommitId &&
                string.Equals(existing.ManifestDigest, stage.ManifestDigest, StringComparison.Ordinal) &&
                string.Equals(existing.PublishedReference, stage.PublishedReference, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException("Artifact file-set journal commit id already exists with different content.");
        }

        var temporary = GetSafeChildPath(journalRoot, $"{stage.CommitId:N}.{Guid.NewGuid():N}.tmp");
        await WriteDurablyAsync(
            temporary,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(stage, JsonOptions)),
            cancellationToken);
        File.Move(temporary, target);
    }

    private void DeleteStagePaths(ArtifactFileSetStage stage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stagingRoot = GetStoragePath(stage.StagingReference);
        var publishedRoot = GetWorkspacePath(stage.WorkspaceCode, stage.PublishedReference);
        DeleteDirectoryIfExists(stagingRoot);
        DeleteDirectoryIfExists(publishedRoot);
    }

    private void DeleteJournal(Guid commitId)
    {
        var path = GetJournalPath(commitId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetJournalPath(Guid commitId) =>
        GetSafeChildPath(GetStoragePath(JournalRelativeRoot), $"{commitId:N}.json");

    private string GetWorkspacePath(string workspaceCode, string relativePath)
    {
        ValidateWorkspaceCode(workspaceCode);
        return GetStoragePath($"{workspaceCode}/{ArtifactPathGuard.NormalizeRelativePath(relativePath)}");
    }

    private string GetStoragePath(string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException("Artifact file-set path must be storage-root relative.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(storageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return EnsureContained(storageRoot, fullPath);
    }

    private static string GetSafeChildPath(string root, string relativePath)
    {
        var normalized = ArtifactPathGuard.NormalizeRelativePath(relativePath);
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        return EnsureContained(root, fullPath);
    }

    private static string EnsureContained(string root, string fullPath)
    {
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Artifact file-set path escapes its controlled root.");
        }

        return fullPath;
    }

    private static ArtifactFileSetWriteRequest NormalizeEntry(ArtifactFileSetWriteRequest entry)
    {
        ArgumentNullException.ThrowIfNull(entry.Content);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.MimeType);
        var path = ArtifactPathGuard.NormalizeRelativePath(entry.RelativePath);
        if (string.Equals(path, "manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Artifact file-set entry cannot replace its manifest.");
        }

        return entry with { RelativePath = path, MimeType = entry.MimeType.Trim() };
    }

    private static ArtifactFileSetAuthority NormalizeAuthority(ArtifactFileSetAuthority? authority)
    {
        if (authority is null || authority.TaskId == Guid.Empty)
        {
            throw new InvalidOperationException("Artifact file-set authority must identify its owning task.");
        }

        if (authority.TaskFencingToken < 0 || authority.NodeFencingToken < 0 ||
            authority.NodeRunId.HasValue &&
            (authority.NodeRunId.Value == Guid.Empty ||
             authority.TaskFencingToken <= 0 ||
             authority.NodeFencingToken <= 0))
        {
            throw new InvalidOperationException("Artifact file-set authority fencing is invalid.");
        }

        return authority;
    }

    private static string NormalizePublishArea(string publishArea)
    {
        var normalized = ArtifactPathGuard.NormalizeRelativePath(publishArea).TrimEnd('/');
        var allowed = new[] { "source", "data", "charts", "draft", "final", "logs", "audit" };
        if (!allowed.Any(prefix =>
                normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Artifact file-set publish area is outside controlled workspace folders.");
        }

        return normalized;
    }

    private static void ValidateWorkspaceCode(string workspaceCode)
    {
        if (string.IsNullOrWhiteSpace(workspaceCode) ||
            !workspaceCode.StartsWith("ws_", StringComparison.OrdinalIgnoreCase) ||
            workspaceCode.Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException("Workspace code is invalid.", nameof(workspaceCode));
        }
    }

    private static async Task WriteDurablyAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough
            });
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<string> ComputeFileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static void EnsureNoSymbolicLinks(string root, string target)
    {
        var current = new DirectoryInfo(target);
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        while (current.FullName.Length >= rootPath.Length)
        {
            if (current.Exists && current.LinkTarget is not null)
            {
                throw new InvalidOperationException("Artifact file-set path contains a symbolic link.");
            }

            if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), rootPath, StringComparison.Ordinal))
            {
                return;
            }

            current = current.Parent
                      ?? throw new InvalidOperationException("Artifact file-set path escaped the configured root.");
        }

        throw new InvalidOperationException("Artifact file-set path escaped the configured root.");
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private void EnsureActiveStage(ArtifactFileSetStage stage)
    {
        if (activeLease is null || activeStage != stage)
        {
            throw new InvalidOperationException("Artifact file-set stage does not match the active reconciliation lease.");
        }
    }

    private async Task ReleaseLeaseAsync(Guid commitId)
    {
        var lease = activeLease;
        activeLease = null;
        activeStage = null;
        if (lease is null)
        {
            return;
        }

        try
        {
            await lease.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Artifact file-set reconciliation lease cleanup failed. CommitId={CommitId}; ErrorType={ErrorType}",
                commitId,
                exception.GetType().Name);
        }
    }
}
