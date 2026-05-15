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
