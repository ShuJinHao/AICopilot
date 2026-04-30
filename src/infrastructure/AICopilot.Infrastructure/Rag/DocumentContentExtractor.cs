using AICopilot.Infrastructure.Rag.Parsers;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AICopilot.Infrastructure.Rag;

public sealed class DocumentContentExtractor(
    IFileStorageService fileStorage,
    DocumentParserFactory parserFactory,
    ILogger<DocumentContentExtractor> logger) : IDocumentContentExtractor
{
    public async Task<string> ExtractAsync(
        DocumentContentSource source,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("开始读取并解析文档: {DocumentName}", source.Name);

        await using var stream = await fileStorage.GetAsync(source.FilePath, cancellationToken)
            ?? throw new FileNotFoundException($"文件未找到: {source.FilePath}");

        var parser = parserFactory.GetParser(source.Extension);
        return await parser.ParseAsync(stream, cancellationToken);
    }
}
