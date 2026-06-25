using System.Net;

namespace AICopilot.Infrastructure.AiGateway;

internal sealed class AiProviderRetryHandler : DelegatingHandler
{
    private const int MaxRetries = 2;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(2);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var retryTemplate = await RetryableHttpRequest.CreateAsync(request, cancellationToken);

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;

            try
            {
                var attemptRequest = attempt == 0
                    ? request
                    : retryTemplate.CreateRequest();
                response = await base.SendAsync(attemptRequest, cancellationToken);

                if (!IsRetryableStatusCode(response.StatusCode) || attempt >= MaxRetries)
                {
                    return response;
                }

                var retryAfter = response.Headers.RetryAfter?.Delta;
                response.Dispose();
                await DelayBeforeRetryAsync(attempt, retryAfter, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                response?.Dispose();
                await DelayBeforeRetryAsync(attempt, retryAfter: null, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetries)
            {
                response?.Dispose();
                await DelayBeforeRetryAsync(attempt, retryAfter: null, cancellationToken);
            }
        }
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            || (int)statusCode == 529;
    }

    private static Task DelayBeforeRetryAsync(
        int attempt,
        TimeSpan? retryAfter,
        CancellationToken cancellationToken)
    {
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 125));
        var exponential = TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
        var computedDelay = exponential + jitter;
        if (computedDelay > MaxDelay)
        {
            computedDelay = MaxDelay;
        }

        var delay = retryAfter is { } headerDelay && headerDelay > TimeSpan.Zero && headerDelay < TimeSpan.FromSeconds(10)
            ? headerDelay
            : computedDelay;
        return Task.Delay(delay, cancellationToken);
    }

    private sealed record RetryableHttpRequest(
        HttpMethod Method,
        Uri? RequestUri,
        Version Version,
        HttpVersionPolicy VersionPolicy,
        IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> Headers,
        byte[]? ContentBytes,
        IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> ContentHeaders)
    {
        public static async Task<RetryableHttpRequest> CreateAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[]? contentBytes = null;
            var contentHeaders = Array.Empty<KeyValuePair<string, IEnumerable<string>>>();

            if (request.Content is not null)
            {
                contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                contentHeaders = request.Content.Headers
                    .Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray()))
                    .ToArray();
            }

            return new RetryableHttpRequest(
                request.Method,
                request.RequestUri,
                request.Version,
                request.VersionPolicy,
                request.Headers.Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray())).ToArray(),
                contentBytes,
                contentHeaders);
        }

        public HttpRequestMessage CreateRequest()
        {
            var request = new HttpRequestMessage(Method, RequestUri)
            {
                Version = Version,
                VersionPolicy = VersionPolicy
            };

            foreach (var header in Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (ContentBytes is not null)
            {
                request.Content = new ByteArrayContent(ContentBytes);
                foreach (var header in ContentHeaders)
                {
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return request;
        }
    }
}
