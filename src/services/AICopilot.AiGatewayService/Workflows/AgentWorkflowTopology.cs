namespace AICopilot.AiGatewayService.Workflows;

public enum AgentWorkflowStageKind
{
    Sequential,
    ParallelFanOut,
    Finalization
}

public sealed record AgentWorkflowStageDescriptor(
    string Id,
    AgentWorkflowStageKind Kind,
    int Order);

public sealed record AgentWorkflowParallelBranchDescriptor(
    BranchType BranchType,
    int Order);

public static class AgentWorkflowTopology
{
    public static IReadOnlyList<AgentWorkflowStageDescriptor> Stages { get; } =
    [
        new("IntentRouting", AgentWorkflowStageKind.Sequential, 10),
        new("CapabilityDiscovery", AgentWorkflowStageKind.Sequential, 20),
        new("ParallelFanOut", AgentWorkflowStageKind.ParallelFanOut, 30),
        new("ContextAggregation", AgentWorkflowStageKind.Sequential, 40),
        new("FinalAgentBuild", AgentWorkflowStageKind.Sequential, 50),
        new("FinalAgentRun", AgentWorkflowStageKind.Finalization, 60)
    ];

    public static IReadOnlyList<AgentWorkflowParallelBranchDescriptor> ParallelBranches { get; } =
    [
        new(BranchType.Tools, 10),
        new(BranchType.Knowledge, 20),
        new(BranchType.DataAnalysis, 30),
        new(BranchType.BusinessPolicy, 40)
    ];
}
