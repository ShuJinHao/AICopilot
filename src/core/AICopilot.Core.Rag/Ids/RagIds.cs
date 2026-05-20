using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.Rag.Ids;

public readonly record struct DocumentId : IStronglyTypedIntId
{
    public DocumentId(int value) => Value = value;

    public int Value { get; }

    public static implicit operator int(DocumentId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct EmbeddingModelId : IStronglyTypedGuidId
{
    public EmbeddingModelId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Embedding model id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static EmbeddingModelId New() => new(Guid.NewGuid());

    public static implicit operator Guid(EmbeddingModelId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct KnowledgeBaseId : IStronglyTypedGuidId
{
    public KnowledgeBaseId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Knowledge base id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static KnowledgeBaseId New() => new(Guid.NewGuid());

    public static implicit operator Guid(KnowledgeBaseId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct KnowledgeCategoryId : IStronglyTypedGuidId
{
    public KnowledgeCategoryId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Knowledge category id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static KnowledgeCategoryId New() => new(Guid.NewGuid());

    public static implicit operator Guid(KnowledgeCategoryId id) => id.Value;

    public override string ToString() => Value.ToString();
}

public readonly record struct KnowledgeSupplementId : IStronglyTypedGuidId
{
    public KnowledgeSupplementId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Knowledge supplement id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static KnowledgeSupplementId New() => new(Guid.NewGuid());

    public static implicit operator Guid(KnowledgeSupplementId id) => id.Value;

    public override string ToString() => Value.ToString();
}
