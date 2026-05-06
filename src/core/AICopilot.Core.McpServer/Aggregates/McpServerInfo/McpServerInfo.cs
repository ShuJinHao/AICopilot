using AICopilot.Core.McpServer.Ids;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.McpServer.Aggregates.McpServerInfo;

/// <summary>
/// 定义 MCP 服务的配置信息，作为聚合根存在。
/// </summary>
public class McpServerInfo : IAggregateRoot<McpServerId>
{
    private List<McpAllowedTool> _allowedTools = [];

    protected McpServerInfo()
    {
    }

    public McpServerInfo(
        string name,
        string description,
        McpTransportType transportType,
        string? command,
        string arguments,
        ChatExposureMode chatExposureMode = ChatExposureMode.Disabled,
        IEnumerable<McpAllowedTool>? allowedTools = null,
        bool isEnabled = true,
        AiToolExternalSystemType externalSystemType = AiToolExternalSystemType.Unknown,
        AiToolCapabilityKind capabilityKind = AiToolCapabilityKind.Diagnostics,
        AiToolRiskLevel riskLevel = AiToolRiskLevel.RequiresApproval)
    {
        Id = McpServerId.New();
        Update(
            name,
            description,
            transportType,
            command,
            arguments,
            chatExposureMode,
            allowedTools,
            isEnabled,
            externalSystemType,
            capabilityKind,
            riskLevel);
    }

    public McpServerId Id { get; private set; }

    public uint RowVersion { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public McpTransportType TransportType { get; private set; }

    public string? Command { get; private set; }

    public string Arguments { get; private set; } = string.Empty;

    public ChatExposureMode ChatExposureMode { get; private set; } = ChatExposureMode.Disabled;

    public IReadOnlyCollection<McpAllowedTool> AllowedTools => _allowedTools.AsReadOnly();

    public AiToolExternalSystemType ExternalSystemType { get; private set; } = AiToolExternalSystemType.Unknown;

    public AiToolCapabilityKind CapabilityKind { get; private set; } = AiToolCapabilityKind.Diagnostics;

    public AiToolRiskLevel RiskLevel { get; private set; } = AiToolRiskLevel.RequiresApproval;

    public bool IsEnabled { get; private set; }

    public void Update(
        string name,
        string description,
        McpTransportType transportType,
        string? command,
        string arguments,
        ChatExposureMode chatExposureMode,
        IEnumerable<McpAllowedTool>? allowedTools,
        bool isEnabled,
        AiToolExternalSystemType externalSystemType = AiToolExternalSystemType.Unknown,
        AiToolCapabilityKind capabilityKind = AiToolCapabilityKind.Diagnostics,
        AiToolRiskLevel riskLevel = AiToolRiskLevel.RequiresApproval)
    {
        Validate(name, description, transportType, command, arguments, chatExposureMode, externalSystemType, capabilityKind, riskLevel);

        Name = name.Trim();
        Description = description.Trim();
        TransportType = transportType;
        Command = string.IsNullOrWhiteSpace(command) ? null : command.Trim();
        Arguments = (arguments ?? string.Empty).Trim();
        ChatExposureMode = chatExposureMode;
        ExternalSystemType = externalSystemType;
        CapabilityKind = capabilityKind;
        RiskLevel = riskLevel;
        IsEnabled = isEnabled;

        _allowedTools.Clear();
        _allowedTools.AddRange(NormalizeAllowedTools(allowedTools, externalSystemType, capabilityKind, riskLevel));
    }

    private static void Validate(
        string name,
        string description,
        McpTransportType transportType,
        string? command,
        string arguments,
        ChatExposureMode chatExposureMode,
        AiToolExternalSystemType externalSystemType,
        AiToolCapabilityKind capabilityKind,
        AiToolRiskLevel riskLevel)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("MCP server name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("MCP server description is required.", nameof(description));
        }

        if (!Enum.IsDefined(typeof(McpTransportType), transportType))
        {
            throw new ArgumentOutOfRangeException(nameof(transportType), transportType, "MCP server transport type is invalid.");
        }

        if (!Enum.IsDefined(typeof(ChatExposureMode), chatExposureMode))
        {
            throw new ArgumentOutOfRangeException(nameof(chatExposureMode), chatExposureMode, "MCP server chat exposure mode is invalid.");
        }

        if (!Enum.IsDefined(typeof(AiToolExternalSystemType), externalSystemType))
        {
            throw new ArgumentOutOfRangeException(nameof(externalSystemType), externalSystemType, "MCP server external system type is invalid.");
        }

        if (!Enum.IsDefined(typeof(AiToolCapabilityKind), capabilityKind))
        {
            throw new ArgumentOutOfRangeException(nameof(capabilityKind), capabilityKind, "MCP server capability kind is invalid.");
        }

        if (!Enum.IsDefined(typeof(AiToolRiskLevel), riskLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(riskLevel), riskLevel, "MCP server risk level is invalid.");
        }

        if (transportType == McpTransportType.Stdio && string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("MCP stdio server command is required.", nameof(command));
        }

        if (transportType == McpTransportType.Sse && string.IsNullOrWhiteSpace(arguments))
        {
            throw new ArgumentException("MCP server arguments are required.", nameof(arguments));
        }

        if (transportType == McpTransportType.Sse
            && (!Uri.TryCreate(arguments.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException("MCP SSE server arguments must be an absolute HTTP or HTTPS URL.", nameof(arguments));
        }
    }

    private static IEnumerable<McpAllowedTool> NormalizeAllowedTools(
        IEnumerable<McpAllowedTool>? allowedTools,
        AiToolExternalSystemType serverExternalSystemType,
        AiToolCapabilityKind serverCapabilityKind,
        AiToolRiskLevel serverRiskLevel)
    {
        return (allowedTools ?? [])
            .Where(tool => !string.IsNullOrWhiteSpace(tool.ToolName))
            .Select(tool => tool.Normalize(serverExternalSystemType, serverCapabilityKind, serverRiskLevel))
            .GroupBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }
}

public sealed record McpAllowedTool(
    string ToolName,
    AiToolExternalSystemType? ExternalSystemType = null,
    AiToolCapabilityKind? CapabilityKind = null,
    AiToolRiskLevel? RiskLevel = null,
    bool ReadOnlyDeclared = false)
{
    public McpAllowedTool Normalize(
        AiToolExternalSystemType serverExternalSystemType,
        AiToolCapabilityKind serverCapabilityKind,
        AiToolRiskLevel serverRiskLevel)
    {
        if (!Enum.IsDefined(typeof(AiToolExternalSystemType), ExternalSystemType ?? serverExternalSystemType))
        {
            throw new ArgumentOutOfRangeException(nameof(ExternalSystemType), ExternalSystemType, "MCP tool external system type is invalid.");
        }

        if (!Enum.IsDefined(typeof(AiToolCapabilityKind), CapabilityKind ?? serverCapabilityKind))
        {
            throw new ArgumentOutOfRangeException(nameof(CapabilityKind), CapabilityKind, "MCP tool capability kind is invalid.");
        }

        if (!Enum.IsDefined(typeof(AiToolRiskLevel), RiskLevel ?? serverRiskLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(RiskLevel), RiskLevel, "MCP tool risk level is invalid.");
        }

        return this with { ToolName = ToolName.Trim() };
    }

    public AiToolExternalSystemType EffectiveExternalSystemType(AiToolExternalSystemType serverDefault)
    {
        return ExternalSystemType ?? serverDefault;
    }

    public AiToolCapabilityKind EffectiveCapabilityKind(AiToolCapabilityKind serverDefault)
    {
        return CapabilityKind ?? serverDefault;
    }

    public AiToolRiskLevel EffectiveRiskLevel(AiToolRiskLevel serverDefault)
    {
        return RiskLevel ?? serverDefault;
    }
}
