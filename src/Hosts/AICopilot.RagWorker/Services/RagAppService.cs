using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.EntityFrameworkCore;
using AICopilot.RagWorker.Services.Parsers;
using AICopilot.Services.Common.Contracts;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.RagWorker.Services;

public class RagAppService(
    IFileStorageService fileStorage,
    DocumentParserFactory parserFactory,
    TextSplitterService textSplitter,
    AiCopilotDbContext dbContext,
    ILogger<RagAppService> logger)
{
    public async Task IndexDocumentAsync(Document document, CancellationToken cancellationToken = new())
    {
        logger.LogInformation("开始索引流程: {DocumentName}", document.Name);

        // Step 1: 加载
        var stream = await LoadDocumentAsync(document, cancellationToken);

        // Step 2: 解析
        var text = await ParseDocumentAsync(document, stream, cancellationToken);
        // Step 3: 分割
        var paragraphs = await SplitDocumentAsync(document, text, cancellationToken);

        logger.LogInformation("文档索引完成: {DocumentName}", document.Name);
    }

    // ================================================================
    // Step 1: 加载
    // ================================================================
    private async Task<Stream> LoadDocumentAsync(Document document, CancellationToken ct)
    {
        logger.LogInformation("加载文档...");

        // 从存储中获取文件流
        var stream = await fileStorage.GetAsync(document.FilePath, ct);

        return stream ?? throw new FileNotFoundException($"文件未找到: {document.FilePath}");
    }

    // ================================================================
    // Step 2: 解析
    // ================================================================
    private async Task<string> ParseDocumentAsync(Document document, Stream stream, CancellationToken ct)
    {
        logger.LogInformation("解析文档...");

        // 根据扩展名获取解析器
        var parser = parserFactory.GetParser(document.Extension);

        // 提取文本
        var text = await parser.ParseAsync(stream, ct);

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("文档内容为空或无法提取文本。");

        logger.LogInformation("文本提取完成，长度: {Length} 字符", text.Length);

        // 更新状态：解析完成 -> 准备切片
        document.CompleteParsing();
        await dbContext.SaveChangesAsync(ct);

        return text;
    }

    // ================================================================
    // Step 3: 切片
    // ================================================================
    private async Task<List<string>> SplitDocumentAsync(Document document, string text, CancellationToken ct)
    {
        logger.LogInformation("开始文本切片...");

        // 为了支持重新索引，如果文档之前处理过，需要先清理旧的切片
        if (document.Chunks.Count > 0)
            document.ClearChunks();

        var paragraphs = textSplitter.Split(text);

        logger.LogInformation("文本切片完成，共 {Count} 个切片。", paragraphs.Count);

        // 将切片转换为领域实体
        for (var i = 0; i < paragraphs.Count; i++)
            document.AddChunk(i, paragraphs[i]);

        await dbContext.SaveChangesAsync(ct);

        return paragraphs;
    }
}