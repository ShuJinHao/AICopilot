using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class ArtifactWorkspaceConfiguration : IEntityTypeConfiguration<ArtifactWorkspace>
{
    public void Configure(EntityTypeBuilder<ArtifactWorkspace> builder)
    {
        builder.ToTable("artifact_workspaces");

        builder.HasKey(workspace => workspace.Id);
        builder.Property(workspace => workspace.Id)
            .HasConversion(id => id.Value, value => new ArtifactWorkspaceId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(workspace => workspace.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .IsRequired()
            .HasColumnName("task_id");

        builder.HasIndex(workspace => workspace.TaskId)
            .IsUnique();

        builder.Property(workspace => workspace.WorkspaceCode)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("workspace_code");

        builder.HasIndex(workspace => workspace.WorkspaceCode)
            .IsUnique();

        builder.Property(workspace => workspace.RootPath)
            .IsRequired()
            .HasMaxLength(1000)
            .HasColumnName("root_path");

        builder.Property(workspace => workspace.WorkspaceUrl)
            .IsRequired()
            .HasMaxLength(1000)
            .HasColumnName("workspace_url");

        builder.Property(workspace => workspace.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(workspace => workspace.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(workspace => workspace.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.HasMany(workspace => workspace.Artifacts)
            .WithOne()
            .HasForeignKey(artifact => artifact.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(workspace => workspace.Artifacts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ArtifactConfiguration : IEntityTypeConfiguration<Artifact>
{
    public void Configure(EntityTypeBuilder<Artifact> builder)
    {
        builder.ToTable("artifacts");

        builder.HasKey(artifact => artifact.Id);
        builder.Property(artifact => artifact.Id)
            .HasConversion(id => id.Value, value => new ArtifactId(value))
            .HasColumnName("id");

        builder.Property(artifact => artifact.WorkspaceId)
            .HasConversion(id => id.Value, value => new ArtifactWorkspaceId(value))
            .IsRequired()
            .HasColumnName("workspace_id");

        builder.Property(artifact => artifact.TaskId)
            .HasConversion(id => id.Value, value => new AgentTaskId(value))
            .IsRequired()
            .HasColumnName("task_id");

        builder.Property(artifact => artifact.ArtifactType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("artifact_type");

        builder.Property(artifact => artifact.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("name");

        builder.Property(artifact => artifact.RelativePath)
            .IsRequired()
            .HasMaxLength(1000)
            .HasColumnName("relative_path");

        builder.Property(artifact => artifact.FileSize)
            .IsRequired()
            .HasColumnName("file_size");

        builder.Property(artifact => artifact.MimeType)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("mime_type");

        builder.Property(artifact => artifact.Version)
            .IsRequired()
            .HasColumnName("version");

        builder.Property(artifact => artifact.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(artifact => artifact.SourceMode)
            .HasMaxLength(80)
            .HasColumnName("source_mode");

        builder.Property(artifact => artifact.Boundary)
            .HasMaxLength(120)
            .HasColumnName("boundary");

        builder.Property(artifact => artifact.IsSimulation)
            .IsRequired()
            .HasColumnName("is_simulation");

        builder.Property(artifact => artifact.IsSandbox)
            .IsRequired()
            .HasColumnName("is_sandbox");

        builder.Property(artifact => artifact.SourceLabel)
            .HasMaxLength(200)
            .HasColumnName("source_label");

        builder.Property(artifact => artifact.QueryHash)
            .HasMaxLength(128)
            .HasColumnName("query_hash");

        builder.Property(artifact => artifact.ResultHash)
            .HasMaxLength(128)
            .HasColumnName("result_hash");

        builder.Property(artifact => artifact.EvidenceSetDigest)
            .HasMaxLength(64)
            .HasColumnName("evidence_set_digest");

        builder.Property(artifact => artifact.RowCount)
            .IsRequired()
            .HasColumnName("row_count");

        builder.Property(artifact => artifact.IsTruncated)
            .IsRequired()
            .HasColumnName("is_truncated");

        builder.Property(artifact => artifact.CreatedByStepId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentStepId(value.Value) : null)
            .HasColumnName("created_by_step_id");

        builder.Property(artifact => artifact.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.Property(artifact => artifact.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("updated_at");

        builder.Property(artifact => artifact.FinalizedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("finalized_at");

        builder.HasIndex(artifact => new { artifact.WorkspaceId, artifact.RelativePath });
    }
}
