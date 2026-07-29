using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Uploads;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AggregateTests;

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
    public void FinalOutputApproval_ShouldRequireAndSealImmutableAuthorityAndDecisionProofs()
    {
        var now = DateTimeOffset.UtcNow;
        var taskId = AgentTaskId.New();
        var proof = CreateFinalOutputProof();

        var unbound = () => new ApprovalRequest(
            taskId,
            AgentApprovalType.FinalOutput,
            proof.WorkspaceCode,
            Guid.NewGuid(),
            now);
        unbound.Should().Throw<ArgumentException>();

        var approval = ApprovalRequest.CreateFinalOutput(
            taskId,
            Guid.NewGuid(),
            now,
            proof);
        approval.HasValidFinalOutputProof().Should().BeTrue();
        approval.FinalOutputProofDigest.Should().MatchRegex("^[0-9a-f]{64}$");
        approval.FinalOutputDecisionProofDigest.Should().BeNull();

        approval.Approve(
            Guid.NewGuid(),
            "immutable final-output decision",
            now.AddSeconds(1));

        approval.HasValidFinalOutputProof().Should().BeTrue();
        approval.HasValidFinalOutputDecisionProof().Should().BeTrue();
        approval.FinalOutputDecisionProofDigest.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void FinalOutputApprovalProof_ShouldSurvivePostgresPrecisionAndRejectCreationTimeDrift()
    {
        var createdAt = DateTimeOffset.UtcNow.AddTicks(7);
        var decidedAt = createdAt.AddSeconds(1).AddTicks(3);
        var approval = ApprovalRequest.CreateFinalOutput(
            AgentTaskId.New(),
            Guid.NewGuid(),
            createdAt,
            CreateFinalOutputProof());
        approval.Approve(Guid.NewGuid(), "precision round-trip", decidedAt);

        SetApprovalRequestProperty(
            approval,
            nameof(ApprovalRequest.CreatedAt),
            ToPostgresMicrosecond(createdAt));
        SetApprovalRequestProperty(
            approval,
            nameof(ApprovalRequest.ApprovedAt),
            (DateTimeOffset?)ToPostgresMicrosecond(decidedAt));
        approval.HasValidFinalOutputProof().Should().BeTrue();
        approval.HasValidFinalOutputDecisionProof().Should().BeTrue();

        SetApprovalRequestProperty(
            approval,
            nameof(ApprovalRequest.CreatedAt),
            ToPostgresMicrosecond(createdAt).AddMilliseconds(1));
        approval.HasValidFinalOutputProof().Should().BeFalse();
        approval.HasValidFinalOutputDecisionProof().Should().BeFalse();
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("workspace-code")]
    [InlineData("final-step")]
    [InlineData("attempt")]
    [InlineData("node")]
    [InlineData("task-fence")]
    [InlineData("node-fence")]
    [InlineData("evidence")]
    [InlineData("manifest")]
    [InlineData("artifact-bindings")]
    public void FinalOutputApproval_ShouldRejectEveryAuthorityTupleDrift(string drift)
    {
        var proof = CreateFinalOutputProof();
        var approval = ApprovalRequest.CreateFinalOutput(
            AgentTaskId.New(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            proof);
        var drifted = drift switch
        {
            "workspace" => proof with { WorkspaceId = ArtifactWorkspaceId.New() },
            "workspace-code" => proof with { WorkspaceCode = "ws_drifted" },
            "final-step" => proof with { FinalStepId = AgentStepId.New() },
            "attempt" => proof with { ActiveRunAttemptId = AgentTaskRunAttemptId.New() },
            "node" => proof with { FinalNodeRunId = AgentNodeRunId.New() },
            "task-fence" => proof with { TaskFencingToken = proof.TaskFencingToken + 1 },
            "node-fence" => proof with { NodeFencingToken = proof.NodeFencingToken + 1 },
            "evidence" => proof with { EvidenceSetDigest = new string('c', 64) },
            "manifest" => proof with { ManifestDigest = new string('d', 64) },
            "artifact-bindings" => proof with { ArtifactBindingDigest = new string('e', 64) },
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };

        approval.MatchesFinalOutputProof(drifted).Should().BeFalse();
        approval.HasValidFinalOutputProof().Should().BeTrue(
            "a caller-provided drift must never mutate the stored proof");
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

    private static FinalOutputApprovalProof CreateFinalOutputProof()
    {
        var bindingJson = AgentCanonicalJsonV1.Canonicalize(JsonSerializer.Serialize(
            new[]
            {
                new FinalOutputApprovalArtifactBinding(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Version: 1,
                    SourceRelativePath: "draft/report.md",
                    FileSize: 7,
                    MimeType: "text/markdown",
                    Sha256: Convert.ToHexString(
                            SHA256.HashData("report"u8.ToArray()))
                        .ToLowerInvariant())
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return new FinalOutputApprovalProof(
            ArtifactWorkspaceId.New(),
            "ws_final_output",
            AgentStepId.New(),
            AgentTaskRunAttemptId.New(),
            AgentNodeRunId.New(),
            TaskFencingToken: 3,
            NodeFencingToken: 2,
            EvidenceSetDigest: new string('a', 64),
            ManifestDigest: new string('b', 64),
            bindingJson,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bindingJson)))
                .ToLowerInvariant());
    }

    [Fact]
    public void AgentStep_ShouldPreserveLargeStructuredOutputWithoutSilentTruncation()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            Guid.NewGuid(),
            "Large output",
            "Preserve complete structured output",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            "{}",
            now);
        var step = task.AddStep(
            "Generate",
            "Generate structured output",
            AgentStepType.ArtifactGeneration,
            "generate_chart_data",
            false,
            now);
        var output = $"{{\"value\":\"{new string('x', 32_000)}\"}}";

        step.Start(now);
        step.Complete(output, now.AddSeconds(1));

        step.OutputJson.Should().Be(output);
        step.OutputJson!.Length.Should().BeGreaterThan(16_000);
    }

    [Fact]
    public void AgentStep_ShouldRejectOversizeUtf8InputWithoutTruncatingMultibytePayload()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            Guid.NewGuid(),
            "Input limit",
            "Reject oversize input",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            "{}",
            now);
        var overLimit = $"{{\"value\":\"{new string('界', 2_667)}\"}}";

        var action = () => task.AddStep(
            "Analyze",
            "Analyze input",
            AgentStepType.Analysis,
            "analyze",
            false,
            now,
            overLimit);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*UTF-8 bytes*8000*node-tool-input-policy:v1*");
        task.Steps.Should().BeEmpty();
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

    private static void SetApprovalRequestProperty<T>(
        ApprovalRequest approval,
        string propertyName,
        T value)
    {
        var property = typeof(ApprovalRequest).GetProperty(propertyName);
        property.Should().NotBeNull();
        property!.SetValue(approval, value);
    }

    private static DateTimeOffset ToPostgresMicrosecond(DateTimeOffset value)
    {
        var utcTicks = value.UtcDateTime.Ticks;
        return new DateTimeOffset(utcTicks - utcTicks % 10, TimeSpan.Zero);
    }
}
