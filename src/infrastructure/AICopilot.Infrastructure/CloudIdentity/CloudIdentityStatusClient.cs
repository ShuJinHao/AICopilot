using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.Infrastructure.CloudIdentity;

public sealed class CloudIdentityStatusClient(
    HttpClient httpClient,
    IOptions<CloudIdentityStatusOptions> options) : ICloudIdentityStatusClient
{
    public async Task<CloudIdentityStatusCheckResult> GetStatusAsync(
        string cloudUserId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var configuredOptions = options.Value;
        if (!configuredOptions.Enabled)
        {
            return CloudIdentityStatusCheckResult.Unavailable("Cloud identity status check is disabled.");
        }

        configuredOptions.EnsureValid();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(configuredOptions.TimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildStatusUri(configuredOptions, cloudUserId, tenantId));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuredOptions.ServiceAccountToken);

            using var response = await httpClient.SendAsync(request, timeoutCts.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return CloudIdentityStatusCheckResult.NotFound("Cloud identity was not found.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return CloudIdentityStatusCheckResult.Unavailable(
                    $"Cloud identity status endpoint returned {(int)response.StatusCode}.");
            }

            var status = await response.Content.ReadFromJsonAsync<CloudIdentityStatusSnapshot>(
                cancellationToken: timeoutCts.Token);

            return status is null
                ? CloudIdentityStatusCheckResult.Unavailable("Cloud identity status endpoint returned an empty payload.")
                : CloudIdentityStatusCheckResult.Succeeded(status);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CloudIdentityStatusCheckResult.Unavailable("Cloud identity status endpoint timed out.");
        }
        catch (HttpRequestException ex)
        {
            return CloudIdentityStatusCheckResult.Unavailable(ex.Message);
        }
    }

    private static Uri BuildStatusUri(
        CloudIdentityStatusOptions options,
        string cloudUserId,
        string tenantId)
    {
        var baseUri = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var path = options.StatusEndpointPath
            .TrimStart('/')
            .Replace("{cloudUserId}", Uri.EscapeDataString(cloudUserId), StringComparison.Ordinal);
        var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var relativeUri = $"{path}{separator}tenantId={Uri.EscapeDataString(tenantId)}";
        return new Uri(baseUri, relativeUri);
    }
}
