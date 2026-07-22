using System.Text.Json.Serialization;

namespace AICopilot.Core.AiGateway.Aggregates.AgentTasks;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentTaskType
{
    GeneralAgent = 0,
    DataAnalysis = 1,
    ReportGeneration = 2,
    PptGeneration = 3,
    ExcelGeneration = 4,
    RagAnswer = 5,
    FileProcessing = 6,
    CloudDataReport = 7
}

public enum AgentTaskStatus
{
    WaitingPlanApproval = 1,
    PlanApproved = 2,
    Running = 3,
    WaitingToolApproval = 4,
    GeneratingArtifacts = 5,
    WorkspaceReady = 6,
    WaitingFinalApproval = 7,
    Finalized = 8,
    Completed = 9,
    Rejected = 10,
    Failed = 11,
    Cancelled = 12,
    Draft = 100,
    Queued = 101,
    ReconciliationRequired = 102
}

public enum AgentTaskRiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum AgentStepType
{
    Plan = 0,
    DataQuery = 1,
    FileRead = 2,
    RagSearch = 3,
    Analysis = 4,
    ChartGeneration = 5,
    ArtifactGeneration = 6,
    Approval = 7,
    Finalize = 8
}

public enum AgentStepStatus
{
    Pending = 0,
    WaitingApproval = 1,
    Approved = 2,
    Running = 3,
    Completed = 4,
    Failed = 5,
    Skipped = 6,
    Cancelled = 7
}

public enum AgentTaskRunAttemptStatus
{
    Running = 0,
    WaitingApproval = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    Created = 5,
    ReconciliationRequired = 6
}

public enum AgentTaskRunTriggerType
{
    Manual = 0,
    ApprovalResume = 1,
    Retry = 2
}

public enum AgentTaskRunQueueStatus
{
    Queued = 0,
    Claimed = 1,
    Leased = Claimed,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    DeadLetter = 5,
    Started = 6
}
