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
        using var generator = embeddingFactory.CreateGenerator(embeddingModel);
        var queryEmbedding = await generator.GenerateVectorAsync(queryText, cancellationToken: cancellationToken);

        var collectionName = $"kb-{knowledgeBase.Id:N}";
        var vectorSearchCollection = vectorStore.GetCollection<ulong, VectorDocumentRecord>(
            collectionName,
            VectorDocumentDefinition.Get(embeddingModel.Dimensions));

        var searchResults = vectorSearchCollection.SearchAsync(
            queryEmbedding,
            topK,
            cancellationToken: cancellationToken);

        var results = new List<KnowledgeVectorSearchResult>();

        await foreach (var record in searchResults)
        {
            if (record.Score < minScore)
            {
                continue;
            }

            results.Add(new KnowledgeVectorSearchResult(
                record.Record.Text,
                record.Score ?? 0,
                int.Parse(record.Record.DocumentId),
                record.Record.DocumentName));
        }

        return results;
    }
}
