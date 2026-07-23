namespace AICopilot.SharedKernel.Result;

public static partial class AppProblemCodes
{
    public const string PlannerToolSchemaUnsupported = "planner_tool_schema_unsupported";
    public const string AgentPlanInvalid = "agent_plan_invalid";
    public const string PlanPayloadTooLarge = "plan_payload_too_large";

    public static bool IsAgentPlanIntegrityCode(string? code) =>
        code is AgentPlanInvalid or AgentPlanSchemaInvalid or PlanPayloadTooLarge;

    public const string EvidencePayloadTooLarge = "evidence_payload_too_large";
    public const string AgentPlanToolDenied = "agent_plan_tool_denied";
    public const string AgentPlanSchemaInvalid = "agent_plan_schema_invalid";
    public const string ToolExecutionNotFound = "tool_execution_not_found";
    public const string ArtifactFinalized = "artifact_finalized";
    public const string ArtifactGenerationFailed = "artifact_generation_failed";
    public const string WorkspaceManifestInvalid = "workspace_manifest_invalid";
    public const string AgentTaskRunInProgress = "agent_task_run_in_progress";
    public const string AgentTaskRetryNotAllowed = "agent_task_retry_not_allowed";
    public const string AgentTaskRunLeaseExpired = "agent_task_run_lease_expired";
    public const string AgentTaskCancellationRequested = "agent_task_cancellation_requested";
    public const string AgentTaskRunQueued = "agent_task_run_queued";
    public const string AgentTaskRunQueueNotFound = "agent_task_run_queue_not_found";
    public const string AgentTaskRunQueueLeaseExpired = "agent_task_run_queue_lease_expired";
    public const string AgentTaskRunFenceStale = "agent_task_run_fence_stale";
    public const string AgentNodeRunFenceStale = "agent_node_run_fence_stale";
    public const string AgentNodeRunStateConflict = "agent_node_run_state_conflict";
    public const string AgentRunBudgetExceeded = "agent_run_budget_exceeded";
    public const string AgentWorkerUnavailable = "agent_worker_unavailable";
    public const string AgentWorkerWorkspaceMismatch = "agent_worker_workspace_mismatch";
    public const string AgentFinalizationStateConflict = "agent_finalization_state_conflict";
    public const string AgentRunQueueDeadLetterNotAllowed = "agent_run_queue_dead_letter_not_allowed";
    public const string AgentRunQueueOperationDenied = "agent_run_queue_operation_denied";
}
