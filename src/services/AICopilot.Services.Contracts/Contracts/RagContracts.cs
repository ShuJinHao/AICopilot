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

public interface IKnowledgeBaseAccessChecker
{
    Task<bool> CanReadAsync(
        Guid knowledgeBaseId,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    Task<bool> CanWriteAsync(
        Guid knowledgeBaseId,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeRetrievalResult(
    string Text,
    double Score,
    int DocumentId,
    string DocumentName,
    int ChunkIndex,
    bool IsLowConfidence,
    string? LowConfidenceReason,
    IReadOnlyCollection<KnowledgeSupplementHitDto>? SupplementHits = null,
    KnowledgeRetrievalGovernanceEvidenceDto? GovernanceEvidence = null);

public sealed record KnowledgeCategoryDto(
    Guid Id,
    string Name,
    string BusinessDomain,
    string Visibility,
    string Department,
    int Priority,
    bool IsEnabled);

public sealed record KnowledgeDocumentVersionDto(
    Guid DocumentGroupId,
    int VersionNo,
    DateTime? EffectiveAt,
    DateTime? ExpiredAt,
    int? SupersededByDocumentId,
    string Status);

public sealed record KnowledgeSupplementDto(
    Guid Id,
    string Title,
    string Content,
    string Priority,
    DateTime? EffectiveAt,
    DateTime? ExpiredAt,
    Guid? CategoryId,
    int? DocumentId,
    bool IsEnabled);

public sealed record KnowledgeSupplementHitDto(
    Guid SupplementId,
    string Title,
    string Priority,
    string Content,
    Guid? CategoryId,
    int? DocumentId,
    string? ContentHash = null,
    Guid? SourceDocumentGroupId = null,
    int? SourceDocumentVersionNo = null,
    string? WarningCode = null);

public sealed record KnowledgeDocumentCitationDto(
    int DocumentId,
    string DocumentName,
    int ChunkIndex,
    Guid DocumentGroupId,
    int VersionNo,
    string Classification,
    string SourceType,
    Guid? CategoryId,
    string CitationHash);

public sealed record KnowledgeRetrievalGovernanceEvidenceDto(
    IReadOnlyCollection<KnowledgeDocumentCitationDto> Citations,
    IReadOnlyCollection<string> WarningCodes,
    bool HasGovernanceOverride,
    int FilteredVectorHitCount);

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
