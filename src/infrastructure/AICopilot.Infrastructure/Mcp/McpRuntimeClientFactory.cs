using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.Text;

namespace AICopilot.Infrastructure.Mcp;

internal static class McpRuntimeClientFactory
{
    private static readonly TimeSpan SseConnectionTimeout = TimeSpan.FromSeconds(15);

    public static async Task<McpClient> CreateStdioClientAsync(
        McpServerInfo mcpServerInfo,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var arguments = ResolveCommandArguments(mcpServerInfo.Arguments);
        var command = string.IsNullOrWhiteSpace(mcpServerInfo.Command) ? "npx" : mcpServerInfo.Command;
        McpRuntimeStdioCommandResolver.EnsureAvailable(command);
        var transportOptions = new StdioClientTransportOptions
        {
            Command = command,
            Arguments = arguments,
            WorkingDirectory = ResolveWorkingDirectory(arguments),
            StandardErrorLines = line => logger.LogWarning("MCP server {Name} stderr: {Line}", mcpServerInfo.Name, line)
        };

        var transport = new StdioClientTransport(transportOptions);
        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    public static async Task<McpClient> CreateSseClientAsync(
        McpServerInfo mcpServerInfo,
        CancellationToken cancellationToken)
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
        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    internal static string[] ResolveCommandArguments(string rawArguments)
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
