using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Specifications.KnowledgeBase;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using Microsoft.Extensions.Logging;

namespace AICopilot.RagService.Documents;

public sealed class DocumentIndexingService(
    IRepository<KnowledgeBase> repository,
    IDocumentContentExtractor contentExtractor,
    IDocumentTextSplitter textSplitter,
    IKnowledgeVectorIndexWriter vectorIndexWriter,
    ILogger<DocumentIndexingService> logger) : IDocumentIndexingService
{
    public async Task IndexAsync(int documentId, CancellationToken cancellationToken = default)
    {
        var knowledgeBase = await repository.FirstOrDefaultAsync(
            new KnowledgeBaseByDocumentIdWithDocumentChunksSpec(documentId),
            cancellationToken);

        var document = knowledgeBase?.Documents.FirstOrDefault(item => item.Id == documentId);
        if (knowledgeBase is null || document is null)
        {
            logger.LogWarning("文档 {DocumentId} 未在数据库中找到，跳过索引。", documentId);
            return;
        }

        if (!CanStartOrRecoverIndexing(document.Status))
        {
            logger.LogInformation("文档 {DocumentId} 当前状态为 {Status}，无需重复索引。", documentId, document.Status);
            return;
        }

        try
        {
            logger.LogInformation("开始索引文档 {DocumentId}: {DocumentName}", document.Id, document.Name);

            document.StartParsing();
            repository.Update(knowledgeBase);
            await repository.SaveChangesAsync(cancellationToken);

            var text = await contentExtractor.ExtractAsync(
                new DocumentContentSource(document.FilePath, document.Extension, document.Name),
                cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("文档内容为空或无法提取文本。");
            }

            document.CompleteParsing();
            await repository.SaveChangesAsync(cancellationToken);

            var previousChunkCount = document.ChunkCount;
            if (document.Chunks.Count > 0)
            {
                document.ClearChunks();
            }

            var chunks = textSplitter.Split(text);
            for (var index = 0; index < chunks.Count; index++)
            {
                document.AddChunk(index, chunks[index]);
            }

            await repository.SaveChangesAsync(cancellationToken);

            document.StartEmbedding();
            await repository.SaveChangesAsync(cancellationToken);

            await vectorIndexWriter.UpsertAsync(
                new KnowledgeVectorIndexRequest(
                    document.Id,
                    document.KnowledgeBaseId,
                    knowledgeBase.EmbeddingModelId,
                    document.Name,
                    previousChunkCount),
                chunks,
                cancellationToken);

            document.MarkAsIndexed();
            await repository.SaveChangesAsync(cancellationToken);
            logger.LogInformation("文档 {DocumentId} 索引完成。", document.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "文档 {DocumentId} 索引失败。", document.Id);
            document.MarkAsFailed(ex.Message);
            await repository.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool CanStartOrRecoverIndexing(DocumentStatus status)
    {
        return status is DocumentStatus.Pending
            or DocumentStatus.Failed
            or DocumentStatus.Parsing
            or DocumentStatus.Splitting
            or DocumentStatus.Embedding;
    }
}
