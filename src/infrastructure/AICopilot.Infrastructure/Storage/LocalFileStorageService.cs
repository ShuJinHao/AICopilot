using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AICopilot.Infrastructure.Storage;

public sealed class LocalFileStorageService(IConfiguration configuration)
    : IFileStorageService, IPersistenceFileReconciliationJournal
{
    private const string UploadRoot = "uploads";
    private const string ReconciliationRoot = ".persistence/file-reconciliation";
    private static readonly JsonSerializerOptions JournalJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string rootPath = ResolveRootPath(configuration);

    public Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafeFullPath(path);

        try
        {
            var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
            return Task.FromResult<Stream?>(stream);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafeFullPath(path);
        cancellationToken.ThrowIfCancellationRequested();
        DeleteFileDurablyIfExists(fullPath);
        return Task.CompletedTask;
    }

    public async Task StageAsync(
        PersistenceFileReconciliationRecord record,
        CancellationToken cancellationToken = default)
    {
        ValidateRecord(record);
        _ = GetSafeFullPath(record.StoragePath);

        var journalDirectory = GetSafeFullPath(ReconciliationRoot);
        CreateDirectoryDurably(journalDirectory);
        var targetPath = GetJournalPath(record.CommitId);
        if (PathExists(targetPath))
        {
            var existing = await ReadJournalAsync(targetPath, cancellationToken);
            if (existing == record)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Persistence file journal '{record.CommitId:N}' already exists with different content.");
        }

        var temporaryPath = Path.Combine(
            journalDirectory,
            $"{record.CommitId:N}.{Guid.NewGuid():N}.tmp");
        var targetMoved = false;
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.None,
                                 Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                             }))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    record,
                    JournalJsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            MoveFileDurably(temporaryPath, targetPath, journalDirectory);
            targetMoved = true;
            File.SetLastWriteTimeUtc(targetPath, record.CreatedAtUtc);
        }
        catch
        {
            if (!targetMoved && !PathExists(temporaryPath) && PathExists(targetPath))
            {
                targetMoved = true;
            }

            DeleteFileDurablyIfExists(temporaryPath);
            if (targetMoved)
            {
                DeleteFileDurablyIfExists(targetPath);
            }

            throw;
        }
    }

    public async Task<PersistenceFileReconciliationSnapshot> GetPendingAsync(
        int maximumEntries,
        DateTime createdBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        if (maximumEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        if (createdBeforeUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Reconciliation cutoff must be UTC.", nameof(createdBeforeUtc));
        }

        var journalDirectory = GetSafeFullPath(ReconciliationRoot);
        if (!DirectoryPathExists(journalDirectory))
        {
            return new PersistenceFileReconciliationSnapshot([], false);
        }

        foreach (var temporaryPath in Directory.EnumerateFiles(journalDirectory, "*.tmp"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(temporaryPath) <= createdBeforeUtc)
            {
                DeleteFileDurablyIfExists(temporaryPath);
            }
        }

        var journalSnapshot = await ReadJournalDirectoryAsync(cancellationToken);
        var records = journalSnapshot.Records
            .Where(item =>
                item.Record.CreatedAtUtc <= createdBeforeUtc &&
                item.LastAttemptAtUtc <= createdBeforeUtc)
            .OrderBy(item => item.LastAttemptAtUtc)
            .ThenBy(item => item.Record.CreatedAtUtc)
            .ThenBy(item => item.Record.CommitId)
            .Take(maximumEntries)
            .Select(item => item.Record)
            .ToArray();
        return new PersistenceFileReconciliationSnapshot(
            records,
            journalSnapshot.HasUnreadableEntries);
    }

    public async Task<PersistenceFileReconciliationRecord?> FindByStoragePathAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        _ = GetSafeFullPath(storagePath);
        var snapshot = await ReadJournalDirectoryAsync(cancellationToken);
        if (snapshot.HasUnreadableEntries)
        {
            throw new InvalidOperationException(
                "Persistence file journals are unreadable; storage-path ownership cannot be determined safely.");
        }

        var matches = snapshot.Records
            .Where(item => string.Equals(
                item.Record.StoragePath,
                storagePath,
                StringComparison.Ordinal))
            .Select(item => item.Record)
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                "Multiple persistence file journals reference the same storage path.")
        };
    }

    public Task<bool> ExistsAsync(Guid commitId, CancellationToken cancellationToken = default)
    {
        ValidateCommitId(commitId);
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetJournalPath(commitId);
        if (PathExists(path))
        {
            return Task.FromResult(true);
        }

        var journalDirectory = GetSafeFullPath(ReconciliationRoot);
        if (DirectoryPathExists(journalDirectory))
        {
            FlushDirectoryDurably(journalDirectory);
        }

        return Task.FromResult(false);
    }

    public Task MarkAttemptedAsync(
        Guid commitId,
        DateTime attemptedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateCommitId(commitId);
        if (attemptedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Journal attempt timestamp must be UTC.", nameof(attemptedAtUtc));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var path = GetJournalPath(commitId);
        if (PathExists(path))
        {
            File.SetLastWriteTimeUtc(path, attemptedAtUtc);
        }

        return Task.CompletedTask;
    }

    public Task CompleteAsync(Guid commitId, CancellationToken cancellationToken = default)
    {
        ValidateCommitId(commitId);
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetJournalPath(commitId);
        DeleteFileDurablyIfExists(path);

        return Task.CompletedTask;
    }

    internal string CreateUploadPath(string fileName)
    {
        var safeFileName = UploadFileNamePolicy.Normalize(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        var datePath = DateTime.UtcNow.ToString("yyyy/MM/dd");
        var uniqueFileName = $"{Guid.NewGuid():N}_{safeFileName}";
        return Path.Combine(UploadRoot, datePath, uniqueFileName).Replace("\\", "/");
    }

    internal async Task SaveAtAsync(
        Stream source,
        string relativePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var fullPath = GetSafeFullPath(relativePath);
        CreateDirectoryDurably(Path.GetDirectoryName(fullPath)!);

        await using (var target = new FileStream(
                         fullPath,
                         new FileStreamOptions
                         {
                             Mode = FileMode.CreateNew,
                             Access = FileAccess.Write,
                             Share = FileShare.None,
                             Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                         }))
        {
            if (source.CanSeek)
            {
                source.Position = 0;
            }

            await source.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
        }

        FlushDirectoryDurably(Path.GetDirectoryName(fullPath)!);
    }

    private string GetSafeFullPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Storage path is required.", nameof(relativePath));
        }

        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException("Storage path must be relative to the configured root.");
        }

        var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (normalizedPath
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Storage path cannot contain parent traversal segments.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalizedPath));
        var rootWithSeparator = EnsureTrailingSeparator(rootPath);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(rootWithSeparator, pathComparison))
        {
            throw new InvalidOperationException("Storage path escapes the configured root.");
        }

        EnsureNoLinkTraversal(fullPath);

        return fullPath;
    }

    private void EnsureNoLinkTraversal(string fullPath)
    {
        EnsurePathSegmentIsNotLink(rootPath);
        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        var currentPath = rootPath;
        foreach (var segment in relativePath.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            EnsurePathSegmentIsNotLink(currentPath);
        }
    }

    private static void EnsurePathSegmentIsNotLink(string path)
    {
        var directoryInfo = new DirectoryInfo(path);
        directoryInfo.Refresh();
        if (directoryInfo.LinkTarget is not null)
        {
            throw new InvalidOperationException(
                "Storage path cannot traverse symbolic links or reparse points.");
        }

        var fileInfo = new FileInfo(path);
        fileInfo.Refresh();
        if (fileInfo.LinkTarget is not null ||
            (fileInfo.Exists && (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0))
        {
            throw new InvalidOperationException(
                "Storage path cannot traverse symbolic links or reparse points.");
        }
    }

    private string GetJournalPath(Guid commitId)
    {
        ValidateCommitId(commitId);
        return GetSafeFullPath(Path.Combine(ReconciliationRoot, $"{commitId:N}.json"));
    }

    private static async Task<PersistenceFileReconciliationRecord> ReadJournalAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        return await JsonSerializer.DeserializeAsync<PersistenceFileReconciliationRecord>(
                   stream,
                   JournalJsonOptions,
                   cancellationToken)
               ?? throw new JsonException("Persistence file journal is empty.");
    }

    private async Task<JournalDirectorySnapshot> ReadJournalDirectoryAsync(
        CancellationToken cancellationToken)
    {
        var journalDirectory = GetSafeFullPath(ReconciliationRoot);
        if (!DirectoryPathExists(journalDirectory))
        {
            return new JournalDirectorySnapshot([], false);
        }

        var records = new List<ScheduledReconciliationRecord>();
        var hasUnreadableEntries = false;
        foreach (var path in Directory
                     .EnumerateFiles(journalDirectory, "*.json")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(fileName, "N", out var fileCommitId))
            {
                hasUnreadableEntries = true;
                continue;
            }

            try
            {
                var record = await ReadJournalAsync(path, cancellationToken);
                ValidateRecord(record);
                _ = GetSafeFullPath(record.StoragePath);
                if (record.CommitId != fileCommitId)
                {
                    hasUnreadableEntries = true;
                    continue;
                }

                records.Add(new ScheduledReconciliationRecord(
                    record,
                    File.GetLastWriteTimeUtc(path)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException or
                    ArgumentException or InvalidOperationException)
            {
                hasUnreadableEntries = true;
            }
        }

        FlushDirectoryDurably(journalDirectory);
        return new JournalDirectorySnapshot(records, hasUnreadableEntries);
    }

    private static void ValidateRecord(PersistenceFileReconciliationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateCommitId(record.CommitId);
        if (string.IsNullOrWhiteSpace(record.StoragePath))
        {
            throw new ArgumentException("Storage path is required.", nameof(record));
        }

        if (record.CreatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Journal creation timestamp must be UTC.", nameof(record));
        }
    }

    private static void ValidateCommitId(Guid commitId)
    {
        if (commitId == Guid.Empty)
        {
            throw new ArgumentException("Persistence commit id is required.", nameof(commitId));
        }
    }

    private static void DeleteFileDurablyIfExists(string path)
    {
        var directoryPath = Path.GetDirectoryName(path)
            ?? throw new DirectoryNotFoundException("Persistence file does not have a parent directory.");
        if (!PathExists(path))
        {
            if (DirectoryPathExists(directoryPath))
            {
                FlushDirectoryDurably(directoryPath);
            }

            return;
        }

        File.Delete(path);
        FlushDirectoryDurably(directoryPath);
    }

    private static bool PathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static bool DirectoryPathExists(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void MoveFileDurably(string sourcePath, string targetPath, string directoryPath)
    {
        File.Move(sourcePath, targetPath, overwrite: false);
        FlushDirectoryDurably(directoryPath);
    }

    private static void CreateDirectoryDurably(string directoryPath)
    {
        var missingDirectories = new Stack<string>();
        var currentPath = Path.GetFullPath(directoryPath);
        while (!Directory.Exists(currentPath))
        {
            missingDirectories.Push(currentPath);
            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(parentPath) || parentPath == currentPath)
            {
                throw new DirectoryNotFoundException(
                    "Unable to find an existing parent for the persistence directory.");
            }

            currentPath = parentPath;
        }

        while (missingDirectories.TryPop(out var missingPath))
        {
            var parentPath = Path.GetDirectoryName(missingPath)
                ?? throw new DirectoryNotFoundException(
                    "Persistence directory does not have a parent path.");
            Directory.CreateDirectory(missingPath);
            FlushDirectoryDurably(parentPath);
            FlushDirectoryDurably(missingPath);
        }
    }

    private static void FlushDirectoryDurably(string directoryPath)
    {
        var descriptor = OperatingSystem.IsMacOS()
            ? MacOpen(directoryPath, OpenReadOnly)
            : UnixOpen(directoryPath, OpenReadOnly);
        if (descriptor < 0)
        {
            throw NativeIoException("open");
        }

        try
        {
            var result = OperatingSystem.IsMacOS()
                ? MacFsync(descriptor)
                : UnixFsync(descriptor);
            if (result != 0)
            {
                throw NativeIoException("fsync");
            }
        }
        finally
        {
            if (OperatingSystem.IsMacOS())
            {
                _ = MacClose(descriptor);
            }
            else
            {
                _ = UnixClose(descriptor);
            }
        }
    }

    private static IOException NativeIoException(string operation)
    {
        return new IOException(
            $"Unable to {operation} the persistence directory metadata.",
            new Win32Exception(Marshal.GetLastWin32Error()));
    }

    private static string ResolveRootPath(IConfiguration configuration)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Durable local persistence storage is supported only on Linux and macOS. " +
                "Run the standard Linux container deployment or provide a separately governed storage backend.");
        }

        var configuredRoot = configuration["FileStorage:RootPath"];
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AICopilot",
                "storage")
            : configuredRoot;

        return Path.GetFullPath(root);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        var separator = Path.DirectorySeparatorChar.ToString();
        return path.EndsWith(separator, StringComparison.Ordinal)
            ? path
            : path + separator;
    }

    private sealed record ScheduledReconciliationRecord(
        PersistenceFileReconciliationRecord Record,
        DateTime LastAttemptAtUtc);

    private sealed record JournalDirectorySnapshot(
        IReadOnlyList<ScheduledReconciliationRecord> Records,
        bool HasUnreadableEntries);

    private const int OpenReadOnly = 0;
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int UnixOpen(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int UnixFsync(int descriptor);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int UnixClose(int descriptor);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "open", SetLastError = true)]
    private static extern int MacOpen(string path, int flags);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "fsync", SetLastError = true)]
    private static extern int MacFsync(int descriptor);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "close", SetLastError = true)]
    private static extern int MacClose(int descriptor);
}
