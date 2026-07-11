using AICopilot.Core.Rag.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.Rag.Aggregates.KnowledgeBase;

public class Document : IEntity<DocumentId>
{
    private readonly List<DocumentChunk> _chunks = [];

    protected Document()
    {
    }

    internal Document(
        DocumentId id,
        KnowledgeBaseId knowledgeBaseId,
        string name,
        string filePath,
        string extension,
        string fileHash,
        DocumentClassification classification = DocumentClassification.Internal,
        DocumentSourceType sourceType = DocumentSourceType.UserUploaded,
        bool isSanitized = false,
        string? reviewedBy = null,
        DateTime? reviewedAt = null,
        DateTime? effectiveFrom = null,
        DateTime? effectiveTo = null,
        bool allowedForFinalPrompt = true,
        string? blockedReason = null,
        Guid? documentGroupId = null,
        int versionNo = 1,
        DateTime? effectiveAt = null,
        DateTime? expiredAt = null,
        KnowledgeCategoryId? categoryId = null)
    {
        if (id.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Document id must be positive.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Document name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Document file path is required.", nameof(filePath));
        }

        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new ArgumentException("Document extension is required.", nameof(extension));
        }

        if (string.IsNullOrWhiteSpace(fileHash))
        {
            throw new ArgumentException("Document file hash is required.", nameof(fileHash));
        }

        Id = id;
        KnowledgeBaseId = knowledgeBaseId;
        Name = name.Trim();
        FilePath = filePath.Trim();
        Extension = extension.Trim();
        FileHash = fileHash.Trim();
        Status = DocumentStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        ConfigureGovernance(
            classification,
            sourceType,
            isSanitized,
            reviewedBy,
            reviewedAt,
            effectiveFrom,
            effectiveTo,
            allowedForFinalPrompt,
            blockedReason);
        ConfigureVersioning(
            documentGroupId ?? Guid.NewGuid(),
            versionNo,
            effectiveAt,
            expiredAt,
            categoryId);
    }

    public DocumentId Id { get; private set; }

    public KnowledgeBaseId KnowledgeBaseId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string FilePath { get; private set; } = string.Empty;

    public string Extension { get; private set; } = string.Empty;

    public string FileHash { get; private set; } = string.Empty;

    public DocumentStatus Status { get; private set; }

    public int ChunkCount { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    public DocumentClassification Classification { get; private set; } = DocumentClassification.Internal;
    public DocumentSourceType SourceType { get; private set; } = DocumentSourceType.UserUploaded;
    public bool IsSanitized { get; private set; }
    public string? ReviewedBy { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public DateTime? EffectiveFrom { get; private set; }
    public DateTime? EffectiveTo { get; private set; }
    public bool AllowedForFinalPrompt { get; private set; } = true;
    public string? BlockedReason { get; private set; }

    public KnowledgeCategoryId? CategoryId { get; private set; }

    public Guid DocumentGroupId { get; private set; }

    public int VersionNo { get; private set; } = 1;

    public DateTime? EffectiveAt { get; private set; }

    public DateTime? ExpiredAt { get; private set; }

    public DocumentId? SupersededByDocumentId { get; private set; }

    public KnowledgeBase KnowledgeBase { get; private set; } = null!;

    public IReadOnlyCollection<DocumentChunk> Chunks => _chunks.AsReadOnly();

    public void ConfigureGovernance(
        DocumentClassification classification,
        DocumentSourceType sourceType,
        bool isSanitized,
        string? reviewedBy,
        DateTime? reviewedAt,
        DateTime? effectiveFrom,
        DateTime? effectiveTo,
        bool allowedForFinalPrompt,
        string? blockedReason)
    {
        if (!Enum.IsDefined(classification))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        if (!Enum.IsDefined(sourceType))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceType));
        }

        if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveTo.Value < effectiveFrom.Value)
        {
            throw new ArgumentException("Document effective end time cannot be earlier than start time.", nameof(effectiveTo));
        }

        Classification = classification;
        SourceType = sourceType;
        IsSanitized = isSanitized;
        ReviewedBy = NormalizeOptionalText(reviewedBy);
        ReviewedAt = reviewedAt;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        AllowedForFinalPrompt = allowedForFinalPrompt;
        BlockedReason = NormalizeOptionalText(blockedReason);
    }

    public bool CanEnterFinalPrompt(DateTime utcNow)
    {
        if (!IsSearchable(utcNow))
        {
            return false;
        }

        if (!AllowedForFinalPrompt || Classification == DocumentClassification.Forbidden)
        {
            return false;
        }

        if (EffectiveFrom.HasValue && EffectiveFrom.Value > utcNow)
        {
            return false;
        }

        if (EffectiveTo.HasValue && EffectiveTo.Value < utcNow)
        {
            return false;
        }

        return true;
    }

    public void ConfigureVersioning(
        Guid documentGroupId,
        int versionNo,
        DateTime? effectiveAt,
        DateTime? expiredAt,
        KnowledgeCategoryId? categoryId)
    {
        if (documentGroupId == Guid.Empty)
        {
            throw new ArgumentException("Document group id is required.", nameof(documentGroupId));
        }

        if (versionNo <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNo), "Document version number must be positive.");
        }

        if (effectiveAt.HasValue && expiredAt.HasValue && expiredAt.Value < effectiveAt.Value)
        {
            throw new ArgumentException("Document expiration cannot be earlier than effective time.", nameof(expiredAt));
        }

        DocumentGroupId = documentGroupId;
        VersionNo = versionNo;
        EffectiveAt = effectiveAt;
        ExpiredAt = expiredAt;
        CategoryId = categoryId;
    }

    public bool IsSearchable(DateTime utcNow)
    {
        if (Status is DocumentStatus.SoftDeleted
            or DocumentStatus.Deleting
            or DocumentStatus.Deleted
            or DocumentStatus.Superseded
            or DocumentStatus.Failed)
        {
            return false;
        }

        if (Status is not (DocumentStatus.Indexed or DocumentStatus.Active))
        {
            return false;
        }

        if (EffectiveAt.HasValue && EffectiveAt.Value > utcNow)
        {
            return false;
        }

        if (ExpiredAt.HasValue && ExpiredAt.Value < utcNow)
        {
            return false;
        }

        return true;
    }

    public void StartParsing()
    {
        if (Status != DocumentStatus.Pending &&
            Status != DocumentStatus.Failed &&
            Status != DocumentStatus.Parsing &&
            Status != DocumentStatus.Splitting &&
            Status != DocumentStatus.Embedding)
        {
            throw new InvalidOperationException($"Current document status {Status} cannot start parsing.");
        }

        Status = DocumentStatus.Parsing;
        ErrorMessage = null;
    }

    public void CompleteParsing()
    {
        if (Status != DocumentStatus.Parsing) return;
        Status = DocumentStatus.Splitting;
    }

    public void AddChunk(int index, string content)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Document chunk index cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Document chunk content is required.", nameof(content));
        }

        if (Status != DocumentStatus.Splitting && Status != DocumentStatus.Embedding)
        {
            throw new InvalidOperationException($"Current document status {Status} cannot add chunks.");
        }

        var chunk = new DocumentChunk(Id, index, content);
        _chunks.Add(chunk);
        ChunkCount = _chunks.Count;
    }

    public void ClearChunks()
    {
        _chunks.Clear();
        ChunkCount = 0;
    }

    public void StartEmbedding()
    {
        Status = DocumentStatus.Embedding;
    }

    public void MarkChunkAsEmbedded(int chunkId, string vectorId)
    {
        if (string.IsNullOrWhiteSpace(vectorId))
        {
            throw new ArgumentException("Document chunk vector id is required.", nameof(vectorId));
        }

        var chunk = _chunks.FirstOrDefault(c => c.Id == chunkId);
        chunk?.SetVectorId(vectorId);
    }

    public void MarkAsIndexed()
    {
        Status = DocumentStatus.Indexed;
        ProcessedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (Status is not (DocumentStatus.Indexed or DocumentStatus.Draft or DocumentStatus.Active))
        {
            throw new InvalidOperationException($"Current document status {Status} cannot be activated.");
        }

        Status = DocumentStatus.Active;
        ProcessedAt ??= DateTime.UtcNow;
    }

    public void SupersedeBy(DocumentId documentId)
    {
        if (documentId.Value <= 0)
        {
            throw new ArgumentException("Superseding document id is required.", nameof(documentId));
        }

        SupersededByDocumentId = documentId;
        Status = DocumentStatus.Superseded;
    }

    public void SoftDelete()
    {
        Status = DocumentStatus.SoftDeleted;
    }

    public void MarkDeleting()
    {
        if (Status != DocumentStatus.SoftDeleted)
        {
            Status = DocumentStatus.SoftDeleted;
        }

        Status = DocumentStatus.Deleting;
    }

    public void MarkDeleted()
    {
        Status = DocumentStatus.Deleted;
        ClearChunks();
    }

    public void MarkAsFailed(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Document failure error message is required.", nameof(errorMessage));
        }

        Status = DocumentStatus.Failed;
        ErrorMessage = errorMessage.Trim();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public enum DocumentClassification
{
    Public = 0,
    Internal = 1,
    Sensitive = 2,
    Forbidden = 3
}

public enum DocumentSourceType
{
    UserUploaded = 0,
    BusinessRule = 1,
    CloudReadOnlyApiDoc = 2,
    Runbook = 3,
    External = 4
}

public enum DocumentStatus
{
    Pending = 0,
    Parsing = 1,
    Splitting = 2,
    Embedding = 3,
    Indexed = 4,
    Failed = 5,
    Draft = 6,
    Active = 7,
    Superseded = 8,
    SoftDeleted = 9,
    Deleting = 10,
    Deleted = 11
}
