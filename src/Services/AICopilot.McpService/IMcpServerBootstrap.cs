using ModelContextProtocol.Client;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.McpService;

public interface IMcpServerBootstrap
{
    IAsyncEnumerable<McpClient> StartAsync(CancellationToken cancellationToken);
}