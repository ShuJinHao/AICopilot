using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;

namespace AICopilot.Services.Contracts;

public interface IKnowledgeVectorSearchService
{
    Task<IReadOnlyList<KnowledgeVectorSearchResult>> SearchAsync(
        KnowledgeBase knowledgeBase,
        EmbeddingModel embeddingModel,
        string queryText,
        int topK,
        double minScore,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeVectorSearchResult(
    string Text,
    double Score,
    int DocumentId,
    string DocumentName,
    int ChunkIndex);
