using AICopilot.AgentPlugin;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.McpServer.Ids;
using AICopilot.Core.McpServer.Specifications.McpServerInfo;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.Runtime.CompilerServices;
using System.Text;

namespace AICopilot.Infrastructure.Mcp;

public class McpServerBootstrap(
    IReadRepository<McpServerInfo> mcpServerRepository,
    IApprovalRequirementReadService approvalRequirementReadService,
    IAgentPluginRegistry agentPluginRegistry,
    ILogger<McpServerBootstrap> logger)
    : IMcpServerBootstrap, IMcpRuntimeRegistrationProvider
{
    private static readonly TimeSpan SseConnectionTimeout = TimeSpan.FromSeconds(15);

    public async IAsyncEnumerable<McpClient> StartAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var candidateServers = await ListCandidateServersAsync(ct);
        foreach (var candidateServer in candidateServers)
        {
            var registration = await CreateRegistrationAsync(candidateServer, ct);
            if (registration is null)
            {
                continue;
            }

            agentPluginRegistry.RegisterAgentPlugin(registration.Plugin);
            if (registration.ClientHandle.Client is McpClient mcpClient)
            {
                yield return mcpClient;
            }
            else
            {
                await registration.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<McpRuntimeServerState>> ListCandidateServersAsync(
        CancellationToken cancellationToken)
    {
        var mcpServerInfos = await mcpServerRepository.ListAsync(
            new McpServerInfosOrderedSpec(),
            cancellationToken);

        return mcpServerInfos
            .Where(IsRuntimeCandidate)
            .Select(server => new McpRuntimeServerState(server.Id.Value, server.Name, server.RowVersion))
            .ToArray();
    }

    public async Task<McpRuntimeRegistration?> CreateRegistrationAsync(
        McpRuntimeServerState server,
        CancellationToken cancellationToken)
    {
        var mcpServerInfo = await mcpServerRepository.GetByIdAsync(
            new McpServerId(server.ServerId),
            cancellationToken);
        if (mcpServerInfo is null || !IsRuntimeCandidate(mcpServerInfo))
        {
            return null;
        }

        var mcpClient = await CreateClientAsync(mcpServerInfo, cancellationToken);
        var clientHandle = new McpRuntimeClientHandle(mcpClient);
        try
        {
            logger.LogInformation("Connected to MCP server {Name}.", mcpServerInfo.Name);

            var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
            var allowlist = mcpServerInfo.AllowedTools.ToDictionary(
                tool => tool.ToolName,
                StringComparer.OrdinalIgnoreCase);
            var exposedTools = tools
                .Where(tool => allowlist.ContainsKey(tool.Name))
                .Select(tool => (Tool: tool, Exposure: allowlist[tool.Name]))
                .Where(candidate => CanExposeTool(mcpServerInfo, candidate.Exposure, candidate.Tool))
                .ToArray();

            logger.LogInformation(
                "MCP server {Name} discovered {TotalCount} tools and exposed {ExposedCount} after allowlist filtering.",
                mcpServerInfo.Name,
                tools.Count,
                exposedTools.Length);

            if (exposedTools.Length == 0)
            {
                logger.LogWarning(
                    "MCP server {Name} did not match any allowed tool names and will not be registered.",
                    mcpServerInfo.Name);
                await clientHandle.DisposeAsync();
                return null;
            }

            var protectedNames = await LoadProtectedToolNamesAsync(mcpServerInfo.Name, cancellationToken);
            var plugin = BuildMcpPlugin(mcpServerInfo, exposedTools, protectedNames, clientHandle);

            return new McpRuntimeRegistration(
                mcpServerInfo.Id.Value,
                mcpServerInfo.Name,
                mcpServerInfo.RowVersion,
                plugin,
                clientHandle);
        }
        catch
        {
            await clientHandle.DisposeAsync();
            throw;
        }
    }

    private static bool IsRuntimeCandidate(McpServerInfo server)
    {
        return server.IsEnabled
               && server.ChatExposureMode.CanExposeInChat()
               && server.AllowedTools.Count > 0;
    }

    private async Task<McpClient> CreateClientAsync(
        McpServerInfo mcpServerInfo,
        CancellationToken cancellationToken)
    {
        return mcpServerInfo.TransportType switch
        {
            McpTransportType.Stdio => await CreateStdioClientAsync(mcpServerInfo, cancellationToken),
            McpTransportType.Sse => await CreateSseClientAsync(mcpServerInfo, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported MCP transport type: {mcpServerInfo.TransportType}")
        };
    }

    private async Task<HashSet<string>> LoadProtectedToolNamesAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        var approvalPolicies = await LoadApprovalPoliciesAsync([serverName], cancellationToken);
        return approvalPolicies.GetValueOrDefault(serverName)
               ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, HashSet<string>>> LoadApprovalPoliciesAsync(
        string[] serverNames,
        CancellationToken cancellationToken)
    {
        if (serverNames.Length == 0)
        {
            return [];
        }

        var requirements = await approvalRequirementReadService.GetToolRequirementsAsync(
            AiToolTargetType.McpServer,
            serverNames,
            cancellationToken);

        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in requirements.Where(requirement => requirement.RequiresApproval))
        {
            if (!result.TryGetValue(requirement.TargetName, out var names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[requirement.TargetName] = names;
            }

            names.Add(requirement.ToolName);
        }

        return result;
    }

    private GenericBridgePlugin BuildMcpPlugin(
        McpServerInfo mcpServerInfo,
        IList<(McpClientTool Tool, McpAllowedTool Exposure)> mcpTools,
        HashSet<string> protectedNames,
        McpRuntimeClientHandle clientHandle)
    {
        var tools = mcpTools
            .Select(candidate => ToToolDefinition(
                mcpServerInfo.Name,
                candidate.Exposure.EffectiveExternalSystemType(mcpServerInfo.ExternalSystemType),
                candidate.Exposure.EffectiveCapabilityKind(mcpServerInfo.CapabilityKind),
                candidate.Exposure.EffectiveRiskLevel(mcpServerInfo.RiskLevel),
                candidate.Exposure.ReadOnlyDeclared,
                candidate.Exposure.McpReadOnlyHint ?? ReadMcpAnnotationHint(candidate.Tool, "ReadOnlyHint", "readOnlyHint"),
                candidate.Exposure.McpDestructiveHint ?? ReadMcpAnnotationHint(candidate.Tool, "DestructiveHint", "destructiveHint"),
                candidate.Exposure.McpIdempotentHint ?? ReadMcpAnnotationHint(candidate.Tool, "IdempotentHint", "idempotentHint"),
                candidate.Tool,
                protectedNames,
                clientHandle))
            .ToArray();

        return new GenericBridgePlugin
        {
            Name = mcpServerInfo.Name,
            Description = mcpServerInfo.Description,
            Tools = tools,
            ChatExposureMode = mcpServerInfo.ChatExposureMode
        };
    }

    private static AiToolDefinition ToToolDefinition(
        string serverName,
        AiToolExternalSystemType externalSystemType,
        AiToolCapabilityKind capabilityKind,
        AiToolRiskLevel riskLevel,
        bool readOnlyDeclared,
        bool? mcpReadOnlyHint,
        bool? mcpDestructiveHint,
        bool? mcpIdempotentHint,
        McpClientTool tool,
        HashSet<string> protectedNames,
        McpRuntimeClientHandle clientHandle)
    {
        var requiresApproval = protectedNames.Contains(tool.Name)
                               || riskLevel == AiToolRiskLevel.RequiresApproval;

        return new AiToolDefinition
        {
            Name = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, serverName, tool.Name),
            ToolName = tool.Name,
            Description = tool.Description,
            Kind = AiToolCallKind.Mcp,
            TargetType = AiToolTargetType.McpServer,
            TargetName = serverName,
            ServerName = serverName,
            RequiresApproval = requiresApproval,
            ExternalSystemType = externalSystemType,
            CapabilityKind = capabilityKind,
            RiskLevel = riskLevel,
            ReadOnlyDeclared = readOnlyDeclared,
            McpReadOnlyHint = mcpReadOnlyHint,
            McpDestructiveHint = mcpDestructiveHint,
            McpIdempotentHint = mcpIdempotentHint,
            JsonSchema = tool.JsonSchema.Clone(),
            ReturnJsonSchema = tool.ReturnJsonSchema?.Clone(),
            InvokeAsync = async (context, cancellationToken) =>
            {
                using var invocation = clientHandle.AcquireInvocation();
                var argumentValues = context.Arguments.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase);
                var arguments = new AIFunctionArguments(argumentValues);

                return await tool.InvokeAsync(arguments, cancellationToken);
            }
        };
    }

    private bool CanExposeTool(McpServerInfo server, McpAllowedTool exposure, McpClientTool tool)
    {
        var externalSystemType = exposure.EffectiveExternalSystemType(server.ExternalSystemType);
        var capabilityKind = exposure.EffectiveCapabilityKind(server.CapabilityKind);
        var riskLevel = exposure.EffectiveRiskLevel(server.RiskLevel);
        var descriptor = AiToolSafetyDescriptor.Create(
            exposure.ReadOnlyDeclared,
            exposure.McpReadOnlyHint ?? ReadMcpAnnotationHint(tool, "ReadOnlyHint", "readOnlyHint"),
            exposure.McpDestructiveHint ?? ReadMcpAnnotationHint(tool, "DestructiveHint", "destructiveHint"),
            exposure.McpIdempotentHint ?? ReadMcpAnnotationHint(tool, "IdempotentHint", "idempotentHint"),
            capabilityKind,
            externalSystemType,
            riskLevel);
        var decision = AiToolSafetyPolicy.Evaluate(
            descriptor,
            tool.Name,
            tool.Description,
            tool.JsonSchema,
            tool.ReturnJsonSchema);

        if (decision.IsAllowed)
        {
            logger.LogInformation(
                "MCP server {ServerName} tool {ToolName} passed safety policy. RuntimeName={RuntimeName}; ReadOnlyDeclared={ReadOnlyDeclared}; McpReadOnlyHint={McpReadOnlyHint}; McpDestructiveHint={McpDestructiveHint}",
                server.Name,
                tool.Name,
                AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, server.Name, tool.Name),
                descriptor.ReadOnlyDeclared,
                descriptor.McpReadOnlyHint,
                descriptor.McpDestructiveHint);
            return true;
        }

        logger.LogWarning(
            "MCP server {ServerName} tool {ToolName} was blocked by safety policy. RuntimeName={RuntimeName}; Reasons={Reasons}",
            server.Name,
            tool.Name,
            AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, server.Name, tool.Name),
            string.Join("; ", decision.BlockReasons ?? [decision.Reason ?? "Unknown"]));
        return false;
    }

    private static bool? ReadMcpAnnotationHint(McpClientTool tool, params string[] propertyNames)
    {
        var annotations = tool.GetType().GetProperty("Annotations")?.GetValue(tool);
        if (annotations is null)
        {
            return ReadBooleanProperty(tool, propertyNames);
        }

        return ReadBooleanProperty(annotations, propertyNames) ?? ReadBooleanProperty(tool, propertyNames);
    }

    private static bool? ReadBooleanProperty(object target, params string[] propertyNames)
    {
        var type = target.GetType();
        foreach (var propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property?.GetValue(target) is bool value)
            {
                return value;
            }
        }

        return null;
    }

    protected virtual async Task<McpClient> CreateStdioClientAsync(McpServerInfo mcpServerInfo, CancellationToken ct)
    {
        var arguments = ResolveCommandArguments(mcpServerInfo.Arguments);
        var transportOptions = new StdioClientTransportOptions
        {
            Command = string.IsNullOrWhiteSpace(mcpServerInfo.Command) ? "npx" : mcpServerInfo.Command,
            Arguments = arguments,
            WorkingDirectory = ResolveWorkingDirectory(arguments),
            StandardErrorLines = line => logger.LogWarning("MCP server {Name} stderr: {Line}", mcpServerInfo.Name, line)
        };

        var transport = new StdioClientTransport(transportOptions);
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    protected virtual async Task<McpClient> CreateSseClientAsync(McpServerInfo mcpServerInfo, CancellationToken ct)
    {
        if (!McpSseEndpointValidator.TryValidate(mcpServerInfo.Arguments, out var endpoint, out var endpointError))
        {
            throw new InvalidOperationException($"MCP SSE server {mcpServerInfo.Name} endpoint is invalid: {endpointError}");
        }

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = endpoint!,
            TransportMode = HttpTransportMode.Sse,
            ConnectionTimeout = SseConnectionTimeout
        };

        var transport = new HttpClientTransport(transportOptions);
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    private static string[] ResolveCommandArguments(string rawArguments)
    {
        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            return [];
        }

        if (File.Exists(rawArguments))
        {
            return [rawArguments];
        }

        return ParseCommandArguments(rawArguments);
    }

    private static string[] ParseCommandArguments(string rawArguments)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';
        var escaping = false;

        for (var index = 0; index < rawArguments.Length; index++)
        {
            var ch = rawArguments[index];

            if (escaping)
            {
                current.Append(ch);
                escaping = false;
                continue;
            }

            if (ch == '\\' && index + 1 < rawArguments.Length)
            {
                var next = rawArguments[index + 1];
                if ((inQuotes && (next == quoteChar || next == '\\')) ||
                    (!inQuotes && (char.IsWhiteSpace(next) || next is '"' or '\'' or '\\')))
                {
                    escaping = true;
                    continue;
                }
            }

            if (ch is '"' or '\'')
            {
                if (!inQuotes)
                {
                    inQuotes = true;
                    quoteChar = ch;
                    continue;
                }

                if (quoteChar == ch)
                {
                    inQuotes = false;
                    quoteChar = '\0';
                    continue;
                }
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                FlushArgument(arguments, current);
                continue;
            }

            current.Append(ch);
        }

        if (escaping)
        {
            current.Append('\\');
        }

        if (inQuotes)
        {
            throw new FormatException("MCP stdio arguments contain an unterminated quoted value.");
        }

        FlushArgument(arguments, current);
        return [.. arguments];
    }

    private static void FlushArgument(List<string> arguments, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        arguments.Add(current.ToString());
        current.Clear();
    }

    private static string? ResolveWorkingDirectory(string[] arguments)
    {
        if (arguments.Length != 1 || !File.Exists(arguments[0]))
        {
            return null;
        }

        return Path.GetDirectoryName(arguments[0]);
    }
}
