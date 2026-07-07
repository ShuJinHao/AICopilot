using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AICopilot.Infrastructure.CloudRead;

internal sealed class CloudAiReadHttpTransport(HttpClient httpClient, ILogger logger)
{
    public async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        CloudAiReadOptions configuredOptions,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(configuredOptions.TimeoutSeconds));

        using var request = new HttpRequestMessage(
            method,
            BuildUri(configuredOptions, path, query));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuredOptions.ServiceAccountToken);

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateResponseException(response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            return await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud AiRead request timed out.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                "Cloud AiRead request failed before receiving a response. Path={Path}; ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                path,
                ex.GetType().Name);
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud AiRead endpoint is unavailable.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                "Cloud AiRead response was not valid JSON. Path={Path}; ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                path,
                ex.GetType().Name);
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "Cloud AiRead endpoint returned an invalid JSON payload.");
        }
    }

    private static Uri BuildUri(
        CloudAiReadOptions options,
        string path,
        IReadOnlyDictionary<string, string?>? query)
    {
        var baseUri = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var relativePath = path.TrimStart('/');
        var relativeUri = query is null || query.Count == 0
            ? relativePath
            : $"{relativePath}?{BuildQueryString(query)}";
        return new Uri(baseUri, relativeUri);
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string?> query)
    {
        return string.Join(
            '&',
            query
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .Select(item =>
                    $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}"));
    }

    private static CloudAiReadException CreateResponseException(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => new CloudAiReadException(
                CloudAiReadProblemCodes.Unauthorized,
                "Cloud AiRead credential is missing or invalid.",
                statusCode),
            HttpStatusCode.Forbidden => new CloudAiReadException(
                CloudAiReadProblemCodes.Forbidden,
                "Cloud AiRead permission or device scope is insufficient.",
                statusCode),
            HttpStatusCode.NotFound => new CloudAiReadException(
                CloudAiReadProblemCodes.NotFound,
                "Cloud AiRead resource was not found.",
                statusCode),
            _ => new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                $"Cloud AiRead endpoint returned {(int)statusCode}.",
                statusCode)
        };
    }
}
