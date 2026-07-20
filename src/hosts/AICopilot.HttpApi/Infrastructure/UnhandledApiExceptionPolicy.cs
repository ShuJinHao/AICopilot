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

        var publicPlanFailure = AgentPlanPublicFailureDisclosurePolicy.Resolve(exception);
        if (publicPlanFailure is not null)
        {
            decision = new UnhandledApiExceptionDecision(
                StatusCodes.Status400BadRequest,
                new ApiProblemDescriptor(
                    publicPlanFailure.Code,
                    publicPlanFailure.Detail,
                    new Dictionary<string, object?>
                    {
                        ["taskId"] = publicPlanFailure.TaskId
                    }),
                exception.GetType().Name,
                exception.InnerException?.GetType().Name ?? string.Empty);
            return true;
        }

        if (exception is AgentTaskPlanPersistenceIntegrityException planIntegrityFailure)
        {
            var isToolOutputFailure = planIntegrityFailure.ErrorCode == AppProblemCodes.ToolOutputSchemaInvalid;
            decision = new UnhandledApiExceptionDecision(
                StatusCodes.Status500InternalServerError,
                new ApiProblemDescriptor(
                    isToolOutputFailure
                        ? AppProblemCodes.ToolOutputSchemaInvalid
                        : AppProblemCodes.InternalServerError,
                    isToolOutputFailure
                        ? "Tool output failed its closed schema or durable-snapshot binding checks."
                        : "Request failed unexpectedly. Contact support with the trace id.",
                    new Dictionary<string, object?>
                    {
                        ["taskId"] = planIntegrityFailure.TaskId
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
