using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using AICopilot.AiRuntime;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AICopilot.Infrastructure.AiGateway;

public sealed class LanguageModelConnectivityTester(
    IEnumerable<IChatClientProvider> providers,
    ISecretProtector secretProtector,
    ILogger<LanguageModelConnectivityTester> logger) : ILanguageModelConnectivityTester
{
    public async Task<LanguageModelConnectivityTestOutcome> TestAsync(
        LanguageModel model,
        CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(model.ApiKey))
            {
                return Failed("API Key is required.", checkedAt, stopwatch);
            }

            var provider = providers.FirstOrDefault(item => item.CanHandle(model.ProtocolType));
            if (provider is null)
            {
                return Failed($"No chat client provider is registered for protocol '{model.ProtocolType}'.", checkedAt, stopwatch);
            }

            var client = provider.CreateClient(model);
            try
            {
                var response = await client.GetResponseAsync(
                    [new ChatMessage(ChatRole.User, "Return exactly OK.")],
                    new ChatOptions
                    {
                        MaxOutputTokens = 16,
                        Temperature = 0
                    },
                    cancellationToken);

                logger.LogDebug(
                    "Language model connectivity test response for {Provider}/{Model}: {ResponseText}",
                    model.Provider,
                    model.Name,
                    response.Text);

                stopwatch.Stop();
                return new LanguageModelConnectivityTestOutcome(true, null, stopwatch.ElapsedMilliseconds, checkedAt);
            }
            finally
            {
                if (client is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Language model connectivity test failed for {Provider}/{Model}.", model.Provider, model.Name);
            return new LanguageModelConnectivityTestOutcome(
                false,
                SanitizeException(ex, model.ApiKey, TryUnprotect(model.ApiKey)),
                stopwatch.ElapsedMilliseconds,
                checkedAt);
        }
    }

    private static LanguageModelConnectivityTestOutcome Failed(
        string error,
        DateTimeOffset checkedAt,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new LanguageModelConnectivityTestOutcome(false, error, stopwatch.ElapsedMilliseconds, checkedAt);
    }

    private string? TryUnprotect(string? storedApiKey)
    {
        try
        {
            return secretProtector.Unprotect(storedApiKey);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static string SanitizeException(Exception exception, string? storedApiKey, string? plaintextApiKey)
    {
        var parts = new List<string>();
        var statusCode = ReadExceptionProperty(exception, "Status")
                         ?? ReadExceptionProperty(exception, "StatusCode");
        var errorCode = ReadExceptionProperty(exception, "ErrorCode")
                        ?? ReadExceptionProperty(exception, "Code")
                        ?? ReadExceptionProperty(exception, "Type");

        if (!string.IsNullOrWhiteSpace(statusCode))
        {
            parts.Add($"status={statusCode}");
        }

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            parts.Add($"type={errorCode}");
        }

        parts.Add(exception.Message);

        if (exception.InnerException is not null)
        {
            parts.Add(exception.InnerException.Message);
        }

        var error = string.Join("; ", parts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
        if (!string.IsNullOrEmpty(storedApiKey))
        {
            error = error.Replace(storedApiKey, "***", StringComparison.Ordinal);
        }

        if (!string.IsNullOrEmpty(plaintextApiKey))
        {
            error = error.Replace(plaintextApiKey, "***", StringComparison.Ordinal);
        }

        return error.Length <= 1000 ? error : error[..1000];
    }

    private static string? ReadExceptionProperty(Exception exception, string propertyName)
    {
        var property = exception.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        return property?.GetValue(exception)?.ToString();
    }
}
