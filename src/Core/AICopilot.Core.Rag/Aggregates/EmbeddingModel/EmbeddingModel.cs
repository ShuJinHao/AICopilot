using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.Rag.Aggregates.EmbeddingModel;

public class EmbeddingModel : IAggregateRoot
{
    protected EmbeddingModel()
    {
    }

    public EmbeddingModel(
        string name,
        string provider,
        string baseUrl,
        string modelName,
        int dimensions,
        int maxTokens,
        string? apiKey = null,
        bool isEnabled = true)
    {
        Id = Guid.NewGuid();
        Update(name, provider, baseUrl, apiKey, modelName, dimensions, maxTokens, isEnabled);
    }

    public Guid Id { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// 显示名称 (如: "OpenAI V3 Small")
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 模型提供商标识 (如: "OpenAI", "AzureOpenAI", "Ollama")
    /// </summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>
    /// 模型提供者的 API BaseUrl
    /// </summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>
    /// 模型提供商的 API Key（没有保持为空）
    /// </summary>
    public string? ApiKey { get; private set; }

    /// <summary>
    /// 实际的模型标识符 (如: "text-embedding-3-small")
    /// </summary>
    public string ModelName { get; private set; } = string.Empty;

    /// <summary>
    /// 向量维度 (如: 1536, 768, 1024)
    /// </summary>
    public int Dimensions { get; private set; }

    /// <summary>
    /// 最大上下文 Token 限制 (如: 8191)
    /// 用于在分割阶段校验切片大小是否超标
    /// </summary>
    public int MaxTokens { get; private set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    public void Update(
        string name,
        string provider,
        string baseUrl,
        string? apiKey,
        string modelName,
        int dimensions,
        int maxTokens,
        bool isEnabled)
    {
        Validate(name, provider, baseUrl, modelName, dimensions, maxTokens);

        Name = name.Trim();
        Provider = provider.Trim();
        BaseUrl = baseUrl.Trim();
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        ModelName = modelName.Trim();
        Dimensions = dimensions;
        MaxTokens = maxTokens;
        IsEnabled = isEnabled;
    }

    private static void Validate(
        string name,
        string provider,
        string baseUrl,
        string modelName,
        int dimensions,
        int maxTokens)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Embedding model name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Embedding model provider is required.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Embedding model base URL is required.", nameof(baseUrl));
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Embedding model base URL must be an absolute HTTP or HTTPS URL.", nameof(baseUrl));
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException("Embedding model model name is required.", nameof(modelName));
        }

        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Embedding model dimensions must be greater than zero.");
        }

        if (maxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "Embedding model max tokens must be greater than zero.");
        }
    }
}
