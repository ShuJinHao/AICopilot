using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.McpServer.Ids;

namespace AICopilot.EntityFrameworkCore.Configuration.McpServer;

public class McpServerConfiguration : IEntityTypeConfiguration<McpServerInfo>
{
    public void Configure(EntityTypeBuilder<McpServerInfo> builder)
    {
        builder.ToTable("mcp_server_info", "mcp");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(id => id.Value, value => new McpServerId(value))
            .HasColumnName("id");

        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.Property(b => b.Name)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("name");

        // 保证名称唯一
        builder.HasIndex(b => b.Name).IsUnique();

        builder.Property(b => b.Description)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnName("description");

        builder.Property(b => b.Command)
            .HasMaxLength(200)
            .HasColumnName("command");

        builder.Property(b => b.Arguments)
            .IsRequired()
            .HasMaxLength(1000)
            .HasColumnName("arguments");

        builder.Property(b => b.ChatExposureMode)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("chat_exposure_mode");

        builder.Property(b => b.TransportType)
            .IsRequired()
            .HasConversion<string>() // 存储枚举字符串，增强可读性
            .HasMaxLength(50)
            .HasColumnName("transport_type");

        builder.Property(b => b.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");

        builder.PrimitiveCollection(b => b.AllowedToolNames)
            .IsRequired()
            .HasColumnName("allowed_tool_names");
    }
}
