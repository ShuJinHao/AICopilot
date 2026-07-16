using System.Net;
using System.Net.Http.Headers;
using AICopilot.Infrastructure.AiGateway;

namespace AICopilot.InProcessTests;

public sealed class AiProviderRetryHandlerTests
{
    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(529)]
    public async Task SendAsync_ShouldRetryTransientStatusCodesUpToThreeTotalAttempts(int statusCode)
    {
        var innerHandler = new CallbackHttpMessageHandler((_, _, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)statusCode);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1));
            return Task.FromResult(response);
        });
        using var invoker = CreateInvoker(innerHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example/models");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be((HttpStatusCode)statusCode);
        innerHandler.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task SendAsync_ShouldNotRetryNonTransientStatusCode()
    {
        var innerHandler = new CallbackHttpMessageHandler((_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));
        using var invoker = CreateInvoker(innerHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example/models");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        innerHandler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_ShouldRetryHttpRequestExceptionUpToThreeTotalAttempts()
    {
        var innerHandler = new CallbackHttpMessageHandler((attempt, _, _) =>
            attempt < 3
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("provider unavailable"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = CreateInvoker(innerHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example/models");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        innerHandler.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task SendAsync_ShouldRetryProviderTaskCancellationUpToThreeTotalAttempts()
    {
        var innerHandler = new CallbackHttpMessageHandler((attempt, _, _) =>
            attempt < 3
                ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("provider timeout"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = CreateInvoker(innerHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example/models");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        innerHandler.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task SendAsync_ShouldNotRetryInvalidOperationException()
    {
        var innerHandler = new CallbackHttpMessageHandler((_, _, _) =>
            Task.FromException<HttpResponseMessage>(new InvalidOperationException("invalid provider state")));
        using var invoker = CreateInvoker(innerHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example/models");

        var act = () => invoker.SendAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        innerHandler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_ShouldPropagateCallerCancellationWithoutRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var innerHandler = new CallbackHttpMessageHandler((_, _, cancellationToken) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        });
        using var invoker = CreateInvoker(innerHandler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.example/models");

        var act = () => invoker.SendAsync(request, cancellation.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
        innerHandler.AttemptCount.Should().Be(1);
    }

    private static HttpMessageInvoker CreateInvoker(HttpMessageHandler innerHandler)
    {
        return new HttpMessageInvoker(new AiProviderRetryHandler { InnerHandler = innerHandler });
    }

    private sealed class CallbackHttpMessageHandler(
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        private int attemptCount;

        public int AttemptCount => Volatile.Read(ref attemptCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref attemptCount);
            return callback(attempt, request, cancellationToken);
        }
    }
}
