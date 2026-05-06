using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Embedding.Models;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace AICopilot.Embedding;

public sealed class KnowledgeVectorSearchService(
    EmbeddingGeneratorFactory embeddingFactory,
    VectorStore vectorStore) : IKnowledgeVectorSearchService
{
    public async Task<IReadOnlyList<KnowledgeVectorSearchResult>> SearchAsync(
        KnowledgeBase knowledgeBase,
        EmbeddingModel embeddingModel,
        string queryText,
        int topK,
        double minScore,
        CancellationToken cancellationToken = default)
    {
        var effectiveTopK = Math.Clamp(topK, 1, 20);
        var effectiveMinScore = Math.Clamp(minScore, 0.0, 1.0);

        using var generator = embeddingFactory.CreateGenerator(embeddingModel);
        var queryEmbedding = await generator.GenerateVectorAsync(queryText, cancellationToken: cancellationToken);

        var collectionName = $"kb-{knowledgeBase.Id.Value:N}";
        var vectorSearchCollection = vectorStore.GetCollection<ulong, VectorDocumentRecord>(
            collectionName,
            VectorDocumentDefinition.Get(embeddingModel.Dimensions));

        var searchResults = vectorSearchCollection.SearchAsync(
            queryEmbedding,
            effectiveTopK,
            cancellationToken: cancellationToken);

        var results = new List<KnowledgeVectorSearchResult>();

        await foreach (var record in searchResults)
        {
            if (!record.Score.HasValue || record.Score.Value < effectiveMinScore)
            {
                continue;
            }

            results.Add(new KnowledgeVectorSearchResult(
                record.Record.Text,
                record.Score.Value,
                int.Parse(record.Record.DocumentId),
                record.Record.DocumentName,
                record.Record.ChunkIndex));
        }

        return results;
    }
}
