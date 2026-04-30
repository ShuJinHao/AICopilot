using AICopilot.AgentPlugin;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Specifications.ApprovalPolicy;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.McpServer.Specifications.McpServerInfo;
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
    IReadRepository<ApprovalPolicy> approvalPolicyRepository,
    AgentPluginLoader agentPluginLoader,
    ILogger<McpServerBootstrap> logger)
    : IMcpServerBootstrap
{
    private static readonly TimeSpan SseConnectionTimeout = TimeSpan.FromSeconds(15);

    public async IAsyncEnumerable<McpClient> StartAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var mcpServerInfos = await mcpServerRepository.ListAsync(new EnabledMcpServerInfosSpec(), ct);
        var approvalPolicies = await LoadApprovalPoliciesAsync(mcpServerInfos.Select(server => server.Name).ToArray());

        foreach (var mcpServerInfo in mcpServerInfos)
        {
            if (!mcpServerInfo.ChatExposureMode.CanExposeInChat())
            {
                logger.LogInformation(
                    "MCP server {Name} is configured as {ExposureMode} and will not be exposed to the production chat toolchain.",
                    mcpServerInfo.Name,
                    mcpServerInfo.ChatExposureMode);
                continue;
            }

            if (mcpServerInfo.AllowedToolNames.Count == 0)
            {
                logger.LogInformation(
                    "MCP server {Name} has no explicit allowlist, so it remains closed to the production chat toolchain.",
                    mcpServerInfo.Name);
                continue;
            }

            McpClient mcpClient = mcpServerInfo.TransportType switch
            {
                McpTransportType.Stdio => await CreateStdioClientAsync(mcpServerInfo, ct),
                McpTransportType.Sse => await CreateSseClientAsync(mcpServerInfo, ct),
                _ => throw new NotSupportedException($"Unsupported MCP transport type: {mcpServerInfo.TransportType}")
            };

            logger.LogInformation("Connected to MCP server {Name}.", mcpServerInfo.Name);

            var tools = await mcpClient.ListToolsAsync(cancellationToken: ct);
            var allowlist = new HashSet<string>(mcpServerInfo.AllowedToolNames, StringComparer.OrdinalIgnoreCase);
            var exposedTools = tools
                .Where(tool => allowlist.Contains(tool.Name))
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
                await mcpClient.DisposeAsync();
                continue;
            }

            var protectedNames = approvalPolicies.GetValueOrDefault(mcpServerInfo.Name)
                                 ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            RegisterMcpPlugin(mcpServerInfo, exposedTools, protectedNames);
            logger.LogInformation("Registered MCP plugin {Name}.", mcpServerInfo.Name);

            yield return mcpClient;
        }
    }

    private async Task<Dictionary<string, HashSet<string>>> LoadApprovalPoliciesAsync(string[] serverNames)
    {
        if (serverNames.Length == 0)
        {
            return [];
        }

        var serverNameSet = new HashSet<string>(serverNames, StringComparer.OrdinalIgnoreCase);
        var policies = await approvalPolicyRepository.ListAsync(
            new EnabledApprovalPoliciesByTargetTypeSpec(ApprovalTargetType.McpServer));

        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var policy in policies.Where(policy => serverNameSet.Contains(policy.TargetName)))
        {
            if (!result.TryGetValue(policy.TargetName, out var names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[policy.TargetName] = names;
            }

            foreach (var toolName in policy.ToolNames)
            {
                names.Add(toolName);
            }
        }

        return result;
    }

    private void RegisterMcpPlugin(
        McpServerInfo mcpServerInfo,
        IList<McpClientTool> mcpTools,
        HashSet<string> protectedNames)
    {
        var tools = mcpTools
            .Select(tool => ToToolDefinition(mcpServerInfo.Name, tool, protectedNames))
            .ToArray();

        var mcpPlugin = new GenericBridgePlugin
        {
            Name = mcpServerInfo.Name,
            Description = mcpServerInfo.Description,
            Tools = tools,
            ChatExposureMode = mcpServerInfo.ChatExposureMode
        };

        agentPluginLoader.RegisterAgentPlugin(mcpPlugin);
    }

    private static AiToolDefinition ToToolDefinition(
        string serverName,
        McpClientTool tool,
        HashSet<string> protectedNames)
    {
        return new AiToolDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            Kind = AiToolCallKind.Mcp,
            ServerName = serverName,
            RequiresApproval = protectedNames.Contains(tool.Name),
            JsonSchema = tool.JsonSchema.Clone(),
            ReturnJsonSchema = tool.ReturnJsonSchema?.Clone(),
            InvokeAsync = async (context, cancellationToken) =>
            {
                var argumentValues = context.Arguments.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase);
                var arguments = new AIFunctionArguments(argumentValues);

                return await tool.InvokeAsync(arguments, cancellationToken);
            }
        };
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
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(mcpServerInfo.Arguments),
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
