namespace AICopilot.Core.AiGateway.Aggregates.AgentTasks;

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
    DraftPlan = 0,
    WaitingPlanApproval = 1,
    Approved = 2,
    Running = 3,
    WaitingToolApproval = 4,
    GeneratingArtifacts = 5,
    WorkspaceReady = 6,
    WaitingFinalApproval = 7,
    Completed = 8,
    Failed = 9,
    Cancelled = 10,
    Rejected = 11
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
    Running = 1,
    WaitingApproval = 2,
    Completed = 3,
    Failed = 4,
    Skipped = 5
}
