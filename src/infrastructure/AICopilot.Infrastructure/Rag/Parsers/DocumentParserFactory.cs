using AICopilot.Services.Contracts;

namespace AICopilot.Infrastructure.Rag.Parsers;

public class DocumentParserFactory(IEnumerable<IDocumentParser> parserSource) : IDocumentFormatPolicy
{
    private readonly IReadOnlyList<IDocumentParser> parsers = parserSource.ToArray();

    public IReadOnlyCollection<string> SupportedExtensions { get; } = parserSource
        .SelectMany(parser => parser.SupportedExtensions)
        .Select(NormalizeExtension)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IDocumentParser GetParser(string extension)
    {
        var ext = NormalizeExtension(extension);

        var parser = parsers.FirstOrDefault(p =>
            p.SupportedExtensions.Any(e =>
                string.Equals(NormalizeExtension(e), ext, StringComparison.OrdinalIgnoreCase)));

        return parser ?? throw new NotSupportedException($"不支持的文件格式: {extension}");
    }

    public bool IsSupported(string extension)
    {
        var ext = NormalizeExtension(extension);
        return SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var ext = extension.Trim().ToLowerInvariant();
        return ext.StartsWith('.') ? ext : $".{ext}";
    }
}
