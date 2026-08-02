using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Http;
using System.Diagnostics.CodeAnalysis;

namespace AICopilot.HttpApi.Infrastructure;

internal sealed record UnhandledApiExceptionDecision(
    int StatusCode,
    ApiProblemDescriptor Problem,
    string ExceptionType,
    string InnerExceptionType);

internal static class UnhandledApiExceptionPolicy
{
    public static bool TryCreate(
        Exception exception,
        [NotNullWhen(true)] out UnhandledApiExceptionDecision? decision)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is PersistenceCommitOutcomeUnknownException commitOutcomeUnknown)
        {
            decision = new UnhandledApiExceptionDecision(
                StatusCodes.Status503ServiceUnavailable,
                new ApiProblemDescriptor(
                    AppProblemCodes.PersistenceCommitOutcomeUnknown,
                    "The write may have committed and must be reconciled before retrying.",
                    new Dictionary<string, object?>
                    {
                        ["commitId"] = commitOutcomeUnknown.CommitId
                    }),
                exception.GetType().Name,
                exception.InnerException?.GetType().Name ?? string.Empty);
            return true;
        }

        decision = new UnhandledApiExceptionDecision(
            StatusCodes.Status500InternalServerError,
            new ApiProblemDescriptor(
                AppProblemCodes.InternalServerError,
                "Request failed unexpectedly. Contact support with the trace id."),
            exception.GetType().Name,
            exception.InnerException?.GetType().Name ?? string.Empty);
        return true;
    }
}
