using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

internal static class ArtifactVersioningFiles
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<Result<ArtifactVersionDto[]>> ListVersionsAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        CancellationToken cancellationToken)
    {
        var versions = new List<ArtifactVersionDto>();
        var indexedVersions = await ReadVersionIndexAsync(
            fileStore,
            workspaceCode,
            artifact,
            cancellationToken);
        if (!indexedVersions.IsSuccess)
        {
            return Result.From(indexedVersions);
        }

        if (indexedVersions.Value is not null)
        {
            versions.AddRange(indexedVersions.Value.Versions.Select(metadata =>
                ToDto(artifact.Id.Value, metadata, isCurrent: false)));
        }
        else
        {
            for (var version = 1; version < artifact.Version; version++)
            {
                var metadata = await ReadLegacyMetadataAsync(
                    fileStore,
                    workspaceCode,
                    artifact,
                    version,
                    cancellationToken);
                if (metadata.IsSuccess && metadata.Value is not null)
                {
                    versions.Add(ToDto(artifact.Id.Value, metadata.Value, isCurrent: false));
                }
            }
        }

        var currentFile = await fileStore.OpenReadAsync(
            workspaceCode,
            artifact.RelativePath,
            artifact.MimeType,
            cancellationToken);
        if (currentFile is null)
        {
            return Result.NotFound("Artifact current file does not exist.");
        }

        await using (currentFile.Stream)
        {
            var sha256 = await ComputeSha256Async(currentFile.Stream, cancellationToken);
            versions.Add(new ArtifactVersionDto(
                artifact.Version,
                currentFile.FileName,
                currentFile.FileSize,
                currentFile.MimeType,
                sha256,
                artifact.UpdatedAt,
                true,
                DownloadUrl(artifact.Id.Value, artifact.Version)));
        }

        return Result.Success(versions.OrderBy(item => item.Version).ToArray());
    }

    public static async Task<Result<ArtifactDownloadDto>> OpenVersionAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        int version,
        CancellationToken cancellationToken)
    {
        var path = await ResolveVersionPathAsync(fileStore, workspaceCode, artifact, version, cancellationToken);
        if (!path.IsSuccess)
        {
            return Result.From(path);
        }

        var resolved = path.Value!;
        var file = await fileStore.OpenReadAsync(
            workspaceCode,
            resolved.RelativePath,
            resolved.MimeType,
            cancellationToken);
        if (file is null)
        {
            return Result.NotFound("Artifact version file does not exist.");
        }

        return Result.Success(new ArtifactDownloadDto(file.Stream, resolved.FileName, resolved.MimeType, file.FileSize));
    }

    public static async Task<Result<string>> ReadVersionTextAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        int version,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var path = await ResolveVersionPathAsync(fileStore, workspaceCode, artifact, version, cancellationToken);
        if (!path.IsSuccess)
        {
            return Result.From(path);
        }

        var resolved = path.Value!;
        return await ReadTextAsync(
            fileStore,
            workspaceCode,
            resolved.RelativePath,
            resolved.MimeType,
            maxBytes,
            cancellationToken);
    }

    public static async Task<Result<string>> ReadTextAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        string relativePath,
        string mimeType,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var file = await fileStore.OpenReadAsync(workspaceCode, relativePath, mimeType, cancellationToken);
        if (file is null)
        {
            return Result.NotFound("Artifact file does not exist.");
        }

        await using var stream = file.Stream;
        if (file.FileSize > maxBytes)
        {
            return Result.Invalid($"Artifact text exceeds the {maxBytes} byte read limit.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Result.Success(await reader.ReadToEndAsync(cancellationToken));
    }

    public static async Task<Result<ArtifactVersionFileSetDraft>> PrepareAtomicUpdateAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        string newContent,
        string? comment,
        CancellationToken cancellationToken)
    {
        var currentContent = await ReadTextAsync(
            fileStore,
            workspaceCode,
            artifact.RelativePath,
            artifact.MimeType,
            ArtifactVersioningPolicy.MaxContentBytes,
            cancellationToken);
        if (!currentContent.IsSuccess)
        {
            return Result.From(currentContent);
        }

        var indexedVersions = await ReadVersionIndexAsync(
            fileStore,
            workspaceCode,
            artifact,
            cancellationToken);
        if (!indexedVersions.IsSuccess)
        {
            return Result.From(indexedVersions);
        }

        var history = indexedVersions.Value?.Versions.ToList() ?? [];
        if (indexedVersions.Value is null)
        {
            for (var version = 1; version < artifact.Version; version++)
            {
                var legacy = await ReadLegacyMetadataAsync(
                    fileStore,
                    workspaceCode,
                    artifact,
                    version,
                    cancellationToken);
                if (!legacy.IsSuccess)
                {
                    return Result.From(legacy);
                }

                if (legacy.Value is not null)
                {
                    history.Add(legacy.Value);
                    continue;
                }

                return Result.Invalid($"Artifact version {version} metadata is missing.");
            }
        }

        var metadata = new ArtifactVersionMetadata(
            artifact.Version,
            Path.GetFileName(artifact.RelativePath),
            artifact.RelativePath,
            artifact.RelativePath,
            Encoding.UTF8.GetByteCount(currentContent.Value!),
            artifact.MimeType,
            ComputeSha256(currentContent.Value!),
            DateTimeOffset.UtcNow,
            NormalizeComment(comment));
        history.RemoveAll(item => item.Version == artifact.Version);
        history.Add(metadata);
        history.Sort(static (left, right) => left.Version.CompareTo(right.Version));

        var currentFileName = Path.GetFileName(artifact.RelativePath);
        var currentRelativePath = $"current/{currentFileName}";
        var indexRelativePath = "versions/index.json";
        var index = new ArtifactVersionIndex(
            "artifact-version-index-v1",
            artifact.Id.Value,
            artifact.Version + 1,
            history);
        return Result.Success(new ArtifactVersionFileSetDraft(
            currentRelativePath,
            [
                new ArtifactFileSetWriteRequest(
                    currentRelativePath,
                    Encoding.UTF8.GetBytes(newContent),
                    artifact.MimeType),
                new ArtifactFileSetWriteRequest(
                    indexRelativePath,
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(index, JsonOptions)),
                    "application/json")
            ]));
    }

    public static string ComputeSha256(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    private static async Task<Result<ResolvedVersionPath>> ResolveVersionPathAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        int version,
        CancellationToken cancellationToken)
    {
        if (version <= 0 || version > artifact.Version)
        {
            return Result.Invalid("Artifact version is out of range.");
        }

        if (version == artifact.Version)
        {
            return Result.Success(new ResolvedVersionPath(
                artifact.RelativePath,
                Path.GetFileName(artifact.RelativePath),
                artifact.MimeType));
        }

        var index = await ReadVersionIndexAsync(fileStore, workspaceCode, artifact, cancellationToken);
        if (!index.IsSuccess)
        {
            return Result.From(index);
        }

        var metadata = index.Value?.Versions.FirstOrDefault(item => item.Version == version) is { } indexed
            ? Result.Success<ArtifactVersionMetadata?>(indexed)
            : await ReadLegacyMetadataAsync(fileStore, workspaceCode, artifact, version, cancellationToken);
        if (!metadata.IsSuccess || metadata.Value is null)
        {
            return Result.NotFound("Artifact version metadata does not exist.");
        }

        return Result.Success(new ResolvedVersionPath(
            metadata.Value.ContentRelativePath,
            metadata.Value.FileName,
            metadata.Value.MimeType));
    }

    private static async Task<Result<ArtifactVersionMetadata?>> ReadLegacyMetadataAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        int version,
        CancellationToken cancellationToken)
    {
        var file = await fileStore.OpenReadAsync(
            workspaceCode,
            GetMetadataPath(artifact, version),
            "application/json",
            cancellationToken);
        if (file is null)
        {
            return Result.Success<ArtifactVersionMetadata?>(null);
        }

        await using var stream = file.Stream;
        try
        {
            var metadata = await JsonSerializer.DeserializeAsync<ArtifactVersionMetadata>(
                stream,
                JsonOptions,
                cancellationToken);
            return metadata is null
                ? Result.Invalid("Artifact version metadata is empty.")
                : Result.Success<ArtifactVersionMetadata?>(metadata);
        }
        catch (JsonException)
        {
            return Result.Invalid("Artifact version metadata is invalid.");
        }
    }

    private static async Task<Result<ArtifactVersionIndex?>> ReadVersionIndexAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        CancellationToken cancellationToken)
    {
        var indexPath = TryGetVersionIndexPath(artifact.RelativePath);
        if (indexPath is null)
        {
            return Result.Success<ArtifactVersionIndex?>(null);
        }

        var file = await fileStore.OpenReadAsync(
            workspaceCode,
            indexPath,
            "application/json",
            cancellationToken);
        if (file is null)
        {
            return Result.Invalid("Artifact version index is missing from its committed file set.");
        }

        await using var stream = file.Stream;
        try
        {
            var index = await JsonSerializer.DeserializeAsync<ArtifactVersionIndex>(
                stream,
                JsonOptions,
                cancellationToken);
            if (index is null ||
                !string.Equals(index.SchemaVersion, "artifact-version-index-v1", StringComparison.Ordinal) ||
                index.ArtifactId != artifact.Id.Value ||
                index.CurrentVersion != artifact.Version ||
                index.Versions.Count != artifact.Version - 1 ||
                index.Versions.Any(item => item.Version <= 0 || item.Version >= artifact.Version) ||
                index.Versions.Select(item => item.Version).Distinct().Count() != index.Versions.Count ||
                !index.Versions.OrderBy(item => item.Version).Select(item => item.Version)
                    .SequenceEqual(Enumerable.Range(1, artifact.Version - 1)))
            {
                return Result.Invalid("Artifact version index is invalid.");
            }

            return Result.Success<ArtifactVersionIndex?>(index);
        }
        catch (JsonException)
        {
            return Result.Invalid("Artifact version index is invalid.");
        }
    }

    private static string? TryGetVersionIndexPath(string currentRelativePath)
    {
        var normalized = ArtifactPathGuard.NormalizeRelativePath(currentRelativePath);
        var marker = "/.committed/";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var commitSegmentStart = markerIndex + marker.Length;
        var commitSegmentEnd = normalized.IndexOf('/', commitSegmentStart);
        if (commitSegmentEnd <= commitSegmentStart ||
            !Guid.TryParseExact(normalized[commitSegmentStart..commitSegmentEnd], "N", out _))
        {
            return null;
        }

        if (!normalized[(commitSegmentEnd + 1)..].StartsWith("current/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{normalized[..commitSegmentEnd]}/versions/index.json";
    }

    private static ArtifactVersionDto ToDto(Guid artifactId, ArtifactVersionMetadata metadata, bool isCurrent)
    {
        return new ArtifactVersionDto(
            metadata.Version,
            metadata.FileName,
            metadata.FileSize,
            metadata.MimeType,
            metadata.Sha256,
            metadata.CreatedAt,
            isCurrent,
            DownloadUrl(artifactId, metadata.Version));
    }

    private static string GetVersionRoot(Artifact artifact, int version)
    {
        return $"draft/.versions/{artifact.Id.Value:N}/v{version}";
    }

    private static string GetMetadataPath(Artifact artifact, int version)
    {
        return $"{GetVersionRoot(artifact, version)}/metadata.json";
    }

    private static string DownloadUrl(Guid artifactId, int version)
    {
        return $"/api/aigateway/artifact/{artifactId}/versions/{version}/download";
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var normalized = comment.Trim();
        return normalized.Length <= 300 ? normalized : normalized[..300];
    }

    private sealed record ResolvedVersionPath(string RelativePath, string FileName, string MimeType);
}

internal sealed record ArtifactVersionMetadata(
    int Version,
    string FileName,
    string OriginalRelativePath,
    string ContentRelativePath,
    long FileSize,
    string MimeType,
    string Sha256,
    DateTimeOffset CreatedAt,
    string? Comment);

internal sealed record ArtifactVersionIndex(
    string SchemaVersion,
    Guid ArtifactId,
    int CurrentVersion,
    IReadOnlyList<ArtifactVersionMetadata> Versions);

internal sealed record ArtifactVersionFileSetDraft(
    string CurrentRelativePath,
    IReadOnlyList<ArtifactFileSetWriteRequest> Files);
