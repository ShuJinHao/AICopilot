using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;

namespace AICopilot.Infrastructure.Storage;

public class LocalFileStorageService(IConfiguration configuration) : IFileStorageService
{
    private const string UploadRoot = "uploads";
    private readonly string rootPath = ResolveRootPath(configuration);

    public async Task<string> SaveAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        var datePath = DateTime.Now.ToString("yyyy/MM/dd");
        var uniqueFileName = $"{Guid.NewGuid()}_{safeFileName}";
        var relativePath = Path.Combine(UploadRoot, datePath);
        var fullDirectory = GetSafeFullPath(relativePath);
        Directory.CreateDirectory(fullDirectory);

        var fullPath = GetSafeFullPath(Path.Combine(relativePath, uniqueFileName));

        await using var fileStream = new FileStream(fullPath, FileMode.Create);
        if (stream.CanSeek) stream.Position = 0;
        await stream.CopyToAsync(fileStream, cancellationToken);

        return Path.Combine(relativePath, uniqueFileName).Replace("\\", "/");
    }

    public Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafeFullPath(path);

        if (!File.Exists(fullPath)) return Task.FromResult<Stream?>(null);

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafeFullPath(path);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
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
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalizedPath));
        var rootWithSeparator = EnsureTrailingSeparator(rootPath);

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage path escapes the configured root.");
        }

        return fullPath;
    }

    private static string ResolveRootPath(IConfiguration configuration)
    {
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
}
