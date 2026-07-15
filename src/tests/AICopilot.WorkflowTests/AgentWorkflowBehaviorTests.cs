using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;

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

    private static async Task<List<ChatChunk>> ReadAllAsync(AgentWorkflowSink sink)
    {
        var chunks = new List<ChatChunk>();
        await foreach (var chunk in sink.ReadAllAsync(CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }
}
