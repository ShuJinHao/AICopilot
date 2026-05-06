using AICopilot.Core.McpServer.Ids;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.McpServer.Aggregates.McpServerInfo;

/// <summary>
/// 定义 MCP 服务的配置信息，作为聚合根存在。
/// </summary>
public class McpServerInfo : IAggregateRoot<McpServerId>
{
    private readonly List<string> _allowedToolNames = [];

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
        IEnumerable<string>? allowedToolNames = null,
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
            allowedToolNames,
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

    public IReadOnlyCollection<string> AllowedToolNames => _allowedToolNames.AsReadOnly();

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
        IEnumerable<string>? allowedToolNames,
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

        _allowedToolNames.Clear();
        _allowedToolNames.AddRange(NormalizeAllowedToolNames(allowedToolNames));
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

    private static IEnumerable<string> NormalizeAllowedToolNames(IEnumerable<string>? allowedToolNames)
    {
        return (allowedToolNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
