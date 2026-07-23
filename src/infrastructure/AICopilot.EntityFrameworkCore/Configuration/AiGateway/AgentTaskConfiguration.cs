using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static AICopilot.EntityFrameworkCore.Configuration.AiGateway.AgentExecutionRuntimeConfigurationMapping;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTask>
{
    public void Configure(EntityTypeBuilder<AgentTask> builder)
    {
        ConfigureEntity(builder, "agent_tasks", task => task.Id, id => id.Value, value => new AgentTaskId(value));

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

        MapLeaseColumns(builder, (nameof(AgentTask.RunLeaseId), "run_lease_id"), (nameof(AgentTask.RunLeaseOwner), "run_lease_owner"), (nameof(AgentTask.RunLeaseExpiresAt), "run_lease_expires_at"), (nameof(AgentTask.RunFencingToken), "run_fencing_token"));

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
        ConfigureEntity(builder, "agent_task_run_attempts", attempt => attempt.Id, id => id.Value, value => new AgentTaskRunAttemptId(value));

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

        MapLeaseColumns(builder, (nameof(AgentTaskRunAttempt.LeaseId), "lease_id"), (nameof(AgentTaskRunAttempt.LeaseOwner), "lease_owner"), (nameof(AgentTaskRunAttempt.LeaseExpiresAt), "lease_expires_at"), (nameof(AgentTaskRunAttempt.TaskFencingToken), "task_fencing_token"));

        MapColumns(builder,
            (nameof(AgentTaskRunAttempt.IsBudgetInitialized), "is_budget_initialized"), (nameof(AgentTaskRunAttempt.BudgetMaxNodes), "budget_max_nodes"), (nameof(AgentTaskRunAttempt.BudgetMaxToolCalls), "budget_max_tool_calls"), (nameof(AgentTaskRunAttempt.BudgetMaxModelCalls), "budget_max_model_calls"),
            (nameof(AgentTaskRunAttempt.BudgetMaxInputTokens), "budget_max_input_tokens"), (nameof(AgentTaskRunAttempt.BudgetMaxOutputTokens), "budget_max_output_tokens"), (nameof(AgentTaskRunAttempt.BudgetMaxElapsedSeconds), "budget_max_elapsed_seconds"), (nameof(AgentTaskRunAttempt.BudgetMaxRetries), "budget_max_retries"), (nameof(AgentTaskRunAttempt.BudgetMaxArtifactCount), "budget_max_artifact_count"), (nameof(AgentTaskRunAttempt.BudgetMaxArtifactBytes), "budget_max_artifact_bytes"),
            (nameof(AgentTaskRunAttempt.BudgetReservedToolCalls), "budget_reserved_tool_calls"), (nameof(AgentTaskRunAttempt.BudgetReservedModelCalls), "budget_reserved_model_calls"), (nameof(AgentTaskRunAttempt.BudgetReservedInputTokens), "budget_reserved_input_tokens"), (nameof(AgentTaskRunAttempt.BudgetReservedOutputTokens), "budget_reserved_output_tokens"), (nameof(AgentTaskRunAttempt.BudgetReservedElapsedMilliseconds), "budget_reserved_elapsed_milliseconds"), (nameof(AgentTaskRunAttempt.BudgetReservedRetries), "budget_reserved_retries"),
            (nameof(AgentTaskRunAttempt.BudgetReservedArtifactCount), "budget_reserved_artifact_count"), (nameof(AgentTaskRunAttempt.BudgetReservedArtifactBytes), "budget_reserved_artifact_bytes"), (nameof(AgentTaskRunAttempt.BudgetConsumedToolCalls), "budget_consumed_tool_calls"), (nameof(AgentTaskRunAttempt.BudgetConsumedModelCalls), "budget_consumed_model_calls"), (nameof(AgentTaskRunAttempt.BudgetConsumedInputTokens), "budget_consumed_input_tokens"), (nameof(AgentTaskRunAttempt.BudgetConsumedOutputTokens), "budget_consumed_output_tokens"),
            (nameof(AgentTaskRunAttempt.BudgetConsumedElapsedMilliseconds), "budget_consumed_elapsed_milliseconds"), (nameof(AgentTaskRunAttempt.BudgetConsumedRetries), "budget_consumed_retries"),
            (nameof(AgentTaskRunAttempt.BudgetConsumedArtifactCount), "budget_consumed_artifact_count"), (nameof(AgentTaskRunAttempt.BudgetConsumedArtifactBytes), "budget_consumed_artifact_bytes"));
        MapTextColumns(builder,
            (nameof(AgentTaskRunAttempt.BudgetPolicyVersion), "budget_policy_version", 120), (nameof(AgentTaskRunAttempt.BudgetCostCurrency), "budget_cost_currency", 8));
        MapDecimalColumns(builder,
            (nameof(AgentTaskRunAttempt.BudgetMaxCostAmount), "budget_max_cost_amount"), (nameof(AgentTaskRunAttempt.BudgetReservedCostAmount), "budget_reserved_cost_amount"),
            (nameof(AgentTaskRunAttempt.BudgetConsumedCostAmount), "budget_consumed_cost_amount"));

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

        MapLeaseColumns(builder, (nameof(AgentTaskRunQueueItem.LeaseId), "lease_id"), (nameof(AgentTaskRunQueueItem.LeaseOwner), "lease_owner"), (nameof(AgentTaskRunQueueItem.LeaseExpiresAt), "lease_expires_at"), (nameof(AgentTaskRunQueueItem.TaskFencingToken), "task_fencing_token"));

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

        MapUniqueFilteredIndex(builder, item => item.TaskId, "status IN ('Queued', 'Claimed', 'Started')", "ux_agent_task_run_queue_items_active_task");
    }
}

public sealed class AgentWorkerHeartbeatConfiguration : IEntityTypeConfiguration<AgentWorkerHeartbeat>
{
    public void Configure(EntityTypeBuilder<AgentWorkerHeartbeat> builder)
    {
        ConfigureEntity(builder, "agent_worker_heartbeats", heartbeat => heartbeat.Id, id => id.Value, value => new AgentWorkerHeartbeatId(value));

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
        ConfigureEntity(builder, "agent_steps", step => step.Id, id => id.Value, value => new AgentStepId(value), usesRowVersion: false);

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
