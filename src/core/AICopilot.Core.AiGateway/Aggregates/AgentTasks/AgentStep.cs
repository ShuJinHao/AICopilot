using System.Text.Json;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.AgentTasks;

public sealed class AgentStep : IEntity<AgentStepId>
{
    private AgentStep()
    {
    }

    internal AgentStep(
        AgentTaskId taskId,
        int stepIndex,
        string title,
        string description,
        AgentStepType stepType,
        string? toolCode,
        bool requiresApproval,
        string? inputJson = null)
    {
        if (stepIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepIndex), "Agent step index must be greater than zero.");
        }

        Id = AgentStepId.New();
        TaskId = taskId;
        StepIndex = stepIndex;
        Title = NormalizeRequired(title, nameof(title), 200);
        Description = NormalizeOptional(description, 1000) ?? string.Empty;
        StepType = stepType;
        ToolCode = NormalizeOptional(toolCode, 100);
        RequiresApproval = requiresApproval;
        InputJson = NormalizeStructuredJson(
            inputJson,
            enforceNodeToolInputPolicy: true,
            nameof(inputJson));
        Status = requiresApproval ? AgentStepStatus.WaitingApproval : AgentStepStatus.Pending;
    }

    public AgentStepId Id { get; private set; }

    public AgentTaskId TaskId { get; private set; }

    public int StepIndex { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public AgentStepType StepType { get; private set; }

    public AgentStepStatus Status { get; private set; }

    public string? ToolCode { get; private set; }

    public bool RequiresApproval { get; private set; }

    public string? InputJson { get; private set; }

    public string? OutputJson { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public void Start(DateTimeOffset nowUtc)
    {
        if (Status is not AgentStepStatus.Pending and not AgentStepStatus.Approved)
        {
            throw new InvalidOperationException("Only pending or approved agent steps can start.");
        }

        Status = AgentStepStatus.Running;
        StartedAt = nowUtc;
    }

    public void Complete(string? outputJson, DateTimeOffset nowUtc)
    {
        if (Status is not AgentStepStatus.Running and not AgentStepStatus.Approved)
        {
            throw new InvalidOperationException("Only running or approved agent steps can complete.");
        }

        OutputJson = NormalizeStructuredJson(
            outputJson,
            enforceNodeToolInputPolicy: false,
            nameof(outputJson));
        Status = AgentStepStatus.Completed;
        FinishedAt = nowUtc;
    }

    public void Fail(string errorMessage, DateTimeOffset nowUtc)
    {
        ErrorMessage = NormalizeRequired(errorMessage, nameof(errorMessage), 2000);
        Status = AgentStepStatus.Failed;
        FinishedAt = nowUtc;
    }

    public void Approve()
    {
        if (!RequiresApproval || Status != AgentStepStatus.WaitingApproval)
        {
            throw new InvalidOperationException("Only approval-waiting agent steps can be approved.");
        }

        Status = AgentStepStatus.Approved;
    }

    public void Cancel(DateTimeOffset nowUtc)
    {
        if (Status is AgentStepStatus.Completed or AgentStepStatus.Failed or AgentStepStatus.Cancelled)
        {
            throw new InvalidOperationException("Completed, failed, or cancelled agent steps cannot be cancelled again.");
        }

        Status = AgentStepStatus.Cancelled;
        FinishedAt = nowUtc;
    }

    public void ResetForRetry()
    {
        if (Status == AgentStepStatus.Completed)
        {
            return;
        }

        Status = AgentStepStatus.Pending;
        OutputJson = null;
        ErrorMessage = null;
        StartedAt = null;
        FinishedAt = null;
    }

    public void WaitForApproval()
    {
        if (Status != AgentStepStatus.Pending)
        {
            throw new InvalidOperationException("Only pending agent steps can wait for approval.");
        }

        RequiresApproval = true;
        Status = AgentStepStatus.WaitingApproval;
    }

    private static string NormalizeRequired(string value, string paramName, int maxLength)
    {
        var normalized = NormalizeOptional(value, maxLength);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }

    private static string? NormalizeStructuredJson(
        string? value,
        bool enforceNodeToolInputPolicy,
        string paramName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            return null;
        }

        if (enforceNodeToolInputPolicy)
        {
            var input = AgentStructuredPayloadPolicyV1.NormalizeNodeToolInput(normalized);
            if (!input.IsValid)
            {
                throw new ArgumentException(
                    input.Error ?? $"{paramName} violates {AgentStructuredPayloadPolicyV1.PolicyVersion}.",
                    paramName);
            }

            return input.CanonicalJson;
        }
        var output = AgentStructuredPayloadPolicyV1.NormalizeInlineOutput(normalized);
        if (!output.IsValid)
        {
            throw new ArgumentException(
                output.Error ?? $"{paramName} violates {AgentStructuredPayloadPolicyV1.InlineOutputPolicyVersion}.",
                paramName);
        }

        return output.CanonicalJson;
    }
}
