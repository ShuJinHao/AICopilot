using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Workflows;

public enum BranchType
{
    Tools,
    Knowledge,
    DataAnalysis,
    BusinessPolicy
}

public record BranchResult
{
    public BranchType Type { get; init; }

    public AiToolDefinition[]? Tools { get; init; }

    public string? Knowledge { get; init; }

    public string? DataAnalysis { get; init; }

    public string? BusinessPolicy { get; init; }

    public static BranchResult FromTools(AiToolDefinition[] tools) =>
        new() { Type = BranchType.Tools, Tools = tools };

    public static BranchResult FromKnowledge(string knowledge) =>
        new() { Type = BranchType.Knowledge, Knowledge = knowledge };

    public static BranchResult FromDataAnalysis(string result) =>
        new() { Type = BranchType.DataAnalysis, DataAnalysis = result };

    public static BranchResult FromBusinessPolicy(string result) =>
        new() { Type = BranchType.BusinessPolicy, BusinessPolicy = result };
}
