using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.ApplicationTests;

public sealed class AgentReportComposerTests
{
    private const string SimulationLabel = "\u6a21\u62df Cloud \u53ea\u8bfb\u6570\u636e";
    private const string EvidenceSetDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

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
        root.GetProperty("evidenceSetDigest").GetString().Should().Be(EvidenceSetDigest);
        root.GetProperty("truthClasses").EnumerateArray().Select(item => item.GetString()).Should()
            .Equal("DerivedFact", "ObservedFact");
        root.GetProperty("evidenceAsOfUtc").GetDateTimeOffset().Should()
            .Be(DateTimeOffset.Parse("2026-07-22T08:00:00Z"));
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
        report.CloudReadonlySource.SourcePath.Should().BeNull("endpoint-like source paths are not export-safe");
        report.EvidenceSetDigest.Should().Be(EvidenceSetDigest);
        report.TruthClasses.Should().Equal("DerivedFact", "ObservedFact");

        markdown.Should().Contain("sourceMode=Simulation");
        markdown.Should().Contain("isSimulation=true");
        markdown.Should().Contain(SimulationLabel);
        markdown.Should().Contain("plannedOutput");
        markdown.Should().Contain("avg.plannedOutput");
        markdown.Should().Contain(EvidenceSetDigest);
        markdown.Should().Contain("DerivedFact, ObservedFact");

        html.Should().Contain("sourceMode=Simulation");
        html.Should().Contain("isSimulation=true");
        html.Should().Contain(SimulationLabel);
        html.Should().Contain("plannedOutput");
        html.Should().Contain("avg.plannedOutput");
        html.Should().Contain(EvidenceSetDigest);
        html.Should().Contain("DerivedFact, ObservedFact");
    }

    [Fact]
    public void ReportSurfaces_ShouldPreserveCloudQueryRosterAndEvidenceBoundInference()
    {
        var task = CreateTask();
        var state = CreateSimulationState();
        state.CloudReadonlyResults.Add(new AgentCloudReadonlyQuerySnapshot(
            "Analysis.Capacity.ByDevice",
            new string('b', 64),
            state.CloudReadonlySummary!,
            state.CloudReadonlyRows,
            SimulationLabel,
            "Simulation",
            true,
            2,
            false,
            DateTimeOffset.Parse("2026-07-22T08:00:00Z")));
        state.ReasoningOutcome = new AgentReasoningToolOutput(
            "completed",
            "agent-reasoning",
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            "Completed",
            "LlmInference",
            "Capacity remained close to plan in the authorized evidence window.",
            ["Both lines remained within the reviewed variance band."],
            ["evidence:0123456789abcdef"],
            ["source-truncated"],
            "None",
            0.82,
            true,
            false,
            1);

        var report = AgentReportComposer.BuildReportDocument(task, state);
        var markdown = AgentReportComposer.BuildMarkdownReport(task, state);
        var html = AgentReportComposer.BuildHtmlReport(task, state);
        using var chart = JsonDocument.Parse(JsonSerializer.Serialize(
            AgentReportComposer.BuildChartPayload(state),
            JsonOptions));

        report.CloudReadonlyQueries.Should().ContainSingle(query =>
            query.Intent == "Analysis.Capacity.ByDevice");
        report.AiInference.Should().NotBeNull();
        report.AiInference!.TruthClass.Should().Be("LlmInference");
        markdown.Should().Contain("AI Evidence Inference");
        markdown.Should().Contain("evidence:0123456789abcdef");
        html.Should().Contain("AI Evidence Inference");
        chart.RootElement.GetProperty("cloudReadonlyQueries").GetArrayLength().Should().Be(1);
        chart.RootElement.GetProperty("aiInference").GetProperty("truthClass").GetString()
            .Should().Be("LlmInference");
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
            ReportEvidenceSetDigest = EvidenceSetDigest,
            ReportTruthClasses = ["DerivedFact", "ObservedFact"],
            ReportEvidenceAsOfUtc = DateTimeOffset.Parse("2026-07-22T08:00:00Z"),
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
