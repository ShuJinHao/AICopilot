using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.WorkflowTests;

public sealed class AgentWorkflowBehaviorTests
{
    [Fact]
    public void AgentTaskStatusValues_ShouldPreserveHistoricalPersistenceValues()
    {
        ((int)AgentTaskStatus.WaitingPlanApproval).Should().Be(1);
        ((int)AgentTaskStatus.PlanApproved).Should().Be(2);
        ((int)AgentTaskStatus.Running).Should().Be(3);
        ((int)AgentTaskStatus.WaitingToolApproval).Should().Be(4);
        ((int)AgentTaskStatus.GeneratingArtifacts).Should().Be(5);
        ((int)AgentTaskStatus.WorkspaceReady).Should().Be(6);
        ((int)AgentTaskStatus.WaitingFinalApproval).Should().Be(7);
        ((int)AgentTaskStatus.Finalized).Should().Be(8);
        ((int)AgentTaskStatus.Completed).Should().Be(9);
        ((int)AgentTaskStatus.Rejected).Should().Be(10);
        ((int)AgentTaskStatus.Failed).Should().Be(11);
        ((int)AgentTaskStatus.Cancelled).Should().Be(12);
        ((int)AgentTaskStatus.Draft).Should().Be(100);
    }

    [Fact]
    public async Task AgentWorkflowSink_ShouldFlushWrittenChunksBeforeCompletion()
    {
        var sink = new AgentWorkflowSink();
        await sink.WriteAsync(new ChatChunk("data-analysis", ChunkType.Text, "first"), CancellationToken.None);
        await sink.WriteAsync(new ChatChunk("data-analysis", ChunkType.Text, "second"), CancellationToken.None);

        sink.Complete();

        var chunks = await ReadAllAsync(sink);

        chunks.Select(chunk => chunk.Content).Should().Equal("first", "second");
    }

    [Fact]
    public async Task AgentWorkflowSink_ShouldPropagateBranchFailureToReader()
    {
        var sink = new AgentWorkflowSink();
        var failure = new InvalidOperationException("branch failed");

        sink.Complete(failure);
        var read = async () => await ReadAllAsync(sink);

        await read.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("branch failed");
    }

    [Fact]
    public void AgentWorkflowTopology_ShouldDeclareParallelFanOutBranches()
    {
        AgentWorkflowTopology.Stages
            .Where(stage => stage.Kind == AgentWorkflowStageKind.ParallelFanOut)
            .Select(stage => stage.Id)
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .Be("ParallelFanOut");

        AgentWorkflowTopology.ParallelBranches
            .OrderBy(branch => branch.Order)
            .Select(branch => branch.BranchType)
            .Should()
            .Equal(
                BranchType.Tools,
                BranchType.Knowledge,
                BranchType.DataAnalysis,
                BranchType.BusinessPolicy);
    }

    [Fact]
    public async Task PlanCapabilityDiscovery_ShouldRejectOpaqueWritableMcp_AndKeepStrictReadOnlyMcpAndLocalTools()
    {
        var opaqueWritableMcp = new AiToolDefinition
        {
            Name = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "gateway-a17", "deleteDevice"),
            ToolName = "deleteDevice",
            Description = "Opaque remote target at https://relay.example.test/mcp",
            Kind = AiToolCallKind.Mcp,
            TargetType = AiToolTargetType.McpServer,
            TargetName = "gateway-a17",
            ServerName = "gateway-a17",
            ExternalSystemType = AiToolExternalSystemType.NonCloud,
            CapabilityKind = AiToolCapabilityKind.SideEffecting,
            RiskLevel = AiToolRiskLevel.Low,
            ReadOnlyDeclared = true
        };
        var strictReadOnlyMcp = new AiToolDefinition
        {
            Name = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "gateway-a17", "queryStatus"),
            ToolName = "queryStatus",
            Description = "Query remote status without changing state.",
            Kind = AiToolCallKind.Mcp,
            TargetType = AiToolTargetType.McpServer,
            TargetName = "gateway-a17",
            ServerName = "gateway-a17",
            ExternalSystemType = AiToolExternalSystemType.CloudReadOnly,
            CapabilityKind = AiToolCapabilityKind.ReadOnlyQuery,
            RiskLevel = AiToolRiskLevel.Low,
            ReadOnlyDeclared = true,
            McpReadOnlyHint = true,
            McpDestructiveHint = false
        };
        var localSideEffect = new AiToolDefinition
        {
            Name = AiToolIdentity.CreateRuntimeName(AiToolTargetType.Plugin, "local-files", "writeReport"),
            ToolName = "writeReport",
            Description = "Write a local report file.",
            Kind = AiToolCallKind.Function,
            TargetType = AiToolTargetType.Plugin,
            TargetName = "local-files",
            ExternalSystemType = AiToolExternalSystemType.NonCloud,
            CapabilityKind = AiToolCapabilityKind.SideEffecting,
            RiskLevel = AiToolRiskLevel.RequiresApproval
        };
        var pipeline = new AgentWorkflowPipeline(
            new FixedIntentRoutingExecutor(),
            new FixedToolsPackExecutor([opaqueWritableMcp, strictReadOnlyMcp, localSideEffect]),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            NullLogger<AgentWorkflowPipeline>.Instance);

        var result = await pipeline.RunPlanDraftWorkflowAsync(
            new ChatStreamRequest(Guid.NewGuid(), "discover tools"));

        result.Tools.Should().NotContain(tool => tool.Name == opaqueWritableMcp.Name);
        result.Tools.Should().Contain(tool => tool.Name == strictReadOnlyMcp.Name);
        result.Tools.Should().Contain(tool => tool.Name == localSideEffect.Name);
    }

    private static async Task<List<ChatChunk>> ReadAllAsync(AgentWorkflowSink sink)
    {
        var chunks = new List<ChatChunk>();
        await foreach (var chunk in sink.ReadAllAsync(CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private sealed class FixedIntentRoutingExecutor : IntentRoutingExecutor
    {
        public FixedIntentRoutingExecutor()
            : base(null!, null!, null!, null!, null!, NullLogger<IntentRoutingExecutor>.Instance)
        {
        }

        public override Task<IntentRoutingStepResult> ExecuteAsync(
            ChatStreamRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new IntentRoutingStepResult(
                [new IntentResult { Intent = "Action.SafetyFixture", Confidence = 1.0 }],
                ManufacturingSceneType.FallbackToExistingRouting,
                null,
                new ChatExecutionMetadataSnapshot()));
        }
    }

    private sealed class FixedToolsPackExecutor(AiToolDefinition[] tools)
        : ToolsPackExecutor(null!, NullLogger<ToolsPackExecutor>.Instance)
    {
        public override Task<BranchResult> DiscoverAsync(
            List<IntentResult> intentResults,
            CancellationToken ct = default) => Task.FromResult(BranchResult.FromTools(tools));
    }
}
