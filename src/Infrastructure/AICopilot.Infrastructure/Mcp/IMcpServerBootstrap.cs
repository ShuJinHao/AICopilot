using ModelContextProtocol.Client;

namespace AICopilot.Infrastructure.Mcp;

public interface IMcpServerBootstrap
{
    IAsyncEnumerable<McpClient> StartAsync(CancellationToken cancellationToken);
}
