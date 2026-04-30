using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.LanguageModel;

public class LanguageModel : IAggregateRoot<LanguageModelId>
{
    protected LanguageModel()
    {
    }

    public LanguageModel(string provider, string name, string baseUrl, string? apiKey, ModelParameters parameters)
    {
        ValidateInfo(provider, name, baseUrl);
        ValidateParameters(parameters);

        Id = LanguageModelId.New();
        Name = name.Trim();
        Provider = provider.Trim();
        BaseUrl = baseUrl.Trim();
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        Parameters = parameters;
    }

    public LanguageModelId Id { get; private set; }

    public uint RowVersion { get; private set; }

    public string Provider { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string BaseUrl { get; private set; } = null!;

    public string? ApiKey { get; private set; }

    public ModelParameters Parameters { get; private set; } = null!;

    public void UpdateInfo(string provider, string name, string baseUrl)
    {
        ValidateInfo(provider, name, baseUrl);

        Provider = provider.Trim();
        Name = name.Trim();
        BaseUrl = baseUrl.Trim();
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
            throw new ArgumentOutOfRangeException(nameof(parameters), "Language model max tokens must be greater than zero.");
        }

        if (parameters.Temperature is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "Language model temperature must be between 0 and 2.");
        }
    }
}
