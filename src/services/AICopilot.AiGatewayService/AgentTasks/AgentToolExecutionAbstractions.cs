using System.Text.Json;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentToolExecutionContext(
    AgentTask Task,
    ArtifactWorkspace Workspace,
    AgentTaskPlanDocument Plan,
    AgentStep Step,
    AgentTaskRunState State,
    ToolRegistration ToolRegistration,
    CancellationToken CancellationToken,
    IReadOnlyCollection<AgentEvidenceRecord>? InputEvidence = null,
    AgentTaskRunAttemptId? RunAttemptId = null,
    AgentNodeRunId? NodeRunId = null);

internal sealed record AgentToolOutputSnapshot(
    string CanonicalJson,
    int Utf8ByteCount)
{
    internal JsonElement ToJsonElement()
    {
        using var document = JsonDocument.Parse(CanonicalJson);
        return document.RootElement.Clone();
    }

    internal static AgentToolOutputSnapshot FromValidated(ToolOutputValidationResult validation)
    {
        if (!validation.IsValid || validation.CanonicalJson is null)
        {
            throw new AgentToolExecutionException(
                validation.IsPayloadTooLarge
                    ? AppProblemCodes.EvidencePayloadTooLarge
                    : AppProblemCodes.ToolOutputSchemaInvalid,
                validation.Error ?? "Tool output could not be captured as canonical JSON.");
        }

        return new AgentToolOutputSnapshot(validation.CanonicalJson, validation.Utf8ByteCount);
    }

    internal static AgentToolOutputSnapshot Capture(object? output) =>
        FromValidated(ToolOutputSchemaValidator.CanonicalizeForPersistence(output));
}

internal sealed record AgentToolExecutionResult(
    AgentToolOutputSnapshot ContractOutput,
    AgentToolOutputSnapshot DurableOutput)
{
    public static AgentToolExecutionResult From(object output)
    {
        var snapshot = AgentToolOutputSnapshot.Capture(output);
        return new AgentToolExecutionResult(snapshot, snapshot);
    }

    public static AgentToolExecutionResult FromValidatedProviderOutput(
        ToolRegistration registration,
        ToolOutputValidationResult contractOutput,
        object durableOutput)
    {
        if (registration.ProviderType is not (ToolProviderType.Mcp or ToolProviderType.MockMcp))
        {
            throw new ArgumentException(
                "Contract/durable split is reserved for MCP provider envelopes.",
                nameof(registration));
        }

        return new AgentToolExecutionResult(
            AgentToolOutputSnapshot.FromValidated(contractOutput),
            AgentToolOutputSnapshot.Capture(durableOutput));
    }
}

internal interface IAgentToolExecutor
{
    bool CanExecute(ToolRegistration tool, AgentStep step);

    Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context);
}

internal sealed class AgentToolExecutorResolver(IEnumerable<IAgentToolExecutor> executors)
{
    private readonly IAgentToolExecutor[] executors = executors.ToArray();

    public IAgentToolExecutor Resolve(ToolRegistration tool, AgentStep step)
    {
        return executors.FirstOrDefault(executor => executor.CanExecute(tool, step))
               ?? throw new AgentToolExecutionException(
                   AppProblemCodes.ToolExecutionNotFound,
                   $"No runtime executor is available for tool '{tool.ToolCode}'.");
    }
}

internal sealed class AgentToolExecutionException(
    string code,
    string message,
    int modelCalls = 0) : InvalidOperationException(message)
{
    public string Code { get; } = code;

    public int ModelCalls { get; } = modelCalls is >= 0 and <= 8
        ? modelCalls
        : throw new ArgumentOutOfRangeException(nameof(modelCalls));
}
