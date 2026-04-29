using AICopilot.Embedding;
using AICopilot.Embedding.Models;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Specifications.EmbeddingModel;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace AICopilot.Infrastructure.Rag;

public sealed class KnowledgeVectorIndexWriter(
    IReadRepository<EmbeddingModel> embeddingModelRepository,
    EmbeddingGeneratorFactory embeddingFactory,
    VectorStore vectorStore,
    ILogger<KnowledgeVectorIndexWriter> logger) : IKnowledgeVectorIndexWriter
{
    public async Task UpsertAsync(
        KnowledgeVectorIndexRequest request,
        IReadOnlyList<string> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            logger.LogWarning("文档 {DocumentId} 没有切片需要写入向量库。", request.DocumentId);
            return;
        }

        var embeddingModel = await embeddingModelRepository.FirstOrDefaultAsync(
            new EmbeddingModelByIdSpec(request.EmbeddingModelId),
            cancellationToken);
        if (embeddingModel is null)
        {
            throw new InvalidOperationException($"未找到 ID 为 {request.EmbeddingModelId} 的嵌入模型配置。");
        }

        using var generator = embeddingFactory.CreateGenerator(embeddingModel);
        var embeddings = await GenerateEmbeddingsAsync(generator, chunks, cancellationToken);
        var dimensions = embeddings.First().Vector.Length;
        var collectionName = $"kb-{request.KnowledgeBaseId:N}";
        var definition = VectorDocumentDefinition.Get(dimensions);
        var collection = vectorStore.GetDynamicCollection(collectionName, definition);

        await collection.EnsureCollectionExistsAsync(cancellationToken);

        var staleRecordKeys = Enumerable
            .Range(0, Math.Max(request.PreviousChunkCount, chunks.Count))
            .Select(index => (object)BuildRecordKey(request.DocumentId, index))
            .ToArray();
        if (staleRecordKeys.Length > 0)
        {
            await collection.DeleteAsync(staleRecordKeys, cancellationToken);
        }

        var records = chunks
            .Select((chunk, index) => new Dictionary<string, object?>
            {
                ["Key"] = BuildRecordKey(request.DocumentId, index),
                ["Text"] = chunk,
                ["DocumentId"] = request.DocumentId.ToString(),
                ["DocumentName"] = request.DocumentName,
                ["KnowledgeBaseId"] = request.KnowledgeBaseId.ToString(),
                ["ChunkIndex"] = index,
                ["Embedding"] = embeddings[index].Vector
            })
            .ToArray();

        await collection.UpsertAsync(records, cancellationToken);

        logger.LogInformation(
            "文档 {DocumentId} 已向向量集合 {CollectionName} 写入 {Count} 条记录。",
            request.DocumentId,
            collectionName,
            chunks.Count);
    }

    private static ulong BuildRecordKey(int documentId, int chunkIndex)
    {
        return ((ulong)(uint)documentId << 32) | (uint)chunkIndex;
    }

    private async Task<List<Embedding<float>>> GenerateEmbeddingsAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        IReadOnlyList<string> chunks,
        CancellationToken cancellationToken)
    {
        const int batchSize = 5;
        var embeddings = new List<Embedding<float>>();
        var batches = chunks.Chunk(batchSize).ToArray();

        for (var index = 0; index < batches.Length; index++)
        {
            logger.LogInformation("正在生成第 {Current}/{Total} 批向量。", index + 1, batches.Length);
            var result = await generator.GenerateAsync(batches[index], cancellationToken: cancellationToken);
            embeddings.AddRange(result);
        }

        return embeddings;
    }
}
