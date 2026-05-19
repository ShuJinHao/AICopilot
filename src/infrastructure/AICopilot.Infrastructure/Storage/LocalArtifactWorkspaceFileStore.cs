using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;

namespace AICopilot.Infrastructure.Storage;

public sealed class LocalArtifactWorkspaceFileStore(IConfiguration configuration) : IArtifactWorkspaceFileStore
{
    private static readonly string[] WorkspaceFolders =
    [
        "source",
        "data",
        "charts",
        "draft",
        "final",
        "logs",
        "audit"
    ];

    private readonly string rootPath = ResolveRootPath(configuration);

    public ArtifactWorkspaceStorageSettings GetSettings()
    {
        return new ArtifactWorkspaceStorageSettings(
            rootPath,
            WorkspaceFolders,
            Enum.GetNames<ArtifactType>().Where(name => name != nameof(ArtifactType.Folder)).ToArray(),
            AllowsUserDefinedPath: false);
    }

    public async Task<ArtifactWorkspaceStorageInfo> CreateWorkspaceAsync(
        string workspaceCode,
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkspaceCode(workspaceCode);
        var workspaceRoot = GetWorkspaceRoot(workspaceCode);
        Directory.CreateDirectory(workspaceRoot);

        foreach (var folder in WorkspaceFolders)
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, folder));
        }

        var manifest = new
        {
            workspaceCode,
            taskId,
            createdAt = DateTimeOffset.UtcNow,
            folders = WorkspaceFolders
        };

        var manifestJson = JsonSerializer.Serialize(manifest, JsonSerializerOptions.Web);
        await File.WriteAllTextAsync(
            GetSafeWorkspacePath(workspaceCode, "manifest.json"),
            manifestJson,
            Encoding.UTF8,
            cancellationToken);

        return new ArtifactWorkspaceStorageInfo(
            workspaceRoot,
            $"/api/aigateway/workspace/{Uri.EscapeDataString(workspaceCode)}");
    }

    public async Task<ArtifactFileWriteResult> WriteTextAsync(
        string workspaceCode,
        string relativePath,
        string content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkspaceCode(workspaceCode);
        var normalizedPath = ArtifactPathGuard.NormalizeDraftPath(relativePath);
        var fullPath = GetSafeWorkspacePath(workspaceCode, normalizedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8, cancellationToken);
        var fileInfo = new FileInfo(fullPath);
        return new ArtifactFileWriteResult(normalizedPath, fileInfo.Length, mimeType);
    }

    public async Task<ArtifactFileWriteResult> WriteBytesAsync(
        string workspaceCode,
        string relativePath,
        byte[] content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkspaceCode(workspaceCode);
        var normalizedPath = ArtifactPathGuard.NormalizeDraftPath(relativePath);
        var fullPath = GetSafeWorkspacePath(workspaceCode, normalizedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
        var fileInfo = new FileInfo(fullPath);
        return new ArtifactFileWriteResult(normalizedPath, fileInfo.Length, mimeType);
    }

    public async Task<ArtifactFileWriteResult> CopyAsync(
        string workspaceCode,
        string sourceRelativePath,
        string targetRelativePath,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkspaceCode(workspaceCode);
        var sourcePath = ArtifactPathGuard.NormalizeRelativePath(sourceRelativePath);
        var targetPath = ArtifactPathGuard.NormalizeFinalPath(targetRelativePath);
        var sourceFullPath = GetSafeWorkspacePath(workspaceCode, sourcePath);
        var targetFullPath = GetSafeWorkspacePath(workspaceCode, targetPath);

        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException("Workspace source file does not exist.", sourcePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetFullPath)!);
        await using var input = File.OpenRead(sourceFullPath);
        await using var output = File.Create(targetFullPath);
        await input.CopyToAsync(output, cancellationToken);
        var fileInfo = new FileInfo(targetFullPath);
        return new ArtifactFileWriteResult(targetPath, fileInfo.Length, mimeType);
    }

    public Task<ArtifactFileReadResult?> OpenReadAsync(
        string workspaceCode,
        string relativePath,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkspaceCode(workspaceCode);
        var normalizedPath = ArtifactPathGuard.NormalizeRelativePath(relativePath);
        var fullPath = GetSafeWorkspacePath(workspaceCode, normalizedPath);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<ArtifactFileReadResult?>(null);
        }

        var fileInfo = new FileInfo(fullPath);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<ArtifactFileReadResult?>(new ArtifactFileReadResult(
            stream,
            Path.GetFileName(normalizedPath),
            mimeType,
            fileInfo.Length));
    }

    public Task<IReadOnlyCollection<ArtifactWorkspaceFileItem>> ListAsync(
        string workspaceCode,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkspaceCode(workspaceCode);
        var workspaceRoot = GetWorkspaceRoot(workspaceCode);
        if (!Directory.Exists(workspaceRoot))
        {
            return Task.FromResult<IReadOnlyCollection<ArtifactWorkspaceFileItem>>([]);
        }

        var files = Directory.EnumerateFileSystemEntries(workspaceRoot, "*", SearchOption.AllDirectories)
            .Select(path => ToItem(workspaceRoot, path))
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyCollection<ArtifactWorkspaceFileItem>>(files);
    }

    private ArtifactWorkspaceFileItem ToItem(string workspaceRoot, string path)
    {
        var relativePath = Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/');
        if (Directory.Exists(path))
        {
            return new ArtifactWorkspaceFileItem(
                Path.GetFileName(path),
                relativePath + "/",
                true,
                0,
                new DirectoryInfo(path).LastWriteTimeUtc);
        }

        var fileInfo = new FileInfo(path);
        return new ArtifactWorkspaceFileItem(
            fileInfo.Name,
            relativePath,
            false,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc);
    }

    private string GetWorkspaceRoot(string workspaceCode)
    {
        return GetSafeFullPath(workspaceCode);
    }

    private string GetSafeWorkspacePath(string workspaceCode, string relativePath)
    {
        return GetSafeFullPath(Path.Combine(workspaceCode, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private string GetSafeFullPath(string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException("Workspace path must be relative to the configured root.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var rootWithSeparator = EnsureTrailingSeparator(rootPath);
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Workspace path escapes the configured root.");
        }

        return fullPath;
    }

    private static string ResolveRootPath(IConfiguration configuration)
    {
        var configuredRoot = configuration["ArtifactWorkspace:RootPath"];
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AICopilot",
                "artifact-workspaces")
            : configuredRoot;

        return Path.GetFullPath(root);
    }

    private static void ValidateWorkspaceCode(string workspaceCode)
    {
        if (string.IsNullOrWhiteSpace(workspaceCode)
            || !workspaceCode.StartsWith("ws_", StringComparison.OrdinalIgnoreCase)
            || workspaceCode.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-')))
        {
            throw new ArgumentException("Workspace code is invalid.", nameof(workspaceCode));
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        var separator = Path.DirectorySeparatorChar.ToString();
        return path.EndsWith(separator, StringComparison.Ordinal)
            ? path
            : path + separator;
    }
}
