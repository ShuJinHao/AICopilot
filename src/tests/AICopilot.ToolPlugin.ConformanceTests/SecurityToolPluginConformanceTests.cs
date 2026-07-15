using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.ToolPlugin.ConformanceTests;

public sealed class SecurityToolPluginConformanceTests
{
    [Fact]
    public void BuiltInTools_ShouldNotExposeLegacyTrialPilotToolCodes()
    {
        BuiltInToolRegistrations.AgentRuntimeTools
            .Select(tool => tool.ToolCode)
            .Should()
            .NotIntersectWith(BuiltInToolRegistrations.ObsoleteAgentRuntimeToolCodes);
    }
    [Theory]
    [InlineData("https://mcp.example.com/sse")]
    [InlineData("http://8.8.8.8/sse")]
    public void McpSseEndpointValidator_ShouldAllowPublicHttpEndpoints(string endpoint)
    {
        var isValid = McpSseEndpointValidator.TryValidate(endpoint, out var uri, out var errorMessage);

        isValid.Should().BeTrue(errorMessage);
        uri.Should().NotBeNull();
    }
    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("file:///tmp/mcp.sock")]
    [InlineData("http://user:pass@mcp.example.com/sse")]
    [InlineData("https://mcp.example.com/sse#fragment")]
    [InlineData("http://localhost/sse")]
    [InlineData("http://dev.localhost/sse")]
    [InlineData("http://127.0.0.1/sse")]
    [InlineData("http://[::1]/sse")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1/sse")]
    [InlineData("http://172.16.0.1/sse")]
    [InlineData("http://192.168.0.1/sse")]
    public void McpSseEndpointValidator_ShouldRejectUnsafeEndpoints(string endpoint)
    {
        var isValid = McpSseEndpointValidator.TryValidate(endpoint, out var uri, out var errorMessage);

        isValid.Should().BeFalse();
        uri.Should().BeNull();
        errorMessage.Should().NotBeNullOrWhiteSpace();
    }
}
