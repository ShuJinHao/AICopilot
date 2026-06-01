namespace AICopilot.Services.Contracts;

public static class CloudReadonlySandboxAgentTrialMarkers
{
    public const string SourceType = "CloudReadonly";
    public const string SourceMode = "CloudReadonlySandbox";
    public const string SourceLabel = "Cloud 只读 Sandbox（非生产）";
    public const string Boundary = "SandboxAgentTrial";
    public const string ToolCode = "query_cloud_sandbox_readonly";
}

public static class CloudReadonlySandboxControlledTrialMarkers
{
    public const string Boundary = "SandboxControlledTrial";
    public const string TrialMode = "ControlledGoal";
    public const string FixedScenarioTrialMode = "FixedScenario";
}

public static class CloudReadonlyPilotReadinessMarkers
{
    public const string SourceType = "CloudReadonly";
    public const string SourceMode = "CloudReadonlyPilotReadiness";
    public const string SourceLabel = "Cloud 只读 Pilot 准入演练（非生产）";
    public const string Boundary = "PilotReadinessRehearsal";
    public const string ToolCode = "query_cloud_pilot_readiness_readonly";
}

public static class CloudReadonlyProductionPilotMarkers
{
    public const string SourceType = "CloudReadonly";
    public const string SourceMode = "CloudReadonlyProductionPilot";
    public const string SourceLabel = "Cloud 生产只读 Pilot";
    public const string Boundary = "ProductionPilot";
    public const string ToolCode = "query_cloud_production_pilot_readonly";
}

public static class CloudReadonlyProductionControlledPilotMarkers
{
    public const string SourceType = "CloudReadonly";
    public const string SourceMode = "CloudReadonlyProductionControlledPilot";
    public const string SourceLabel = "Cloud 生产只读 Controlled Pilot";
    public const string Boundary = "ProductionControlledPilot";
    public const string ToolCode = "query_cloud_production_controlled_readonly";
    public const string TrialMode = "ProductionControlledGoal";
}

public static class CloudReadonlySourceMarkers
{
    public const string SimulationSourceMode = "Simulation";
    public const string RealSourceMode = "Real";
    public const string SimulationSourceLabel = "模拟 Cloud 只读数据";
}
