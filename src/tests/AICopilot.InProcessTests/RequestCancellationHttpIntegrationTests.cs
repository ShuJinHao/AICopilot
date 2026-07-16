using System.Net;
using System.Text;
using AICopilot.HttpApi.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace AICopilot.InProcessTests;

public sealed class RequestCancellationHttpIntegrationTests
{
    [Fact]
    public async Task RequestAborted_BeforeResponseStarts_ShouldDisconnectWithoutProblemDetailsOrBusiness500Tracking()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await CancellationProbeServer.StartAsync(app =>
        {
            app.MapGet("/cancel-before-response", async context =>
            {
                try
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                }
                finally
                {
                    requestExited.TrySetResult();
                }
            });
        });
        using var cancellation = new CancellationTokenSource();

        var request = server.Client.GetAsync("/cancel-before-response", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var action = async () => await request;
        await action.Should().ThrowAsync<OperationCanceledException>();
        await requestExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        server.Logger.ErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task ExceptionHandler_ShouldDistinguishRequestAbortFromInternalCancellationBeforeResponseStarts()
    {
        var logger = new RecordingHandlerLogger();
        var handler = new UseCaseExceptionHandler(logger);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext
        {
            RequestAborted = cancellation.Token
        };
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(
            context,
            new OperationCanceledException(cancellation.Token),
            CancellationToken.None);

        handled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.ContentType.Should().BeNull();
        context.Response.Body.Length.Should().Be(0);
        logger.ErrorCount.Should().Be(0);
        logger.CancellationObserved.Task.IsCompletedSuccessfully.Should().BeTrue();
        logger.LastCancellationResponseStarted.Should().BeFalse();

        var activeLogger = new RecordingHandlerLogger();
        var activeHandler = new UseCaseExceptionHandler(activeLogger);
        var activeContext = new DefaultHttpContext();
        activeContext.Response.Body = new MemoryStream();
        activeContext.TraceIdentifier = "internal-timeout-trace";

        var internalHandled = await activeHandler.TryHandleAsync(
            activeContext,
            new OperationCanceledException("library timeout"),
            CancellationToken.None);

        internalHandled.Should().BeTrue();
        activeContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        activeContext.Response.ContentType.Should().StartWith("application/problem+json");
        activeContext.Response.Body.Position = 0;
        var body = await new StreamReader(activeContext.Response.Body).ReadToEndAsync();
        body.Should().Contain("internal_server_error").And.Contain("internal-timeout-trace");
        body.Should().NotContain("library timeout");
        activeLogger.ErrorCount.Should().Be(1);
    }

    [Fact]
    public async Task RequestAborted_AfterResponseStarts_ShouldKeepStartedResponseAndNeverTrackBusiness500()
    {
        var responseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await CancellationProbeServer.StartAsync(app =>
        {
            app.MapGet("/cancel-after-response", async context =>
            {
                try
                {
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync("started", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    responseStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                }
                finally
                {
                    requestExited.TrySetResult();
                }
            });
        });

        using var response = await server.Client.GetAsync(
            "/cancel-after-response",
            HttpCompletionOption.ResponseHeadersRead);
        await responseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");

        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[7];
        await stream.ReadExactlyAsync(buffer);
        Encoding.UTF8.GetString(buffer).Should().Be("started");

        response.Dispose();
        server.Client.CancelPendingRequests();
        await requestExited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        server.Logger.ErrorCount.Should().Be(0);

        var internalLogger = new RecordingHandlerLogger();
        var internalHandler = new UseCaseExceptionHandler(internalLogger);
        var startedFeatures = new FeatureCollection();
        startedFeatures.Set<IHttpResponseFeature>(new StartedHttpResponseFeature());
        var startedContext = new DefaultHttpContext(startedFeatures);

        var internalHandled = await internalHandler.TryHandleAsync(
            startedContext,
            new OperationCanceledException("downstream timeout"),
            CancellationToken.None);

        internalHandled.Should().BeFalse();
        startedContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        internalLogger.ErrorCount.Should().Be(1);
    }

    private sealed class StartedHttpResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = new MemoryStream();

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    private sealed class CancellationProbeServer(
        WebApplication application,
        HttpClient client,
        RecordingHandlerLogger logger) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public RecordingHandlerLogger Logger { get; } = logger;

        public static async Task<CancellationProbeServer> StartAsync(Action<WebApplication> mapEndpoints)
        {
            var logger = new RecordingHandlerLogger();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<ILogger<UseCaseExceptionHandler>>(logger);
            builder.Services.AddExceptionHandler<UseCaseExceptionHandler>();
            builder.Services.AddProblemDetails();

            var application = builder.Build();
            application.UseExceptionHandler();
            mapEndpoints(application);
            await application.StartAsync();

            var addresses = application.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()
                ?.Addresses
                ?? throw new InvalidOperationException("Cancellation probe server did not expose an address.");
            var address = addresses.Single(uri => new Uri(uri).Host is "127.0.0.1" or "localhost");
            var client = new HttpClient { BaseAddress = new Uri(address) };
            return new CancellationProbeServer(application, client, logger);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
        }
    }

    private sealed class RecordingHandlerLogger : ILogger<UseCaseExceptionHandler>
    {
        private int errorCount;

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ErrorCount => Volatile.Read(ref errorCount);

        public bool? LastCancellationResponseStarted { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Interlocked.Increment(ref errorCount);
            }

            var message = formatter(state, exception);
            if (logLevel == LogLevel.Debug &&
                message.Contains("cancellation propagated without ProblemDetails", StringComparison.Ordinal))
            {
                LastCancellationResponseStarted = message.Contains(
                    "ResponseStarted: True",
                    StringComparison.Ordinal);
                CancellationObserved.TrySetResult();
            }
        }
    }
}
