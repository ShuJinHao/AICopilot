using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.ToolPlugin.ConformanceTests;

public sealed class BuiltInMcpToolCatalogConformanceTests
{
    [Fact]
    public void BuiltInToolRegistrations_ShouldSeedMockMcpToolsDisabledByDefault()
    {
        var tools = BuiltInToolRegistrations.AgentRuntimeTools;

        tools.Should().Contain(tool => tool.ToolCode == "mock_mcp_health_check" &&
                                      tool.ProviderType == ToolProviderType.MockMcp &&
                                      !tool.IsEnabled &&
                                      !tool.IsVisibleToPlanner &&
                                      !tool.IsExecutableByAgent &&
                                      tool.CatalogVersion == BuiltInToolRegistrations.CurrentCatalogVersion);
        tools.Should().Contain(tool => tool.ToolCode == "mock_mcp_kpi_formula_lookup" &&
                                      tool.DataBoundary == ToolDataBoundary.RagContextOnly);
        tools.Should().Contain(tool => tool.ToolCode == "mock_mcp_artifact_quality_check" &&
                                      tool.DataBoundary == ToolDataBoundary.ArtifactDraftOnly);

        var ticketPreview = tools.Should().ContainSingle(tool => tool.ToolCode == "mock_mcp_external_ticket_preview").Which;
        ticketPreview.RiskLevel.Should().Be(AiToolRiskLevel.High);
        ticketPreview.RequiresApproval.Should().BeTrue();
        ticketPreview.ApprovalPolicy.Should().Be("ToolApproval");

        var cloudReadonly = tools.Should().ContainSingle(tool => tool.ToolCode == "query_cloud_data_readonly").Which;
        cloudReadonly.IsEnabled.Should().BeFalse();
        cloudReadonly.IsVisibleToPlanner.Should().BeFalse();
        cloudReadonly.IsExecutableByAgent.Should().BeFalse();

        var businessReadonly = tools.Should().ContainSingle(tool => tool.ToolCode == "query_business_database_readonly").Which;
        businessReadonly.RequiredPermission.Should().Be("DataSource.TextToSql");
    }
}
