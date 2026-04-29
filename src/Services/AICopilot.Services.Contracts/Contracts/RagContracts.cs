namespace AICopilot.Services.Contracts;

public interface IKnowledgeRetrievalService
{
    Task<IReadOnlyList<KnowledgeRetrievalResult>> SearchAsync(
        Guid knowledgeBaseId,
        string queryText,
        int topK,
        double minScore,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeRetrievalResult(
    string Text,
    double Score,
    int DocumentId,
    string DocumentName);

public interface IDocumentIndexingService
{
    Task IndexAsync(int documentId, CancellationToken cancellationToken = default);
}

public interface IDocumentContentExtractor
{
    Task<string> ExtractAsync(DocumentContentSource source, CancellationToken cancellationToken = default);
}

public sealed record DocumentContentSource(
    string FilePath,
    string Extension,
    string Name);

public interface IDocumentTextSplitter
{
    IReadOnlyList<string> Split(string text);
}

public interface IKnowledgeVectorIndexWriter
{
    Task UpsertAsync(
        KnowledgeVectorIndexRequest request,
        IReadOnlyList<string> chunks,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeVectorIndexRequest(
    int DocumentId,
    Guid KnowledgeBaseId,
    Guid EmbeddingModelId,
    string DocumentName,
    int PreviousChunkCount = 0);
