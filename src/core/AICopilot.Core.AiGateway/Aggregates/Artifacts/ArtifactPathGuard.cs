namespace AICopilot.Core.AiGateway.Aggregates.Artifacts;

public static class ArtifactPathGuard
{
    private static readonly string[] DraftPrefixes =
    [
        "source/",
        "data/",
        "charts/",
        "draft/",
        "logs/",
        "audit/"
    ];

    public static string NormalizeDraftPath(string relativePath)
    {
        var path = NormalizeRelativePath(relativePath);
        if (!DraftPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Draft artifacts must stay under the controlled workspace draft/source/data/charts/logs/audit folders.", nameof(relativePath));
        }

        if (path.StartsWith("final/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Draft artifacts cannot be written into final folder.", nameof(relativePath));
        }

        return path;
    }

    public static string NormalizeFinalPath(string relativePath)
    {
        var path = NormalizeRelativePath(relativePath);
        if (!path.StartsWith("final/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Final artifacts must stay under the controlled workspace final folder.", nameof(relativePath));
        }

        return path;
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Artifact relative path is required.", nameof(relativePath));
        }

        var path = relativePath.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(path)
            || path.Contains("../", StringComparison.Ordinal)
            || path.Contains("/..", StringComparison.Ordinal)
            || path.Equals("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Artifact path must be a safe workspace-relative path.", nameof(relativePath));
        }

        return path;
    }
}
