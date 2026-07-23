using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.Contracts;

public sealed record AgentTaskPlanFreshReadRequest(
    Guid TaskId,
    string ExpectedPlanJson,
    AgentTaskStatus ExpectedStatus,
    IReadOnlyCollection<AgentTaskPlanExecutionStepContract> ExpectedExecutionSteps);

/// <summary>
/// Immutable execution fields covered by the confirmed Plan v2 digest. Runtime
/// state/output fields intentionally do not belong to this projection.
/// </summary>
public sealed record AgentTaskPlanExecutionStepContract(
    int StepIndex,
    string Title,
    string Description,
    AgentStepType StepType,
    string? ToolCode,
    bool RequiresApproval,
    string? InputJson);

public sealed record AgentTaskPlanFreshReadDecision(
    bool IsMatch,
    string? ErrorCode,
    string? SafeDetail)
{
    public static AgentTaskPlanFreshReadDecision Match { get; } =
        new(true, null, null);

    public static AgentTaskPlanFreshReadDecision Mismatch(
        string errorCode,
        string safeDetail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeDetail);
        return new AgentTaskPlanFreshReadDecision(false, errorCode, safeDetail);
    }
}

/// <summary>
/// Reloads the persisted task through an independent persistence context and
/// compares the exact plan bytes and lifecycle status with the candidate entity.
/// </summary>
public interface IAgentTaskPlanFreshReadVerifier
{
    Task<AgentTaskPlanFreshReadDecision> VerifyAsync(
        AgentTaskPlanFreshReadRequest request,
        CancellationToken cancellationToken);
}

public sealed record AgentTaskPlanPersistenceValidationRequest(
    Guid TaskId,
    string PlanJson);

public sealed record AgentTaskPlanPersistenceValidationDecision(
    bool IsValid,
    string? ErrorCode,
    string? SafeDetail,
    IReadOnlyCollection<AgentTaskPlanExecutionStepContract>? ExecutionSteps)
{
    public static AgentTaskPlanPersistenceValidationDecision Valid(
        IReadOnlyCollection<AgentTaskPlanExecutionStepContract> executionSteps)
    {
        ArgumentNullException.ThrowIfNull(executionSteps);
        return new AgentTaskPlanPersistenceValidationDecision(true, null, null, executionSteps);
    }

    public static AgentTaskPlanPersistenceValidationDecision Invalid(
        string errorCode,
        string safeDetail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeDetail);
        return new AgentTaskPlanPersistenceValidationDecision(false, errorCode, safeDetail, null);
    }
}

public interface IAgentTaskPlanPersistencePolicy
{
    AgentTaskPlanPersistenceValidationDecision Validate(
        AgentTaskPlanPersistenceValidationRequest request);
}

public sealed record AgentPlanPublicFailureDisclosure(
    string Code,
    string Detail,
    string UserFacingMessage);

public sealed record AgentPlanPersistenceFailureDisclosure(
    Guid TaskId,
    string Code,
    string Detail,
    string UserFacingMessage);

public sealed record AgentPlanPublicResultFailureMatch(
    AgentPlanPublicFailureDisclosure Disclosure,
    Guid? TaskId);

/// <summary>
/// Single public disclosure owner for plan-integrity codes that may cross REST
/// or SSE boundaries. Callers must never derive these texts from exception or
/// result details because those values can contain plan-controlled content.
/// </summary>
public static class AgentPlanPublicFailureDisclosurePolicy
{
    public static AgentPlanPublicFailureDisclosure? Resolve(string? code)
    {
        if (!AppProblemCodes.IsAgentPlanIntegrityCode(code))
        {
            return null;
        }

        return code switch
        {
            AppProblemCodes.AgentPlanInvalid => new AgentPlanPublicFailureDisclosure(
                AppProblemCodes.AgentPlanInvalid,
                "Agent task plan failed integrity validation.",
                "计划草案未通过完整性校验，请刷新后重新生成。"),
            AppProblemCodes.AgentPlanSchemaInvalid => new AgentPlanPublicFailureDisclosure(
                AppProblemCodes.AgentPlanSchemaInvalid,
                "Agent task plan does not match the required schema.",
                "计划草案结构无效，请刷新后重新生成。"),
            AppProblemCodes.PlanPayloadTooLarge => new AgentPlanPublicFailureDisclosure(
                AppProblemCodes.PlanPayloadTooLarge,
                "Agent task plan exceeds the maximum allowed size of 262144 UTF-8 bytes.",
                "计划草案超过 262144 UTF-8 字节上限，请缩小请求后重试。"),
            _ => null
        };
    }

    public static AgentPlanPersistenceFailureDisclosure? Resolve(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return FindPublicIntegrityFailure(exception);
    }

    public static AgentPlanPublicResultFailureMatch? ResolveResultErrors(
        IEnumerable<object>? errors)
    {
        if (errors is null)
        {
            return null;
        }

        foreach (var problem in errors.OfType<ApiProblemDescriptor>())
        {
            var disclosure = Resolve(problem.Code);
            if (disclosure is not null)
            {
                var taskId = problem.Extensions is not null &&
                    problem.Extensions.TryGetValue("taskId", out var taskIdValue) &&
                    taskIdValue is Guid taskGuid &&
                    taskGuid != Guid.Empty
                        ? taskGuid
                        : (Guid?)null;
                return new AgentPlanPublicResultFailureMatch(disclosure, taskId);
            }
        }

        return null;
    }

    private static AgentPlanPersistenceFailureDisclosure? FindPublicIntegrityFailure(
        Exception exception)
    {
        if (exception is AgentTaskPlanPersistenceIntegrityException integrityFailure)
        {
            var publicFailure = Resolve(integrityFailure.ErrorCode);
            if (publicFailure is not null)
            {
                return new AgentPlanPersistenceFailureDisclosure(
                    integrityFailure.TaskId,
                    publicFailure.Code,
                    publicFailure.Detail,
                    publicFailure.UserFacingMessage);
            }
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var innerException in aggregate.InnerExceptions)
            {
                var nested = FindPublicIntegrityFailure(innerException);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }

        return exception.InnerException is null
            ? null
            : FindPublicIntegrityFailure(exception.InnerException);
    }
}

public sealed class AgentTaskPlanPersistenceIntegrityException : InvalidOperationException
{
    public AgentTaskPlanPersistenceIntegrityException(
        Guid taskId,
        string errorCode,
        string safeDetail)
        : base("Agent task plan persistence integrity validation failed.")
    {
        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("Agent task id cannot be empty.", nameof(taskId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeDetail);
        TaskId = taskId;
        ErrorCode = errorCode;
        SafeDetail = safeDetail;
    }

    public Guid TaskId { get; }

    public string ErrorCode { get; }

    public string SafeDetail { get; }
}
