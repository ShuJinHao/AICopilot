using AICopilot.Services.CrossCutting.Exceptions;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Http;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Diagnostics;
namespace AICopilot.HttpApi.Infrastructure;

public sealed class UseCaseExceptionHandler(ILogger<UseCaseExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug(
                "AICopilot API cancellation propagated without ProblemDetails. TraceIdentifier: {TraceIdentifier}; RequestAborted: {RequestAborted}; ResponseStarted: {ResponseStarted}",
                httpContext.TraceIdentifier,
                httpContext.RequestAborted.IsCancellationRequested,
                httpContext.Response.HasStarted);
            return false;
        }

        if (exception is ApiProblemException apiProblemException)
        {
            httpContext.Response.StatusCode = apiProblemException.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(
                ApiProblemDetailsFactory.Create(
                    apiProblemException.StatusCode,
                    apiProblemException.Problem,
                    traceIdentifier: httpContext.TraceIdentifier),
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
        }

        if (!UnhandledApiExceptionPolicy.TryCreate(exception, out var decision))
        {
            return false;
        }

        logger.LogError(
            "Unhandled AICopilot API exception. TraceIdentifier: {TraceIdentifier}; ExceptionType: {ExceptionType}; InnerExceptionType: {InnerExceptionType}; OriginalMessage: {OriginalMessage}",
            httpContext.TraceIdentifier,
            decision.ExceptionType,
            decision.InnerExceptionType,
            "hidden_by_security_policy");

        if (httpContext.Response.HasStarted)
        {
            // The failure is still tracked, but an already-started response cannot be safely
            // rewritten as ProblemDetails. Returning false lets the server terminate the stream.
            return false;
        }

        httpContext.Response.StatusCode = decision.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(
            ApiProblemDetailsFactory.Create(
                decision.StatusCode,
                decision.Problem,
                traceIdentifier: httpContext.TraceIdentifier),
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }
}
