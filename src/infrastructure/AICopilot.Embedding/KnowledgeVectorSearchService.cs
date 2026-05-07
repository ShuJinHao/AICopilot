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
        var finalPromptDocuments = knowledgeBase.Documents
            .Where(document => document.CanEnterFinalPrompt(DateTime.UtcNow))
            .ToDictionary(document => document.Id.Value);

        await foreach (var record in searchResults)
        {
            if (!record.Score.HasValue || record.Score.Value < effectiveMinScore)
            {
                continue;
            }

            if (!int.TryParse(record.Record.DocumentId, out var documentId) ||
                !finalPromptDocuments.TryGetValue(documentId, out var document))
            {
                continue;
            }

            results.Add(new KnowledgeVectorSearchResult(
                record.Record.Text,
                record.Score.Value,
                document.Id.Value,
                document.Name,
                record.Record.ChunkIndex));
        }

        return results;
    }
}
