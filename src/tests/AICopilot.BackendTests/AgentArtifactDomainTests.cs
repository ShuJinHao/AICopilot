using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.BackendTests;

[Trait("Suite", "AgentArtifact")]
public sealed class AgentArtifactDomainTests
{
    [Fact]
    public void AgentTask_ShouldRequirePlanApprovalBeforeRun()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            Guid.NewGuid(),
            "生成报告",
            "分析上传数据并生成报告",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Medium,
            LanguageModelId.New(),
            """{"planned_steps":[]}""",
            now);

        var startBeforeApproval = () => task.Start(now);
        startBeforeApproval.Should().Throw<InvalidOperationException>();

        task.ApprovePlan(now);
        task.Start(now);

        task.Status.Should().Be(AgentTaskStatus.Running);
    }

    [Theory]
    [InlineData("../draft/report.md")]
    [InlineData("C:/tmp/report.md")]
    [InlineData("final/report.pdf")]
    public void ArtifactWorkspace_ShouldRejectUnsafeDraftPaths(string relativePath)
    {
        var workspace = new ArtifactWorkspace(
            AgentTaskId.New(),
            "ws_test",
            "agent-workspaces/ws_test",
            "/ai/workspaces/ws_test",
            DateTimeOffset.UtcNow);

        var action = () => workspace.AddDraftArtifact(
            ArtifactType.Markdown,
            "报告",
            relativePath,
            10,
            "text/markdown",
            null,
            DateTimeOffset.UtcNow);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Artifact_ShouldOnlyMoveApprovedArtifactsToFinal()
    {
        var workspace = new ArtifactWorkspace(
            AgentTaskId.New(),
            "ws_report",
            "agent-workspaces/ws_report",
            "/ai/workspaces/ws_report",
            DateTimeOffset.UtcNow);
        var artifact = workspace.AddDraftArtifact(
            ArtifactType.Markdown,
            "报告",
            "draft/report.md",
            10,
            "text/markdown",
            null,
            DateTimeOffset.UtcNow);

        var markFinalBeforeApproval = () => artifact.MarkFinal("final/report.md", DateTimeOffset.UtcNow);
        markFinalBeforeApproval.Should().Throw<InvalidOperationException>();

        artifact.Approve(DateTimeOffset.UtcNow);
        artifact.MarkFinal("final/report.md", DateTimeOffset.UtcNow);

        artifact.Status.Should().Be(ArtifactStatus.Final);
        artifact.RelativePath.Should().Be("final/report.md");
    }

    [Fact]
    public void ApprovalRequest_ShouldOnlyCompleteOnce()
    {
        var approval = new ApprovalRequest(
            AgentTaskId.New(),
            AgentApprovalType.Plan,
            "plan",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        approval.Approve(Guid.NewGuid(), "同意执行", DateTimeOffset.UtcNow);
        var secondApproval = () => approval.Reject(Guid.NewGuid(), "再次处理", DateTimeOffset.UtcNow);

        secondApproval.Should().Throw<InvalidOperationException>();
        approval.Status.Should().Be(AgentApprovalStatus.Approved);
    }

    [Fact]
    public void ApprovalRequest_ShouldSupportPendingExpiration()
    {
        var approval = new ApprovalRequest(
            AgentTaskId.New(),
            AgentApprovalType.ToolCall,
            "step-1",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        approval.Expire(DateTimeOffset.UtcNow);
        var decideAfterExpiration = () => approval.Approve(Guid.NewGuid(), "late approval", DateTimeOffset.UtcNow);

        approval.Status.Should().Be(AgentApprovalStatus.Expired);
        decideAfterExpiration.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AgentStep_ShouldEscalateRuntimeHighRiskToolToApproval()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            Guid.NewGuid(),
            "生成受控产物",
            "生成 PDF 草稿并等待确认",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Medium,
            null,
            """{"planned_steps":[]}""",
            now);
        var step = task.AddStep(
            "生成 PDF 草稿",
            "生成 draft/report.pdf",
            AgentStepType.ArtifactGeneration,
            "generate_pdf",
            requiresApproval: false,
            now);

        step.WaitForApproval();
        step.Approve();

        step.RequiresApproval.Should().BeTrue();
        step.Status.Should().Be(AgentStepStatus.Pending);
    }

    [Fact]
    public void ChatRuntimeSettings_ShouldClampUnsafeRuntimeValues()
    {
        var settings = new ChatRuntimeSettings(
            routingHistoryCount: 200,
            answerHistoryCount: -1,
            ragRewriteHistoryCount: 100,
            agentPlanningHistoryCount: 100,
            summaryThresholdMessages: 1,
            contextTokenLimit: 100,
            DateTimeOffset.UtcNow);

        settings.RoutingHistoryCount.Should().Be(20);
        settings.AnswerHistoryCount.Should().Be(0);
        settings.RagRewriteHistoryCount.Should().Be(20);
        settings.AgentPlanningHistoryCount.Should().Be(30);
        settings.SummaryThresholdMessages.Should().Be(5);
        settings.ContextTokenLimit.Should().Be(4000);
    }

    [Fact]
    public void UploadRecord_ShouldRecordKnowledgeBaseBindingWithoutServerPath()
    {
        var record = new UploadRecord(
            UploadRecordScope.KnowledgeBase,
            Guid.NewGuid(),
            null,
            null,
            Guid.NewGuid(),
            12,
            "rule.md",
            "text/markdown",
            128,
            new string('a', 64),
            null,
            DateTimeOffset.UtcNow);

        record.Status.Should().Be(UploadRecordStatus.LinkedToKnowledgeBase);
        record.RagDocumentId.Should().Be(12);
        record.StoragePath.Should().BeNull();
    }
}
