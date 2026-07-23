using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.AiGatewayService.Tools;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentParallelReadNodeExecutionRequest(
    AgentTask Task,
    ArtifactWorkspace Workspace,
    AgentTaskPlanDocument Plan,
    AgentPlanNodeDocument NodeContract,
    AgentStep Step,
    ToolRegistration ToolRegistration);

internal sealed record AgentParallelReadNodeExecutionOutcome(
    AgentTaskRunState State,
    AgentToolExecutionResult? ExecutionResult,
    int ToolCallCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? FailureCode,
    string? SafeMessage)
{
    public bool IsSuccess => ExecutionResult is not null && FailureCode is null;
}

/// <summary>
/// Executes only independent replay-safe read roots. Each instance is resolved from
/// its own DI scope so provider repositories and DbContexts are never shared across
/// concurrent DAG branches. Claiming, lease renewal, Evidence normalization and
/// checkpoint commits remain serialized by the parent runtime.
/// </summary>
internal sealed class AgentParallelReadNodeExecutor(
    AgentBuiltInToolDispatcher builtInToolDispatcher)
{
    private static readonly IReadOnlySet<string> AllowedParallelKinds = new HashSet<string>(
    [
        "CloudReadNode",
        "GovernedDataReadNode",
        "KnowledgeRetrievalNode"
    ], StringComparer.Ordinal);

    public async Task<AgentParallelReadNodeExecutionOutcome> ExecuteAsync(
        AgentParallelReadNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var state = new AgentTaskRunState();
        var toolCallCount = 0;

        if (request.Plan.TopologyProfile != "DagV1" ||
            request.NodeContract.DependsOn.Count != 0 ||
            request.NodeContract.EvidenceSelectors.Count != 0 ||
            request.NodeContract.ApprovalPolicy.Required ||
            request.NodeContract.SideEffectClass != "ReadOnly" ||
            !AllowedParallelKinds.Contains(request.NodeContract.NodeKind) ||
            request.NodeContract.RequestedToolCodes.Count != 1 ||
            !string.Equals(
                request.NodeContract.RequestedToolCodes.Single(),
                request.ToolRegistration.ToolCode,
                StringComparison.Ordinal) ||
            request.ToolRegistration.TargetType != ToolRegistrationTargetType.AgentRuntime ||
            request.ToolRegistration.ProviderType is ToolProviderType.Mcp or ToolProviderType.MockMcp)
        {
            return Failure(
                state,
                toolCallCount,
                startedAtUtc,
                AppProblemCodes.AgentPlanInvalid,
                "Parallel DAG execution accepts only independent built-in read roots.");
        }

        try
        {
            var inputValidation = ToolInputSchemaValidator.ValidateAndParse(
                request.Step.InputJson,
                request.ToolRegistration.InputSchemaJson);
            if (!inputValidation.IsValid)
            {
                throw new AgentToolExecutionException(
                    AppProblemCodes.AgentPlanSchemaInvalid,
                    inputValidation.Error ?? "Agent step input does not match registry schema.");
            }

            var executor = new RuntimeBuiltInAgentToolExecutor(builtInToolDispatcher.ExecuteAsync);
            var context = new AgentToolExecutionContext(
                request.Task,
                request.Workspace,
                request.Plan,
                request.Step,
                state,
                request.ToolRegistration,
                cancellationToken,
                InputEvidence: []);
            var executionResult = await AgentNodeExecutionPlane.ExecuteAsync(
                AgentNodeExecutionContract.ForDurable(request.NodeContract),
                executionToken => AgentToolExecutionTimeout.ExecuteAsync(
                    executor,
                    context with { CancellationToken = executionToken }),
                cancellationToken,
                attemptCount => toolCallCount = attemptCount);
            AgentToolRuntimeOutputGate.EnsureValid(request.ToolRegistration, executionResult);

            var artifactBinding = AgentArtifactOutputBindingGate.Validate(
                request.Task,
                request.Workspace,
                request.Step,
                request.ToolRegistration,
                executionResult.ContractOutput);
            if (!artifactBinding.IsValid)
            {
                throw new AgentToolExecutionException(
                    AppProblemCodes.ToolOutputSchemaInvalid,
                    artifactBinding.Error ?? "Read-node output violates its workspace binding contract.");
            }

            return new AgentParallelReadNodeExecutionOutcome(
                state,
                executionResult,
                Math.Max(1, toolCallCount),
                startedAtUtc,
                DateTimeOffset.UtcNow,
                FailureCode: null,
                SafeMessage: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(
                state,
                Math.Max(1, toolCallCount),
                startedAtUtc,
                AgentToolExecutionAuditBuilder.ResolveExecutionErrorCode(
                    exception,
                    request.Step,
                    request.ToolRegistration),
                AgentToolExecutionAuditBuilder.BuildSafeExceptionSummary(exception));
        }
    }

    private static AgentParallelReadNodeExecutionOutcome Failure(
        AgentTaskRunState state,
        int toolCallCount,
        DateTimeOffset startedAtUtc,
        string failureCode,
        string safeMessage) =>
        new(
            state,
            ExecutionResult: null,
            toolCallCount,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            failureCode,
            safeMessage);

}
