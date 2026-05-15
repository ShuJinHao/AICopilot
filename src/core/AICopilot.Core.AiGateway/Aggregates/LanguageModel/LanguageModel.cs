using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.LanguageModel;

public class LanguageModel : IAggregateRoot<LanguageModelId>
{
    protected LanguageModel()
    {
    }

    public LanguageModel(string provider, string name, string baseUrl, string? apiKey, ModelParameters parameters)
        : this(
            provider,
            name,
            baseUrl,
            apiKey,
            parameters,
            provider,
            LanguageModelUsage.Chat | LanguageModelUsage.Routing,
            true)
    {
    }

    public LanguageModel(
        string provider,
        string name,
        string baseUrl,
        string? apiKey,
        ModelParameters parameters,
        string protocolType,
        LanguageModelUsage usage,
        bool isEnabled)
    {
        ValidateInfo(provider, name, baseUrl);
        ValidateProtocol(protocolType);
        ValidateUsage(usage);
        ValidateParameters(parameters);

        Id = LanguageModelId.New();
        Name = name.Trim();
        Provider = provider.Trim();
        ProtocolType = protocolType.Trim();
        BaseUrl = baseUrl.Trim();
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        Parameters = parameters;
        Usage = usage;
        IsEnabled = isEnabled;
    }

    public LanguageModelId Id { get; private set; }

    public uint RowVersion { get; private set; }

    public string Provider { get; private set; } = null!;

    public string ProtocolType { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string BaseUrl { get; private set; } = null!;

    public string? ApiKey { get; private set; }

    public ModelParameters Parameters { get; private set; } = null!;

    public LanguageModelUsage Usage { get; private set; } = LanguageModelUsage.Chat;

    public bool IsEnabled { get; private set; } = true;

    public LanguageModelConnectivityStatus ConnectivityStatus { get; private set; } =
        LanguageModelConnectivityStatus.Unknown;

    public DateTimeOffset? ConnectivityCheckedAt { get; private set; }

    public string? ConnectivityError { get; private set; }

    public void UpdateInfo(string provider, string name, string baseUrl, string protocolType)
    {
        ValidateInfo(provider, name, baseUrl);
        ValidateProtocol(protocolType);

        Provider = provider.Trim();
        Name = name.Trim();
        BaseUrl = baseUrl.Trim();
        ProtocolType = protocolType.Trim();
    }

    public void UpdateInfo(string provider, string name, string baseUrl)
    {
        UpdateInfo(provider, name, baseUrl, ProtocolType);
    }

    public void UpdateApiKey(string? apiKey)
    {
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public void UpdateParameters(ModelParameters parameters)
    {
        ValidateParameters(parameters);
        Parameters = parameters;
    }

    public void UpdateRuntimeFlags(LanguageModelUsage usage, bool isEnabled)
    {
        ValidateUsage(usage);
        Usage = usage;
        IsEnabled = isEnabled;
    }

    public bool SupportsUsage(LanguageModelUsage usage)
    {
        return Usage.HasFlag(usage);
    }

    public void ResetConnectivityStatus()
    {
        ConnectivityStatus = LanguageModelConnectivityStatus.Unknown;
        ConnectivityCheckedAt = null;
        ConnectivityError = null;
    }

    public void MarkConnectivitySucceeded(DateTimeOffset checkedAt)
    {
        ConnectivityStatus = LanguageModelConnectivityStatus.Succeeded;
        ConnectivityCheckedAt = checkedAt;
        ConnectivityError = null;
    }

    public void MarkConnectivityFailed(DateTimeOffset checkedAt, string error)
    {
        ConnectivityStatus = LanguageModelConnectivityStatus.Failed;
        ConnectivityCheckedAt = checkedAt;
        ConnectivityError = TrimConnectivityError(error);
    }

    private static string TrimConnectivityError(string error)
    {
        var normalized = string.IsNullOrWhiteSpace(error) ? "Unknown connectivity error." : error.Trim();
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }

    private static void ValidateInfo(string provider, string name, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Language model provider is required.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Language model name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Language model base URL is required.", nameof(baseUrl));
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Language model base URL must be an absolute HTTP or HTTPS URL.", nameof(baseUrl));
        }
    }

    private static void ValidateParameters(ModelParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.MaxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "Language model context window must be greater than zero.");
        }

        if (parameters.MaxOutputTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "Language model max output tokens must be greater than zero.");
        }

        if (parameters.Temperature is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "Language model temperature must be between 0 and 2.");
        }
    }

    private static void ValidateProtocol(string protocolType)
    {
        if (string.IsNullOrWhiteSpace(protocolType))
        {
            throw new ArgumentException("Language model protocol type is required.", nameof(protocolType));
        }
    }

    private static void ValidateUsage(LanguageModelUsage usage)
    {
        if (usage == LanguageModelUsage.None)
        {
            throw new ArgumentException("Language model usage must include at least one usage.", nameof(usage));
        }
    }
}
