using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTask>
{
    public void Configure(EntityTypeBuilder<AgentTask> builder)
    {
        builder.ToTable("agent_tasks");

        builder.HasKey(task => task.Id);
        builder.Property(task => task.Id)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(task => task.TaskCode)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("task_code");

        builder.HasIndex(task => task.TaskCode).IsUnique();

        builder.Property(task => task.SessionId)
            .HasConversion(id => id.Value, value => new SessionId(value))
            .IsRequired()
            .HasColumnName("session_id");

        builder.Property(task => task.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.HasIndex(task => task.UserId)
            .HasDatabaseName("ix_agent_tasks_user_id");

        builder.Property(task => task.Title)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("title");

        builder.Property(task => task.Goal)
            .IsRequired()
            .HasMaxLength(2000)
            .HasColumnName("goal");

        builder.Property(task => task.TaskType)
            .HasConversion<string>()
            .HasMaxLength(80)
            .IsRequired()
            .HasColumnName("task_type");

        builder.Property(task => task.Status)
            .HasConversion<string>()
            .HasMaxLength(80)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(task => task.RiskLevel)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("risk_level");

        builder.Property(task => task.ModelId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new LanguageModelId(value.Value) : null)
            .HasColumnName("model_id");

        builder.Property(task => task.WorkspaceId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ArtifactWorkspaceId(value.Value) : null)
            .HasColumnName("workspace_id");

        builder.Property(task => task.ActiveRunAttemptId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentTaskRunAttemptId(value.Value) : null)
            .HasColumnName("active_run_attempt_id");

        builder.Property(task => task.RunAttemptCount)
            .IsRequired()
            .HasDefaultValue(0)
            .HasColumnName("run_attempt_count");

        builder.Property(task => task.RunLeaseId)
            .HasColumnName("run_lease_id");

        builder.Property(task => task.RunLeaseOwner)
            .HasMaxLength(120)
            .HasColumnName("run_lease_owner");

        builder.Property(task => task.RunLeaseExpiresAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("run_lease_expires_at");

        builder.Property(task => task.RunFencingToken)
            .IsRequired()
            .HasDefaultValue(0L)
            .HasColumnName("run_fencing_token");

        builder.Property(task => task.PlanJson)
            .IsRequired()
            .HasColumnName("plan_json");

        builder.Property(task => task.FinalSummary)
            .HasMaxLength(4000)
            .HasColumnName("final_summary");

        builder.Property(task => task.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(task => task.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.Property(task => task.CompletedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("completed_at");

        builder.HasMany(task => task.Steps)
            .WithOne()
            .HasForeignKey(step => step.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(task => task.Steps)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class AgentTaskRunAttemptConfiguration : IEntityTypeConfiguration<AgentTaskRunAttempt>
{
    public void Configure(EntityTypeBuilder<AgentTaskRunAttempt> builder)
    {
        builder.ToTable("agent_task_run_attempts");

        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Id)
            .HasConversion(id => id.Value, value => new AgentTaskRunAttemptId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(attempt => attempt.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .IsRequired()
            .HasColumnName("task_id");

        builder.Property(attempt => attempt.AttemptNo)
            .IsRequired()
            .HasColumnName("attempt_no");

        builder.Property(attempt => attempt.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(attempt => attempt.TriggerType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("trigger_type");

        builder.Property(attempt => attempt.LeaseId)
            .HasColumnName("lease_id");

        builder.Property(attempt => attempt.LeaseOwner)
            .HasMaxLength(120)
            .HasColumnName("lease_owner");

        builder.Property(attempt => attempt.LeaseExpiresAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("lease_expires_at");

        builder.Property(attempt => attempt.TaskFencingToken)
            .IsRequired()
            .HasDefaultValue(0L)
            .HasColumnName("task_fencing_token");

        builder.Property(attempt => attempt.IsBudgetInitialized).HasColumnName("is_budget_initialized");
        builder.Property(attempt => attempt.BudgetPolicyVersion).HasMaxLength(120).HasColumnName("budget_policy_version");
        builder.Property(attempt => attempt.BudgetMaxNodes).HasColumnName("budget_max_nodes");
        builder.Property(attempt => attempt.BudgetMaxToolCalls).HasColumnName("budget_max_tool_calls");
        builder.Property(attempt => attempt.BudgetMaxModelCalls).HasColumnName("budget_max_model_calls");
        builder.Property(attempt => attempt.BudgetMaxInputTokens).HasColumnName("budget_max_input_tokens");
        builder.Property(attempt => attempt.BudgetMaxOutputTokens).HasColumnName("budget_max_output_tokens");
        builder.Property(attempt => attempt.BudgetMaxElapsedSeconds).HasColumnName("budget_max_elapsed_seconds");
        builder.Property(attempt => attempt.BudgetMaxCostAmount).HasPrecision(18, 6).HasColumnName("budget_max_cost_amount");
        builder.Property(attempt => attempt.BudgetCostCurrency).HasMaxLength(8).HasColumnName("budget_cost_currency");
        builder.Property(attempt => attempt.BudgetMaxRetries).HasColumnName("budget_max_retries");
        builder.Property(attempt => attempt.BudgetMaxArtifactCount).HasColumnName("budget_max_artifact_count");
        builder.Property(attempt => attempt.BudgetMaxArtifactBytes).HasColumnName("budget_max_artifact_bytes");
        builder.Property(attempt => attempt.BudgetReservedToolCalls).HasColumnName("budget_reserved_tool_calls");
        builder.Property(attempt => attempt.BudgetReservedModelCalls).HasColumnName("budget_reserved_model_calls");
        builder.Property(attempt => attempt.BudgetReservedInputTokens).HasColumnName("budget_reserved_input_tokens");
        builder.Property(attempt => attempt.BudgetReservedOutputTokens).HasColumnName("budget_reserved_output_tokens");
        builder.Property(attempt => attempt.BudgetReservedElapsedMilliseconds).HasColumnName("budget_reserved_elapsed_milliseconds");
        builder.Property(attempt => attempt.BudgetReservedCostAmount).HasPrecision(18, 6).HasColumnName("budget_reserved_cost_amount");
        builder.Property(attempt => attempt.BudgetReservedRetries).HasColumnName("budget_reserved_retries");
        builder.Property(attempt => attempt.BudgetReservedArtifactCount).HasColumnName("budget_reserved_artifact_count");
        builder.Property(attempt => attempt.BudgetReservedArtifactBytes).HasColumnName("budget_reserved_artifact_bytes");
        builder.Property(attempt => attempt.BudgetConsumedToolCalls).HasColumnName("budget_consumed_tool_calls");
        builder.Property(attempt => attempt.BudgetConsumedModelCalls).HasColumnName("budget_consumed_model_calls");
        builder.Property(attempt => attempt.BudgetConsumedInputTokens).HasColumnName("budget_consumed_input_tokens");
        builder.Property(attempt => attempt.BudgetConsumedOutputTokens).HasColumnName("budget_consumed_output_tokens");
        builder.Property(attempt => attempt.BudgetConsumedElapsedMilliseconds).HasColumnName("budget_consumed_elapsed_milliseconds");
        builder.Property(attempt => attempt.BudgetConsumedCostAmount).HasPrecision(18, 6).HasColumnName("budget_consumed_cost_amount");
        builder.Property(attempt => attempt.BudgetConsumedRetries).HasColumnName("budget_consumed_retries");
        builder.Property(attempt => attempt.BudgetConsumedArtifactCount).HasColumnName("budget_consumed_artifact_count");
        builder.Property(attempt => attempt.BudgetConsumedArtifactBytes).HasColumnName("budget_consumed_artifact_bytes");

        builder.Property(attempt => attempt.StartedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("started_at");

        builder.Property(attempt => attempt.CompletedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("completed_at");

        builder.Property(attempt => attempt.FailureCode)
            .HasMaxLength(120)
            .HasColumnName("failure_code");

        builder.Property(attempt => attempt.SafeMessage)
            .HasMaxLength(2000)
            .HasColumnName("safe_message");

        builder.HasIndex(attempt => attempt.TaskId)
            .HasDatabaseName("ix_agent_task_run_attempts_task_id");

        builder.HasIndex(attempt => new { attempt.TaskId, attempt.AttemptNo })
            .IsUnique()
            .HasDatabaseName("ix_agent_task_run_attempts_task_attempt_no");
    }
}

public sealed class AgentTaskRunQueueItemConfiguration : IEntityTypeConfiguration<AgentTaskRunQueueItem>
{
    public void Configure(EntityTypeBuilder<AgentTaskRunQueueItem> builder)
    {
        builder.ToTable("agent_task_run_queue_items");

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id)
            .HasConversion(id => id.Value, value => new AgentTaskRunQueueItemId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(item => item.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .IsRequired()
            .HasColumnName("task_id");

        builder.Property(item => item.TriggerType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("trigger_type");

        builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(item => item.RequestedBy)
            .IsRequired()
            .HasColumnName("requested_by");

        builder.Property(item => item.RunAttemptId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentTaskRunAttemptId(value.Value) : null)
            .HasColumnName("run_attempt_id");

        builder.Property(item => item.LeaseId)
            .HasColumnName("lease_id");

        builder.Property(item => item.LeaseOwner)
            .HasMaxLength(120)
            .HasColumnName("lease_owner");

        builder.Property(item => item.LeaseExpiresAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("lease_expires_at");

        builder.Property(item => item.TaskFencingToken)
            .IsRequired()
            .HasDefaultValue(0L)
            .HasColumnName("task_fencing_token");

        builder.Property(item => item.AvailableAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("available_at");

        builder.Property(item => item.StartedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("started_at");

        builder.Property(item => item.CompletedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("completed_at");

        builder.Property(item => item.FailureCode)
            .HasMaxLength(120)
            .HasColumnName("failure_code");

        builder.Property(item => item.SafeMessage)
            .HasMaxLength(2000)
            .HasColumnName("safe_message");

        builder.Property(item => item.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(item => item.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.HasIndex(item => item.TaskId)
            .HasDatabaseName("ix_agent_task_run_queue_items_task_id");

        builder.HasIndex(item => new { item.Status, item.AvailableAt })
            .HasDatabaseName("ix_agent_task_run_queue_items_status_available_at");

        builder.HasIndex(item => item.LeaseExpiresAt)
            .HasDatabaseName("ix_agent_task_run_queue_items_lease_expires_at");

        builder.HasIndex(item => item.RunAttemptId)
            .HasDatabaseName("ix_agent_task_run_queue_items_run_attempt_id");

        builder.HasIndex(item => item.TaskId)
            .IsUnique()
            .HasFilter("status IN ('Queued', 'Claimed', 'Started')")
            .HasDatabaseName("ux_agent_task_run_queue_items_active_task");
    }
}

public sealed class AgentWorkerHeartbeatConfiguration : IEntityTypeConfiguration<AgentWorkerHeartbeat>
{
    public void Configure(EntityTypeBuilder<AgentWorkerHeartbeat> builder)
    {
        builder.ToTable("agent_worker_heartbeats");

        builder.HasKey(heartbeat => heartbeat.Id);
        builder.Property(heartbeat => heartbeat.Id)
            .HasConversion(id => id.Value, value => new AgentWorkerHeartbeatId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(heartbeat => heartbeat.WorkerId)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("worker_id");

        builder.Property(heartbeat => heartbeat.WorkerName)
            .IsRequired()
            .HasMaxLength(160)
            .HasColumnName("worker_name");

        builder.Property(heartbeat => heartbeat.StartedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("started_at");

        builder.Property(heartbeat => heartbeat.LastSeenAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("last_seen_at");

        builder.Property(heartbeat => heartbeat.ActiveQueueItemId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentTaskRunQueueItemId(value.Value) : null)
            .HasColumnName("active_queue_item_id");

        builder.Property(heartbeat => heartbeat.ActiveTaskId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentTaskId(value.Value) : null)
            .HasColumnName("active_task_id");

        builder.Property(heartbeat => heartbeat.WorkspaceRootHash)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("workspace_root_hash");

        builder.Property(heartbeat => heartbeat.Version)
            .IsRequired()
            .HasMaxLength(80)
            .HasColumnName("version");

        builder.HasIndex(heartbeat => heartbeat.WorkerId)
            .IsUnique()
            .HasDatabaseName("ux_agent_worker_heartbeats_worker_id");

        builder.HasIndex(heartbeat => heartbeat.LastSeenAt)
            .HasDatabaseName("ix_agent_worker_heartbeats_last_seen_at");

        builder.HasIndex(heartbeat => heartbeat.ActiveTaskId)
            .HasDatabaseName("ix_agent_worker_heartbeats_active_task_id");
    }
}

public sealed class AgentStepConfiguration : IEntityTypeConfiguration<AgentStep>
{
    public void Configure(EntityTypeBuilder<AgentStep> builder)
    {
        builder.ToTable("agent_steps");

        builder.HasKey(step => step.Id);
        builder.Property(step => step.Id)
            .HasConversion(id => id.Value, value => new AgentStepId(value))
            .HasColumnName("id");

        builder.Property(step => step.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .IsRequired()
            .HasColumnName("task_id");

        builder.Property(step => step.StepIndex)
            .IsRequired()
            .HasColumnName("step_index");

        builder.Property(step => step.Title)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("title");

        builder.Property(step => step.Description)
            .HasMaxLength(1000)
            .HasColumnName("description");

        builder.Property(step => step.StepType)
            .HasConversion<string>()
            .HasMaxLength(80)
            .IsRequired()
            .HasColumnName("step_type");

        builder.Property(step => step.Status)
            .HasConversion<string>()
            .HasMaxLength(80)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(step => step.ToolCode)
            .HasMaxLength(100)
            .HasColumnName("tool_code");

        builder.Property(step => step.RequiresApproval)
            .IsRequired()
            .HasColumnName("requires_approval");

        builder.Property(step => step.InputJson)
            .HasColumnName("input_json");

        builder.Property(step => step.OutputJson)
            .HasColumnName("output_json");

        builder.Property(step => step.ErrorMessage)
            .HasMaxLength(2000)
            .HasColumnName("error_message");

        builder.Property(step => step.StartedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("started_at");

        builder.Property(step => step.FinishedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("finished_at");

        builder.HasIndex(step => new { step.TaskId, step.StepIndex })
            .IsUnique();
    }
}
