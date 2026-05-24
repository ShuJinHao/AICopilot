using AICopilot.Core.Rag.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.Rag.Aggregates.KnowledgeBase;

public class KnowledgeBase : IAggregateRoot<KnowledgeBaseId>
{
    private readonly List<Document> _documents = [];

    protected KnowledgeBase()
    {
    }

    public KnowledgeBase(
        string name,
        string description,
        EmbeddingModelId embeddingModelId,
        Guid? ownerUserId = null,
        KnowledgeBaseAccessScope accessScope = KnowledgeBaseAccessScope.OwnerOnly)
    {
        ValidateInfo(name, description);
        ValidateEmbeddingModelId(embeddingModelId);
        ValidateAccess(ownerUserId, accessScope);

        Id = KnowledgeBaseId.New();
        Name = name.Trim();
        Description = description.Trim();
        EmbeddingModelId = embeddingModelId;
        OwnerUserId = ownerUserId;
        AccessScope = accessScope;
    }

    public KnowledgeBaseId Id { get; private set; }

    public uint RowVersion { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    public Guid? OwnerUserId { get; private set; }

    public KnowledgeBaseAccessScope AccessScope { get; private set; } = KnowledgeBaseAccessScope.OwnerOnly;

    /// <summary>
    /// 嵌入模型ID。一个知识库内的所有文档必须使用相同的嵌入模型。
    /// </summary>
    public EmbeddingModelId EmbeddingModelId { get; private set; }

    // 导航属性：对外只暴露只读集合
    public IReadOnlyCollection<Document> Documents => _documents.AsReadOnly();

    /// <summary>
    /// 添加新文档到知识库
    /// </summary>
    public Document AddDocument(
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
        var document = new Document(
            Id,
            name,
            filePath,
            extension,
            fileHash,
            classification,
            sourceType,
            isSanitized,
            reviewedBy,
            reviewedAt,
            effectiveFrom,
            effectiveTo,
            allowedForFinalPrompt,
            blockedReason,
            documentGroupId,
            versionNo,
            effectiveAt,
            expiredAt,
            categoryId);
        _documents.Add(document);
        return document;
    }

    /// <summary>
    /// 移除文档
    /// </summary>
    public void RemoveDocument(DocumentId documentId)
    {
        var doc = _documents.FirstOrDefault(d => d.Id == documentId);
        if (doc != null)
        {
            _documents.Remove(doc);
        }
    }

    public void UpdateInfo(string name, string description)
    {
        ValidateInfo(name, description);

        Name = name.Trim();
        Description = description.Trim();
    }

    public void UpdateEmbeddingModel(EmbeddingModelId embeddingModelId)
    {
        ValidateEmbeddingModelId(embeddingModelId);
        EmbeddingModelId = embeddingModelId;
    }

    public void UpdateAccess(Guid? ownerUserId, KnowledgeBaseAccessScope accessScope)
    {
        ValidateAccess(ownerUserId, accessScope);
        OwnerUserId = ownerUserId;
        AccessScope = accessScope;
    }

    private static void ValidateInfo(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Knowledge base name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Knowledge base description is required.", nameof(description));
        }
    }

    private static void ValidateEmbeddingModelId(EmbeddingModelId embeddingModelId)
    {
    }

    private static void ValidateAccess(Guid? ownerUserId, KnowledgeBaseAccessScope accessScope)
    {
        if (!Enum.IsDefined(accessScope))
        {
            throw new ArgumentOutOfRangeException(nameof(accessScope));
        }

        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("Knowledge base owner user id cannot be empty.", nameof(ownerUserId));
        }
    }
}

public enum KnowledgeBaseAccessScope
{
    OwnerOnly = 0,
    AuthenticatedUsers = 1
}
