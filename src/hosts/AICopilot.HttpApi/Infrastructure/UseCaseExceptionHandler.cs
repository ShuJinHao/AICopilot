using AICopilot.Services.CrossCutting.Exceptions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Diagnostics;
namespace AICopilot.HttpApi.Infrastructure;

public sealed class UseCaseExceptionHandler(ILogger<UseCaseExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is ApiProblemException apiProblemException)
        {
            httpContext.Response.StatusCode = apiProblemException.StatusCode;
            httpContext.Response.ContentType = "application/problem+json";

            await httpContext.Response.WriteAsJsonAsync(
                ApiProblemDetailsFactory.Create(apiProblemException.StatusCode, apiProblemException.Problem),
                cancellationToken);

            return true;
        }

        if (exception is PersistenceCommitOutcomeUnknownException commitOutcomeUnknown)
        {
            logger.LogError(
                "AICopilot persistence commit outcome is unknown. TraceIdentifier: {TraceIdentifier}; CommitId: {CommitId}; ExceptionType: {ExceptionType}; InnerExceptionType: {InnerExceptionType}; OriginalMessage: {OriginalMessage}",
                httpContext.TraceIdentifier,
                commitOutcomeUnknown.CommitId,
                commitOutcomeUnknown.GetType().Name,
                commitOutcomeUnknown.InnerException?.GetType().Name ?? string.Empty,
                "hidden_by_security_policy");

            var persistenceProblem = new ApiProblemDescriptor(
                AppProblemCodes.PersistenceCommitOutcomeUnknown,
                "The write may have committed and must be reconciled before retrying.",
                new Dictionary<string, object?>
                {
                    ["traceId"] = httpContext.TraceIdentifier,
                    ["commitId"] = commitOutcomeUnknown.CommitId
                });

            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            httpContext.Response.ContentType = "application/problem+json";
            await httpContext.Response.WriteAsJsonAsync(
                ApiProblemDetailsFactory.Create(StatusCodes.Status503ServiceUnavailable, persistenceProblem),
                cancellationToken);

            return true;
        }

        logger.LogError(
            "Unhandled AICopilot API exception. TraceIdentifier: {TraceIdentifier}; ExceptionType: {ExceptionType}; InnerExceptionType: {InnerExceptionType}; OriginalMessage: {OriginalMessage}",
            httpContext.TraceIdentifier,
            exception.GetType().Name,
            exception.InnerException?.GetType().Name ?? string.Empty,
            "hidden_by_security_policy");

        var problem = new ApiProblemDescriptor(
            AppProblemCodes.InternalServerError,
            "Request failed unexpectedly. Contact support with the trace id.",
            new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier
            });

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(
            ApiProblemDetailsFactory.Create(StatusCodes.Status500InternalServerError, problem),
            cancellationToken);

        return true;
    }
}
