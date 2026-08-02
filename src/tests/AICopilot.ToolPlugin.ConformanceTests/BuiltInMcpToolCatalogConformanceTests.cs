using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.ToolPlugin.ConformanceTests;

public sealed class BuiltInMcpToolCatalogConformanceTests
{
    [Fact]
    public void BuiltInToolRegistrations_ShouldContainOnlyTheCurrentHarnessDiagnosticTool()
    {
        var tools = BuiltInToolRegistrations.HarnessTools;

        var diagnostic = tools.Should().ContainSingle().Which;
        diagnostic.ToolCode.Should().Be("plugin__diagnosticadvisorplugin__generatediagnosticchecklist");
        diagnostic.ProviderType.Should().Be(ToolProviderType.BuiltIn);
        diagnostic.TargetType.Should().Be(ToolRegistrationTargetType.Plugin);
        diagnostic.RequiresApproval.Should().BeTrue();
        diagnostic.RiskLevel.Should().Be(AiToolRiskLevel.RequiresApproval);
        diagnostic.RequiredPermission.Should().Be("AiGateway.Chat");
        diagnostic.DataBoundary.Should().Be(ToolDataBoundary.NoData);
        diagnostic.IsExecutableByAgent.Should().BeTrue();
        diagnostic.SchemaVersion.Should().Be(BuiltInToolRegistrations.CurrentSchemaVersion);
        diagnostic.CatalogVersion.Should().Be(BuiltInToolRegistrations.CurrentCatalogVersion);
    }
}
