using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.Rag.Aggregates.KnowledgeBase;

public class Document : IEntity<int>
{
    private readonly List<DocumentChunk> _chunks = [];

    protected Document()
    {
    }

    internal Document(Guid knowledgeBaseId, string name, string filePath, string extension, string fileHash)
    {
        if (knowledgeBaseId == Guid.Empty)
        {
            throw new ArgumentException("Document knowledge base id is required.", nameof(knowledgeBaseId));
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

        KnowledgeBaseId = knowledgeBaseId;
        Name = name.Trim();
        FilePath = filePath.Trim();
        Extension = extension.Trim();
        FileHash = fileHash.Trim();
        Status = DocumentStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public int Id { get; private set; }

    public Guid KnowledgeBaseId { get; private set; }

    /// <summary>
    /// 原始文件名
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 文件存储路径
    /// </summary>
    public string FilePath { get; private set; } = string.Empty;

    /// <summary>
    /// 文件扩展名
    /// </summary>
    public string Extension { get; private set; } = string.Empty;

    /// <summary>
    /// 文件哈希值
    /// </summary>
    public string FileHash { get; private set; } = string.Empty;

    /// <summary>
    /// 文档处理状态
    /// </summary>
    public DocumentStatus Status { get; private set; }

    /// <summary>
    /// 切片数量
    /// </summary>
    public int ChunkCount { get; private set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    // 导航属性
    public KnowledgeBase KnowledgeBase { get; private set; } = null!;

    public IReadOnlyCollection<DocumentChunk> Chunks => _chunks.AsReadOnly();

    #region 领域行为方法

    /// <summary>
    /// 开始解析文档
    /// </summary>
    public void StartParsing()
    {
        if (Status != DocumentStatus.Pending && Status != DocumentStatus.Failed)
            throw new InvalidOperationException($"当前状态 {Status} 不允许开始解析");

        Status = DocumentStatus.Parsing;
        ErrorMessage = null;
    }

    /// <summary>
    /// 完成解析，准备切片
    /// </summary>
    public void CompleteParsing()
    {
        if (Status != DocumentStatus.Parsing) return;
        Status = DocumentStatus.Splitting;
    }

    /// <summary>
    /// 添加文档切片
    /// </summary>
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

        // 允许在 Splitting 或 Embedding 阶段添加/重新生成切片
        if (Status != DocumentStatus.Splitting && Status != DocumentStatus.Embedding)
            throw new InvalidOperationException($"当前状态 {Status} 不允许添加切片");

        var chunk = new DocumentChunk(Id, index, content);
        _chunks.Add(chunk);
        ChunkCount = _chunks.Count;
    }

    /// <summary>
    /// 清空所有切片（例如重新处理时）
    /// </summary>
    public void ClearChunks()
    {
        _chunks.Clear();
        ChunkCount = 0;
    }

    /// <summary>
    /// 开始向量化
    /// </summary>
    public void StartEmbedding()
    {
        Status = DocumentStatus.Embedding;
    }

    /// <summary>
    /// 标记切片已向量化完成（更新向量ID）
    /// </summary>
    public void MarkChunkAsEmbedded(int chunkId, string vectorId)
    {
        if (string.IsNullOrWhiteSpace(vectorId))
        {
            throw new ArgumentException("Document chunk vector id is required.", nameof(vectorId));
        }

        var chunk = _chunks.FirstOrDefault(c => c.Id == chunkId);
        chunk?.SetVectorId(vectorId);
    }

    /// <summary>
    /// 文档处理全部完成
    /// </summary>
    public void MarkAsIndexed()
    {
        Status = DocumentStatus.Indexed;
        ProcessedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 标记处理失败
    /// </summary>
    public void MarkAsFailed(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Document failure error message is required.", nameof(errorMessage));
        }

        Status = DocumentStatus.Failed;
        ErrorMessage = errorMessage.Trim();
    }

    #endregion 领域行为方法
}

public enum DocumentStatus
{
    Pending = 0,      // 等待处理
    Parsing = 1,      // 正在读取/解析内容
    Splitting = 2,    // 正在进行文本切片
    Embedding = 3,    // 正在调用模型生成向量
    Indexed = 4,      // 索引完成，可用于检索
    Failed = 5        // 处理失败
}
