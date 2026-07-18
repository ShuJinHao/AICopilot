using System.Text;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.AggregateTests;

public sealed class AgentPlanPersistenceDomainTests
{
    [Fact]
    public void AgentTask_ShouldPreservePlanJsonBeyondLegacyCharacterLimit()
    {
        var payload = $"{{\"schemaVersion\":\"2.0\",\"padding\":\"{new string('x', 40_000)}\"}}";

        var task = CreateTask(payload);

        task.PlanJson.Should().Be(payload);
        task.PlanJson.Length.Should().BeGreaterThan(32_000);
    }

    [Fact]
    public void AgentStep_ShouldAcceptExactUtf8InputBoundaryAndRejectOneByteOver()
    {
        var emptyPayloadBytes = Encoding.UTF8.GetByteCount("{\"value\":\"\"}");
        var exact = $"{{\"value\":\"{new string('x', 8_000 - emptyPayloadBytes)}\"}}";
        var task = CreateTask("{}");

        var step = task.AddStep("Input", "Input", AgentStepType.Analysis, null, false, DateTimeOffset.UtcNow, exact);
        var addOver = () => task.AddStep(
            "Over",
            "Over",
            AgentStepType.Analysis,
            null,
            false,
            DateTimeOffset.UtcNow,
            exact.Replace("\"}", "x\"}", StringComparison.Ordinal));

        Encoding.UTF8.GetByteCount(step.InputJson!).Should().Be(8_000);
        addOver.Should().Throw<ArgumentException>().WithMessage("*8001 UTF-8 bytes*8000*");
        task.Steps.Should().ContainSingle();
    }

    [Fact]
    public void AgentStep_ShouldPreserveLargeValidOutputWithoutLegacyTruncation()
    {
        var task = CreateTask("{}");
        var step = task.AddStep("Output", "Output", AgentStepType.Analysis, null, false, DateTimeOffset.UtcNow);
        var output = $"{{\"rows\":\"{new string('x', 24_000)}\"}}";

        step.Start(DateTimeOffset.UtcNow);
        step.Complete(output, DateTimeOffset.UtcNow);

        step.OutputJson.Should().Be(output);
        step.OutputJson!.Length.Should().BeGreaterThan(16_000);
    }

    [Fact]
    public void AgentStep_ShouldRejectInvalidStructuredJsonInsteadOfSavingPrefix()
    {
        var task = CreateTask("{}");

        var add = () => task.AddStep(
            "Invalid",
            "Invalid",
            AgentStepType.Analysis,
            null,
            false,
            DateTimeOffset.UtcNow,
            "{\"broken\":");

        add.Should().Throw<ArgumentException>().WithMessage("*valid JSON*");
        task.Steps.Should().BeEmpty();
    }

    private static AgentTask CreateTask(string planJson)
    {
        return new AgentTask(
            SessionId.New(),
            Guid.NewGuid(),
            "Plan persistence",
            "Plan persistence",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            planJson,
            DateTimeOffset.UtcNow);
    }
}
