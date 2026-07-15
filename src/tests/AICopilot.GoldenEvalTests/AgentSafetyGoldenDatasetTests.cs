using System.Reflection;
using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.GoldenEvalTests;

public sealed class AgentSafetyGoldenDatasetTests
{
    private const string DatasetResource =
        "AICopilot.GoldenEvalTests.datasets.v1.agent-safety-matrix.json";

    public static IEnumerable<object[]> Cases()
    {
        using var document = LoadDataset();
        foreach (var testCase in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return [testCase.GetProperty("id").GetString()!];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ProductionWorkflow_ShouldMatchVersionedSafetyDataset(string caseId)
    {
        using var document = LoadDataset();
        document.RootElement.GetProperty("changeReason").GetString()
            .Should().NotBeNullOrWhiteSpace();
        var testCase = document.RootElement.GetProperty("cases")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("id").GetString() == caseId);
        var input = testCase.GetProperty("input");
        var tool = new AiToolDefinition
        {
            Name = input.GetProperty("toolName").GetString()!,
            ToolName = input.GetProperty("toolName").GetString()!,
            Description = input.GetProperty("description").GetString(),
            ExternalSystemType = Enum.Parse<AiToolExternalSystemType>(
                input.GetProperty("externalSystemType").GetString()!),
            CapabilityKind = Enum.Parse<AiToolCapabilityKind>(
                input.GetProperty("capabilityKind").GetString()!),
            RiskLevel = Enum.Parse<AiToolRiskLevel>(input.GetProperty("riskLevel").GetString()!),
            ReadOnlyDeclared = input.GetProperty("readOnlyDeclared").GetBoolean()
        };
        var pipeline = CreatePipeline(tool);

        var result = await pipeline.RunPlanDraftWorkflowAsync(
            new ChatStreamRequest(Guid.NewGuid(), "evaluate versioned tool safety"));

        var exposed = result.Tools.Any(candidate => candidate.Name == tool.Name);
        exposed.Should().Be(testCase.GetProperty("expected").GetProperty("exposed").GetBoolean());
    }

    private static AgentWorkflowPipeline CreatePipeline(AiToolDefinition tool)
    {
        return new AgentWorkflowPipeline(
            new GoldenIntentRoutingExecutor(),
            new GoldenToolsPackExecutor(tool),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            NullLogger<AgentWorkflowPipeline>.Instance);
    }

    private static JsonDocument LoadDataset()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DatasetResource)
                     ?? throw new InvalidOperationException(
                         $"Golden dataset resource is missing: {DatasetResource}");
        return JsonDocument.Parse(stream);
    }

    private sealed class GoldenIntentRoutingExecutor : IntentRoutingExecutor
    {
        public GoldenIntentRoutingExecutor()
            : base(null!, null!, null!, null!, null!, NullLogger<IntentRoutingExecutor>.Instance)
        {
        }

        public override Task<IntentRoutingStepResult> ExecuteAsync(
            ChatStreamRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new IntentRoutingStepResult(
                [new IntentResult { Intent = "Action.GoldenSafety", Confidence = 1.0 }],
                ManufacturingSceneType.FallbackToExistingRouting,
                null,
                new ChatExecutionMetadataSnapshot()));
        }
    }

    private sealed class GoldenToolsPackExecutor(AiToolDefinition tool)
        : ToolsPackExecutor(null!, NullLogger<ToolsPackExecutor>.Instance)
    {
        public override Task<BranchResult> DiscoverAsync(
            List<IntentResult> intentResults,
            CancellationToken ct = default) =>
            Task.FromResult(BranchResult.FromTools([tool]));
    }
}
