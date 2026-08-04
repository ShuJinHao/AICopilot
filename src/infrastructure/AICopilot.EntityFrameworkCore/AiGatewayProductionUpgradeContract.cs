namespace AICopilot.EntityFrameworkCore;

public static class AiGatewayProductionUpgradeContract
{
    public const string ProductionBaselineSha = "06b92eee9d714329ca7def2d3aff433d729b8420";
    public const string LastProductionMigrationId =
        "20260722170000_AddArtifactEvidenceSetDigest";
    public const string CurrentUpgradeMigrationId =
        "20260804055544_UpgradeHarnessRuntimeFromProduction";
    public const string EfProductVersion = "10.0.9";

    public const string ExpectedProductionHistorySha256 =
        "c58def891d65a2134be511380cf99640ab4f132db9fffa9adfa7928dc827c8cc";
    public const string ExpectedProductionSchemaSha256 =
        "c8dae93a351576c5eafb2969ed337baa3e86ec105f966aa1f45e6244837236df";
    public const string ExpectedProductionColumnPrecisionProjectionSha256 =
        "1a60eb36f7700abb1e8c06807eac15d8a8314f11d1587b6fbc1b4554a77b97f0";
    public const int ExpectedProductionColumnCount = 453;
    public const int ExpectedProductionTemporalColumnCount = 58;

    public const string ExpectedCurrentSchemaSha256 =
        "32c62fcd2017822b362c15b5d47daf5059bdbf9e402ad2fe82e4c7f2cfcc828a";

    public static IReadOnlyList<string> ProductionMigrationIds { get; } =
    [
        "20260515030952_AiGatewayFreshBaseline",
        "20260519101000_AddPromptPolicyP1",
        "20260519112000_AddToolGovernanceP4",
        "20260520055258_AddArtifactWorkspaceGovernanceP9",
        "20260520071856_AddTrialOperationsP10",
        "20260521083354_AddProductionOperationsP142",
        "20260522020407_AddProductionPilotHardeningP160",
        "20260524050227_AddPilotAuthorizationWorkflowM2",
        "20260524065030_AddPilotAuthorizationHardeningM21",
        "20260617041001_AddProductionControlledPilotIntentCloudQueryFields",
        "20260622022909_AddMessageRenderPayload",
        "20260622053440_DropLegacyTrialPilotModels",
        "20260622062000_AddMessageEventsProjection",
        "20260622075032_AddSkillDefinitions",
        "20260623010000_DropPromptPolicies",
        "20260625032000_DisableMockMcpTools",
        "20260625052000_RemoveRuntimeSummaryThreshold",
        "20260722090000_AddAgentExecutionRuntimeP1",
        "20260722150000_DropSkillDefinitions",
        "20260722160000_AddDagNodeScheduling",
        LastProductionMigrationId
    ];

    public static IReadOnlySet<string> RetiredTableAllowlist { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "agent_evidence_records",
            "agent_node_reconciliation_decisions",
            "agent_node_runs",
            "agent_run_usage_ledger",
            "agent_steps",
            "agent_task_run_attempts",
            "agent_task_run_queue_items",
            "agent_tasks",
            "agent_worker_heartbeats",
            "approval_policies",
            "approval_requests",
            "artifact_file_set_operations",
            "artifact_workspaces",
            "artifacts",
            "chat_runtime_settings",
            "message_events",
            "routing_model_configurations",
            "tool_execution_records",
            "upload_records"
        };

    public static IReadOnlySet<string> PreservedTableAllowlist { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "conversation_templates",
            "language_models",
            "messages",
            "model_quota_reservations",
            "sessions",
            "tool_registrations"
        };
}
