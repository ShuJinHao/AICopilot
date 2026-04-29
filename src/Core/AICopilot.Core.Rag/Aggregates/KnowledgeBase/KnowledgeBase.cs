using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.Rag.Aggregates.KnowledgeBase;

public class KnowledgeBase : IAggregateRoot
{
    private readonly List<Document> _documents = [];

    protected KnowledgeBase()
    {
    }

    public KnowledgeBase(string name, string description, Guid embeddingModelId)
    {
        ValidateInfo(name, description);
        ValidateEmbeddingModelId(embeddingModelId);

        Id = Guid.NewGuid();
        Name = name.Trim();
        Description = description.Trim();
        EmbeddingModelId = embeddingModelId;
    }

    public Guid Id { get; private set; }

    public uint RowVersion { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// 嵌入模型ID。一个知识库内的所有文档必须使用相同的嵌入模型。
    /// </summary>
    public Guid EmbeddingModelId { get; private set; }

    // 导航属性：对外只暴露只读集合
    public IReadOnlyCollection<Document> Documents => _documents.AsReadOnly();

    /// <summary>
    /// 添加新文档到知识库
    /// </summary>
    public Document AddDocument(string name, string filePath, string extension, string fileHash)
    {
        var document = new Document(Id, name, filePath, extension, fileHash);
        _documents.Add(document);
        return document;
    }

    /// <summary>
    /// 移除文档
    /// </summary>
    public void RemoveDocument(int documentId)
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

    public void UpdateEmbeddingModel(Guid embeddingModelId)
    {
        ValidateEmbeddingModelId(embeddingModelId);
        EmbeddingModelId = embeddingModelId;
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

    private static void ValidateEmbeddingModelId(Guid embeddingModelId)
    {
        if (embeddingModelId == Guid.Empty)
        {
            throw new ArgumentException("Knowledge base embedding model id is required.", nameof(embeddingModelId));
        }
    }
}
