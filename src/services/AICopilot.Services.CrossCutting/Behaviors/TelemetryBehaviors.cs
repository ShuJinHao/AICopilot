using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AICopilot.Services.CrossCutting.Behaviors;

public sealed class TelemetryBehavior<TRequest, TResponse>(
    ILogger<TelemetryBehavior<TRequest, TResponse>> logger) :
    IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var telemetry = RequestTelemetryScope.Start(typeof(TRequest), "request");
        try
        {
            var response = await next(cancellationToken);
            telemetry.Complete(logger, null);
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            telemetry.Complete(logger, exception);
            throw;
        }
        finally
        {
            telemetry.Dispose();
        }
    }
}

public sealed class TelemetryStreamBehavior<TRequest, TResponse>(
    ILogger<TelemetryStreamBehavior<TRequest, TResponse>> logger) :
    IStreamPipelineBehavior<TRequest, TResponse> where TRequest : IStreamRequest<TResponse>
{
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var telemetry = RequestTelemetryScope.Start(typeof(TRequest), "stream");
        Exception? failure = null;
        try
        {
            await using var enumerator = next().GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    failure = exception;
                    throw;
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            telemetry.Complete(logger, failure);
            telemetry.Dispose();
        }
    }
}

internal sealed class RequestTelemetryScope : IDisposable
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly Activity? startedActivity;
    private readonly Activity? activity;
    private readonly string requestName;
    private readonly string kind;

    private RequestTelemetryScope(Type requestType, string kind)
    {
        requestName = requestType.FullName ?? requestType.Name;
        this.kind = kind;
        startedActivity = Activity.Current is null
            ? PipelineTelemetry.ActivitySource.StartActivity($"MediatR {requestType.Name}")
            : null;
        activity = startedActivity ?? Activity.Current;
        activity?.SetTag("aicopilot.mediatr.request", requestName);
        activity?.SetTag("aicopilot.mediatr.kind", kind);
    }

    public static RequestTelemetryScope Start(Type requestType, string kind)
    {
        return new RequestTelemetryScope(requestType, kind);
    }

    public void Complete(ILogger logger, Exception? exception)
    {
        stopwatch.Stop();
        activity?.SetTag("aicopilot.mediatr.elapsed_ms", stopwatch.Elapsed.TotalMilliseconds);
        if (exception is null)
        {
            activity?.SetTag("aicopilot.mediatr.status", "ok");
            logger.LogInformation(
                "MediatR {Kind} {RequestName} completed in {ElapsedMilliseconds} ms.",
                kind,
                requestName,
                stopwatch.Elapsed.TotalMilliseconds);
            return;
        }

        activity?.SetTag("aicopilot.mediatr.status", "error");
        activity?.SetTag("error.type", exception.GetType().Name);
        logger.LogWarning(
            "MediatR {Kind} {RequestName} failed after {ElapsedMilliseconds} ms. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
            kind,
            requestName,
            stopwatch.Elapsed.TotalMilliseconds,
            exception.GetType().Name);
    }

    public void Dispose()
    {
        startedActivity?.Dispose();
    }
}
