using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.HttpApi.Infrastructure;

public sealed record CloudDelegationTokenContractProof(
    string Audience,
    string Actor);

public interface ICloudDelegationTokenContractValidator
{
    Task<CloudDelegationTokenContractProof?> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}

internal sealed class CloudDelegationTokenContractValidator(
    HttpClient httpClient,
    IOptions<CloudAiReadOptions> cloudAiReadOptions)
    : ICloudDelegationTokenContractValidator
{
    private const string ProbePath = "/api/v1/ai/read/processes?maxRows=1";
    private const string PostPolicyPermissionFailure =
        "拒绝访问：Cloud 用户状态、权限或设备范围不再有效";
    private const int MaximumProblemBytes = 4096;

    public async Task<CloudDelegationTokenContractProof?> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var options = cloudAiReadOptions.Value;
        if (!options.Enabled ||
            !Uri.TryCreate(options.BaseUrl?.TrimEnd('/'), UriKind.Absolute, out var aiReadBaseUri) ||
            aiReadBaseUri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var probeUri = new Uri(aiReadBaseUri, ProbePath);
        if (!string.Equals(probeUri.Scheme, aiReadBaseUri.Scheme, StringComparison.Ordinal) ||
            !string.Equals(probeUri.Authority, aiReadBaseUri.Authority, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, probeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return ValidatedProof();
            }

            if (response.StatusCode != HttpStatusCode.Forbidden ||
                !await IsPostPolicyPermissionFailureAsync(response, cancellationToken))
            {
                return null;
            }

            // This problem can only be emitted after Cloud's AiRead endpoint policy has
            // authenticated the token and accepted its audience, actor and scope. A user
            // without Process.Read may still log in; their later queries remain denied.
            return ValidatedProof();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static CloudDelegationTokenContractProof ValidatedProof() => new(
        CloudDelegationDefaults.Audience,
        CloudDelegationDefaults.Actor);

    private static async Task<bool> IsPostPolicyPermissionFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaximumProblemBytes)
        {
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaximumProblemBytes)
            {
                return false;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;
        try
        {
            using var document = await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
            return TryReadUniqueString(document.RootElement, "detail", out var detail) &&
                   string.Equals(detail, PostPolicyPermissionFailure, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadUniqueString(
        JsonElement root,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var matches = root.EnumerateObject()
            .Where(property => string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = matches[0].Value.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }
}
