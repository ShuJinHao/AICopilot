using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Uploads;

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

        task.ConfirmExecutablePlan(task.PlanJson, Array.Empty<int>(), now);
        var startBeforePlanApproval = () => task.Start(now);
        startBeforePlanApproval.Should().Throw<InvalidOperationException>();

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
    public void ArtifactWorkspace_ShouldRejectDraftWritesAfterFinalization()
    {
        var workspace = new ArtifactWorkspace(
            AgentTaskId.New(),
            "ws_report_final",
            "agent-workspaces/ws_report_final",
            "/ai/workspaces/ws_report_final",
            DateTimeOffset.UtcNow);
        var artifact = workspace.AddDraftArtifact(
            ArtifactType.Markdown,
            "report.md",
            "draft/report.md",
            10,
            "text/markdown",
            null,
            DateTimeOffset.UtcNow);
        artifact.Approve(DateTimeOffset.UtcNow);
        artifact.MarkFinal("final/report.md", DateTimeOffset.UtcNow);
        workspace.FinalizeWorkspace(DateTimeOffset.UtcNow);

        var addDraft = () => workspace.AddDraftArtifact(
            ArtifactType.Markdown,
            "report-v2.md",
            "draft/report-v2.md",
            20,
            "text/markdown",
            null,
            DateTimeOffset.UtcNow);

        addDraft.Should().Throw<InvalidOperationException>()
            .WithMessage("artifact_finalized:*");
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
        step.Status.Should().Be(AgentStepStatus.Approved);
    }

    [Fact]
    public void ChatRuntimeSettings_ShouldClampUnsafeRuntimeValues()
    {
        var settings = new ChatRuntimeSettings(
            routingHistoryCount: 200,
            answerHistoryCount: -1,
            ragRewriteHistoryCount: 100,
            agentPlanningHistoryCount: 100,
            contextTokenLimit: 100,
            DateTimeOffset.UtcNow);

        settings.RoutingHistoryCount.Should().Be(20);
        settings.AnswerHistoryCount.Should().Be(0);
        settings.RagRewriteHistoryCount.Should().Be(20);
        settings.AgentPlanningHistoryCount.Should().Be(30);
        settings.ContextTokenLimit.Should().Be(4000);
    }

    [Fact]
    public void ChatRuntimeSettings_ShouldUseTenAnswerHistoryMessagesByDefault()
    {
        var settings = ChatRuntimeSettings.CreateDefault(DateTimeOffset.UtcNow);

        settings.AnswerHistoryCount.Should().Be(10);
        settings.RoutingHistoryCount.Should().Be(4);
        settings.RagRewriteHistoryCount.Should().Be(4);
        settings.AgentPlanningHistoryCount.Should().Be(6);
    }

    [Fact]
    public void UploadRecord_ShouldRejectKnowledgeBaseShadowScope()
    {
        var action = () => new UploadRecord(
            UploadRecordScope.KnowledgeBase,
            Guid.NewGuid(),
            null,
            null,
            "rule.md",
            "text/markdown",
            128,
            new string('a', 64),
            "uploads/rule.md",
            DateTimeOffset.UtcNow);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UploadRecord_ShouldRequireExactlyOneActiveScopeTarget()
    {
        var action = () => new UploadRecord(
            UploadRecordScope.SessionTemp,
            Guid.NewGuid(),
            SessionId.New(),
            AgentTaskId.New(),
            "rule.md",
            "text/markdown",
            128,
            new string('a', 64),
            "uploads/rule.md",
            DateTimeOffset.UtcNow);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UploadRecordSpecifications_ShouldHideInactiveHistoricalRows()
    {
        var userId = Guid.NewGuid();
        var sessionId = SessionId.New();
        var taskId = AgentTaskId.New();
        var activeSession = CreateUploadRecord(
            UploadRecordScope.SessionTemp,
            userId,
            sessionId,
            null);
        var failedSession = CreateUploadRecord(
            UploadRecordScope.SessionTemp,
            userId,
            sessionId,
            null);
        var deletedAgent = CreateUploadRecord(
            UploadRecordScope.AgentInput,
            userId,
            null,
            taskId);
        var legacyKnowledgeBase = CreateUploadRecord(
            UploadRecordScope.AgentInput,
            userId,
            null,
            taskId);
        SetUploadRecordProperty(failedSession, nameof(UploadRecord.Status), UploadRecordStatus.Failed);
        SetUploadRecordProperty(deletedAgent, nameof(UploadRecord.Status), UploadRecordStatus.Deleted);
        SetUploadRecordProperty(
            legacyKnowledgeBase,
            nameof(UploadRecord.Scope),
            UploadRecordScope.KnowledgeBase);

        var byId = new UploadRecordByIdForUserSpec(failedSession.Id, userId)
            .FilterCondition!
            .Compile();
        var byIds = new UploadRecordsByIdsForUserSpec(
                [activeSession.Id, failedSession.Id, deletedAgent.Id, legacyKnowledgeBase.Id],
                userId)
            .FilterCondition!
            .Compile();
        var bySession = new UploadRecordsBySessionForUserSpec(sessionId, userId)
            .FilterCondition!
            .Compile();
        var byTask = new UploadRecordsByAgentTaskForUserSpec(taskId, userId)
            .FilterCondition!
            .Compile();

        byId(failedSession).Should().BeFalse();
        byIds(activeSession).Should().BeTrue();
        byIds(failedSession).Should().BeFalse();
        byIds(deletedAgent).Should().BeFalse();
        byIds(legacyKnowledgeBase).Should().BeFalse();
        bySession(activeSession).Should().BeTrue();
        bySession(failedSession).Should().BeFalse();
        byTask(deletedAgent).Should().BeFalse();
        byTask(legacyKnowledgeBase).Should().BeFalse();
    }

    private static UploadRecord CreateUploadRecord(
        UploadRecordScope scope,
        Guid userId,
        SessionId? sessionId,
        AgentTaskId? taskId)
    {
        return new UploadRecord(
            scope,
            userId,
            sessionId,
            taskId,
            "input.txt",
            "text/plain",
            5,
            new string('a', 64),
            $"uploads/{Guid.NewGuid():N}/input.txt",
            DateTimeOffset.UtcNow);
    }

    private static void SetUploadRecordProperty<T>(
        UploadRecord record,
        string propertyName,
        T value)
    {
        var property = typeof(UploadRecord).GetProperty(propertyName);
        property.Should().NotBeNull();
        property!.SetValue(record, value);
    }
}
