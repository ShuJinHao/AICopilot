using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.McpServer.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AICopilot.EntityFrameworkCore.Configuration.McpServer;

public class McpServerConfiguration : IEntityTypeConfiguration<McpServerInfo>
{
    private static readonly JsonSerializerOptions AllowedToolsJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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

        builder.Property(b => b.ExternalSystemType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("external_system_type");

        builder.Property(b => b.CapabilityKind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("capability_kind");

        builder.Property(b => b.RiskLevel)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("risk_level");

        builder.Property(b => b.TransportType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("transport_type");

        builder.Property(b => b.IsEnabled)
            .IsRequired()
            .HasColumnName("is_enabled");

        builder.Ignore(b => b.AllowedTools);

        builder.Property<List<McpAllowedTool>>("_allowedTools")
            .HasColumnName("allowed_tools")
            .HasColumnType("jsonb")
            .HasConversion(AllowedToolsConverter())
            .Metadata.SetValueComparer(AllowedToolsComparer());
    }

    private static ValueConverter<List<McpAllowedTool>, string> AllowedToolsConverter()
    {
        return new ValueConverter<List<McpAllowedTool>, string>(
            tools => JsonSerializer.Serialize(tools, AllowedToolsJsonOptions),
            payload => string.IsNullOrWhiteSpace(payload)
                ? new List<McpAllowedTool>()
                : JsonSerializer.Deserialize<List<McpAllowedTool>>(payload, AllowedToolsJsonOptions) ?? new List<McpAllowedTool>());
    }

    private static ValueComparer<List<McpAllowedTool>> AllowedToolsComparer()
    {
        return new ValueComparer<List<McpAllowedTool>>(
            (left, right) => JsonSerializer.Serialize(left, AllowedToolsJsonOptions) == JsonSerializer.Serialize(right, AllowedToolsJsonOptions),
            tools => JsonSerializer.Serialize(tools, AllowedToolsJsonOptions).GetHashCode(StringComparison.Ordinal),
            tools => JsonSerializer.Deserialize<List<McpAllowedTool>>(
                         JsonSerializer.Serialize(tools, AllowedToolsJsonOptions),
                         AllowedToolsJsonOptions)
                     ?? new List<McpAllowedTool>());
    }
}
