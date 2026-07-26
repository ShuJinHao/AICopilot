using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Workflows;

public enum BranchType
{
    Tools,
    Knowledge,
    DataAnalysis,
    BusinessPolicy
}

public enum BranchExecutionStatus
{
    Skipped,
    Empty,
    Succeeded,
    Failed
}

public sealed record BranchResult
{
    public required BranchType Type { get; init; }

    public required BranchExecutionStatus Status { get; init; }

    public bool IsRequired { get; init; }

    public AiToolDefinition[]? Tools { get; init; }

    public string? Knowledge { get; init; }

    public string? DataAnalysis { get; init; }

    public string? BusinessPolicy { get; init; }

    public string? FailureCode { get; init; }

    public string? SafeMessage { get; init; }

    public IReadOnlyCollection<AgentWorkflowEvidence> Evidence { get; init; } = [];

    internal IReadOnlyCollection<AgentBranchEvidenceSeed> EvidenceSeeds { get; init; } = [];

    public static BranchResult Skipped(BranchType type) =>
        new() { Type = type, Status = BranchExecutionStatus.Skipped };

    public static BranchResult Empty(BranchType type) =>
        new() { Type = type, Status = BranchExecutionStatus.Empty };

    public static BranchResult Failed(BranchType type, string failureCode, string safeMessage) =>
        new()
        {
            Type = type,
            Status = BranchExecutionStatus.Failed,
            FailureCode = failureCode,
            SafeMessage = safeMessage
        };

    public static BranchResult FromTools(AiToolDefinition[] tools) =>
        tools.Length == 0
            ? Empty(BranchType.Tools)
            : new() { Type = BranchType.Tools, Status = BranchExecutionStatus.Succeeded, Tools = tools };

    public static BranchResult FromKnowledge(string knowledge) =>
        string.IsNullOrWhiteSpace(knowledge)
            ? Empty(BranchType.Knowledge)
            : new() { Type = BranchType.Knowledge, Status = BranchExecutionStatus.Succeeded, Knowledge = knowledge };

    public static BranchResult FromDataAnalysis(string result) =>
        string.IsNullOrWhiteSpace(result)
            ? Empty(BranchType.DataAnalysis)
            : new() { Type = BranchType.DataAnalysis, Status = BranchExecutionStatus.Succeeded, DataAnalysis = result };

    internal static BranchResult FromDataAnalysis(
        string result,
        IReadOnlyCollection<AgentBranchEvidenceSeed> evidenceSeeds) =>
        string.IsNullOrWhiteSpace(result) || evidenceSeeds.Count == 0
            ? Empty(BranchType.DataAnalysis)
            : new()
            {
                Type = BranchType.DataAnalysis,
                Status = BranchExecutionStatus.Succeeded,
                DataAnalysis = result,
                EvidenceSeeds = evidenceSeeds
            };

    public static BranchResult FromBusinessPolicy(string result) =>
        string.IsNullOrWhiteSpace(result)
            ? Empty(BranchType.BusinessPolicy)
            : new() { Type = BranchType.BusinessPolicy, Status = BranchExecutionStatus.Succeeded, BusinessPolicy = result };

    public BranchResult WithRequirement(bool isRequired) => this with { IsRequired = isRequired };
}
