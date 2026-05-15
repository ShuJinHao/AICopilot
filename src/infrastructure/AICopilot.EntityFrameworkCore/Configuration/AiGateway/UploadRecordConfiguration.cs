using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AICopilot.EntityFrameworkCore.Configuration.AiGateway;

public sealed class UploadRecordConfiguration : IEntityTypeConfiguration<UploadRecord>
{
    public void Configure(EntityTypeBuilder<UploadRecord> builder)
    {
        builder.ToTable("upload_records");

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id)
            .HasConversion(id => id.Value, value => new UploadRecordId(value))
            .HasColumnName("id");

        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.Property(record => record.Scope)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("scope");

        builder.Property(record => record.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(record => record.SessionId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new SessionId(value.Value) : null)
            .HasColumnName("session_id");

        builder.Property(record => record.AgentTaskId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new AgentTaskId(value.Value) : null)
            .HasColumnName("agent_task_id");

        builder.Property(record => record.KnowledgeBaseId)
            .HasColumnName("knowledge_base_id");

        builder.Property(record => record.RagDocumentId)
            .HasColumnName("rag_document_id");

        builder.Property(record => record.FileName)
            .IsRequired()
            .HasMaxLength(255)
            .HasColumnName("file_name");

        builder.Property(record => record.ContentType)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("content_type");

        builder.Property(record => record.FileSize)
            .IsRequired()
            .HasColumnName("file_size");

        builder.Property(record => record.Sha256)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("sha256");

        builder.Property(record => record.StoragePath)
            .HasMaxLength(1000)
            .HasColumnName("storage_path");

        builder.Property(record => record.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired()
            .HasColumnName("status");

        builder.Property(record => record.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasColumnName("created_at");

        builder.HasIndex(record => new { record.UserId, record.SessionId })
            .HasDatabaseName("ix_upload_records_user_session");

        builder.HasIndex(record => new { record.UserId, record.AgentTaskId })
            .HasDatabaseName("ix_upload_records_user_agent_task");

        builder.HasIndex(record => record.KnowledgeBaseId)
            .HasDatabaseName("ix_upload_records_knowledge_base_id");
    }
}
