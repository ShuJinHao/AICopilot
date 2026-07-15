using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.ApplicationTests;

public sealed class AgentReportComposerTests
{
    private const string SimulationLabel = "\u6a21\u62df Cloud \u53ea\u8bfb\u6570\u636e";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BuildChartPayload_ShouldCreateChartV2_WithMultiSeriesSimulationSource()
    {
        var state = CreateSimulationState();

        var payload = AgentReportComposer.BuildChartPayload(state);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, JsonOptions));
        var root = document.RootElement;

        root.GetProperty("schemaVersion").GetInt32().Should().Be(2);
        root.GetProperty("chartType").GetString().Should().Be("bar");
        root.GetProperty("type").GetString().Should().Be("bar");
        root.GetProperty("series").GetArrayLength().Should().BeGreaterThan(1);
        root.GetProperty("values").GetArrayLength().Should().Be(2);
        root.GetProperty("rowCount").GetInt32().Should().Be(2);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();

        var sourceInfo = root.GetProperty("sourceInfo");
        sourceInfo.GetProperty("sourceMode").GetString().Should().Be("Simulation");
        sourceInfo.GetProperty("isSimulation").GetBoolean().Should().BeTrue();
        sourceInfo.GetProperty("sourceLabel").GetString().Should().Be(SimulationLabel);
        sourceInfo.GetProperty("rowCount").GetInt32().Should().Be(2);
        sourceInfo.GetProperty("isTruncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void BuildReportDocument_ShouldUseUnifiedSimulationModel_ForMarkdownAndHtml()
    {
        var task = CreateTask();
        var state = CreateSimulationState();

        var report = AgentReportComposer.BuildReportDocument(task, state);
        var markdown = AgentReportComposer.BuildMarkdownReport(task, state);
        var html = AgentReportComposer.BuildHtmlReport(task, state);

        report.CloudReadonlySource.Should().NotBeNull();
        report.CloudReadonlySource!.SourceMode.Should().Be("Simulation");
        report.CloudReadonlySource.IsSimulation.Should().BeTrue();
        report.Tables.Should().Contain(table => table.Name == SimulationLabel);
        report.Metrics.Should().Contain(metric => metric.Name == "sourceMode" && metric.Value == "Simulation");
        report.Metrics.Should().Contain(metric => metric.Name == "avg.plannedOutput");

        markdown.Should().Contain("sourceMode=Simulation");
        markdown.Should().Contain("isSimulation=true");
        markdown.Should().Contain(SimulationLabel);
        markdown.Should().Contain("plannedOutput");
        markdown.Should().Contain("avg.plannedOutput");

        html.Should().Contain("sourceMode=Simulation");
        html.Should().Contain("isSimulation=true");
        html.Should().Contain(SimulationLabel);
        html.Should().Contain("plannedOutput");
        html.Should().Contain("avg.plannedOutput");
    }

    private static AgentTask CreateTask()
    {
        return new AgentTask(
            SessionId.New(),
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "Simulation report",
            "Review weekly production capacity from offline simulation data.",
            AgentTaskType.CloudDataReport,
            AgentTaskRiskLevel.Medium,
            null,
            "{}",
            DateTimeOffset.UtcNow);
    }

    private static AgentTaskRunState CreateSimulationState()
    {
        return new AgentTaskRunState
        {
            CloudReadonlySummary = $"sourceMode=Simulation; isSimulation=true; sourceLabel={SimulationLabel}; returned 2 rows.",
            CloudReadonlySourceLabel = SimulationLabel,
            CloudReadonlySourcePath = "/simulation/manufacturing/weekly-report",
            CloudReadonlySourceMode = "Simulation",
            CloudReadonlyIsSimulation = true,
            CloudReadonlyRowCount = 2,
            CloudReadonlyIsTruncated = false,
            CloudReadonlyRows =
            [
                new Dictionary<string, object?>
                {
                    ["line"] = "Line-A",
                    ["plannedOutput"] = 1200,
                    ["actualOutput"] = 1174,
                    ["defects"] = 7
                },
                new Dictionary<string, object?>
                {
                    ["line"] = "Line-B",
                    ["plannedOutput"] = 980,
                    ["actualOutput"] = 955,
                    ["defects"] = 11
                }
            ]
        };
    }
}
