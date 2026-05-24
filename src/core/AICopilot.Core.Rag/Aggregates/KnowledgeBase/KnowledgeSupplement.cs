using AICopilot.Core.Rag.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.Rag.Aggregates.KnowledgeBase;

public class KnowledgeSupplement : IAggregateRoot<KnowledgeSupplementId>
{
    protected KnowledgeSupplement()
    {
    }

    public KnowledgeSupplement(
        string title,
        string content,
        KnowledgeSupplementPriority priority,
        DateTime? effectiveAt = null,
        DateTime? expiredAt = null,
        KnowledgeCategoryId? categoryId = null,
        DocumentId? documentId = null,
        bool isEnabled = true)
    {
        Validate(title, content, effectiveAt, expiredAt);

        Id = KnowledgeSupplementId.New();
        Title = title.Trim();
        Content = content.Trim();
        Priority = priority;
        EffectiveAt = effectiveAt;
        ExpiredAt = expiredAt;
        CategoryId = categoryId;
        DocumentId = documentId;
        IsEnabled = isEnabled;
        CreatedAt = DateTime.UtcNow;
    }

    public KnowledgeSupplementId Id { get; private set; }

    public uint RowVersion { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Content { get; private set; } = string.Empty;

    public KnowledgeSupplementPriority Priority { get; private set; }

    public DateTime? EffectiveAt { get; private set; }

    public DateTime? ExpiredAt { get; private set; }

    public KnowledgeCategoryId? CategoryId { get; private set; }

    public DocumentId? DocumentId { get; private set; }

    public bool IsEnabled { get; private set; } = true;

    public DateTime CreatedAt { get; private set; }

    public void Update(
        string title,
        string content,
        KnowledgeSupplementPriority priority,
        DateTime? effectiveAt,
        DateTime? expiredAt,
        KnowledgeCategoryId? categoryId,
        DocumentId? documentId,
        bool isEnabled)
    {
        Validate(title, content, effectiveAt, expiredAt);

        Title = title.Trim();
        Content = content.Trim();
        Priority = priority;
        EffectiveAt = effectiveAt;
        ExpiredAt = expiredAt;
        CategoryId = categoryId;
        DocumentId = documentId;
        IsEnabled = isEnabled;
    }

    public bool CanApply(DateTime utcNow)
    {
        if (!IsEnabled)
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

    private static void Validate(
        string title,
        string content,
        DateTime? effectiveAt,
        DateTime? expiredAt)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Knowledge supplement title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Knowledge supplement content is required.", nameof(content));
        }

        if (effectiveAt.HasValue && expiredAt.HasValue && expiredAt.Value < effectiveAt.Value)
        {
            throw new ArgumentException("Knowledge supplement expiration cannot be earlier than effective time.", nameof(expiredAt));
        }
    }
}

public enum KnowledgeSupplementPriority
{
    Normal = 0,
    High = 1,
    CriticalOverride = 2
}
