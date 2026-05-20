using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AICopilot.Infrastructure.CloudRead;

public sealed class CloudReadonlySandboxClient(
    HttpClient httpClient,
    ILogger<CloudReadonlySandboxClient> logger) : ICloudReadonlySandboxClient
{
    public async Task<JsonDocument> SendJsonAsync(
        CloudReadonlySandboxOptions options,
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken cancellationToken = default)
    {
        options.EnsureValid();

        var decision = CloudAiReadEndpointPolicy.Evaluate(
            method,
            path,
            options.ExplicitPostQueryPaths);
        if (!decision.IsAllowed)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.RequestBlocked,
                decision.Reason ?? "CloudReadonlySandbox request was blocked by the allowlist policy.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        using var request = new HttpRequestMessage(method, BuildUri(options, path, query));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ServiceAccountToken);

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
                "CloudReadonlySandbox request timed out.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "CloudReadonlySandbox request failed before receiving a response. Path={Path}", path);
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                "CloudReadonlySandbox endpoint is unavailable.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "CloudReadonlySandbox response was not valid JSON. Path={Path}", path);
            throw;
        }
    }

    private static Uri BuildUri(
        CloudReadonlySandboxOptions options,
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
                "CloudReadonlySandbox credential is missing or invalid.",
                statusCode),
            HttpStatusCode.Forbidden => new CloudAiReadException(
                CloudAiReadProblemCodes.Forbidden,
                "CloudReadonlySandbox permission or device scope is insufficient.",
                statusCode),
            HttpStatusCode.NotFound => new CloudAiReadException(
                CloudAiReadProblemCodes.NotFound,
                "CloudReadonlySandbox resource was not found.",
                statusCode),
            _ => new CloudAiReadException(
                CloudAiReadProblemCodes.Unavailable,
                $"CloudReadonlySandbox endpoint returned {(int)statusCode}.",
                statusCode)
        };
    }
}
