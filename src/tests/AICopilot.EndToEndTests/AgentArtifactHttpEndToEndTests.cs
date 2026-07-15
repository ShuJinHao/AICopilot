using System.Net;
using AICopilot.Core.AiGateway.Aggregates.Sessions;

namespace AICopilot.EndToEndTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class AgentArtifactHttpEndToEndTests(CoreAICopilotAppFixture fixture)
    : AgentTaskHttpScenarioTestBase(fixture)
{
    [Fact]
    public async Task FinalizeAndDownload_ShouldExposeArtifactAndAuditTimeline()
    {
        await AuthenticateAsAdminAsync();

        var templateId = await CreateAgentReportTemplateAsync();
        var session = await PostJsonAsync<CreatedSessionDto>(
            "/api/aigateway/session",
            new { templateId });
        var upload = await UploadAiGatewayFileAsync(
            session.Id,
            $"agent-report-{Guid.NewGuid():N}.csv",
            "station,count\nA,2\nB,3\nC,5\n");

        var task = await PostPlanStreamAsync(new
        {
            sessionId = session.Id,
            goal = "Generate a controlled report from the uploaded CSV.",
            skillCode = "artifact_report",
            taskType = 2,
            modelId = (Guid?)null,
            uploadIds = new[] { upload.Id },
            knowledgeBaseIds = Array.Empty<Guid>()
        });

        task.Status.Should().Be("Draft");
        task.WorkspaceCode.Should().BeNull();

        var planApproval = (await GetPendingApprovalsAsync(task.Id))
            .Should()
            .ContainSingle(item => item.Type == "Plan")
            .Subject;
        await ApproveAgentApprovalAsync(planApproval.Id, "Plan approved.");

        task = await PostJsonAsync<AgentTaskDto>(
            "/api/aigateway/agent/task/run",
            new { id = task.Id });
        task.IsRunQueued.Should().BeTrue();
        task.RunQueueStatus.Should().Be("Queued");
        task = await WaitForTaskStatusAsync(task.Id, "WaitingToolApproval");
        task.Status.Should().Be("WaitingToolApproval");
        task.WorkspaceCode.Should().NotBeNullOrWhiteSpace();

        var draftWorkspace = await GetJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{task.WorkspaceCode}");
        AssertArtifactPrefixesExist(draftWorkspace, "source/", "data/");
        AssertArtifactsExist(
            draftWorkspace,
            "charts/chart-data.json",
            "draft/report.md",
            "draft/report.html");
        draftWorkspace.Artifacts.Should().OnlyContain(item => item.Status != "Final");
        draftWorkspace.Artifacts.Should().OnlyContain(item =>
            !item.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase));
        draftWorkspace.Manifest.Select(item => item.ArtifactId)
            .Should()
            .BeEquivalentTo(draftWorkspace.Artifacts.Select(item => item.Id));
        draftWorkspace.Manifest.Should().OnlyContain(item =>
            item.DownloadUrl == $"/api/aigateway/artifact/{item.ArtifactId}/download");

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var pendingApprovals = await GetPendingApprovalsAsync(task.Id);
            if (pendingApprovals.Any(item => item.Type == "FinalOutput"))
            {
                break;
            }

            pendingApprovals.Should().NotBeEmpty(
                "the runtime should pause before each high-risk tool");
            foreach (var approval in pendingApprovals.Where(item => item.Type != "FinalOutput"))
            {
                await ApproveAgentApprovalAsync(approval.Id, $"Approve {approval.TargetName}.");
            }

            task = await WaitForTaskToPauseAsync(task.Id);
        }

        var finalApproval = (await GetPendingApprovalsAsync(task.Id))
            .Should()
            .ContainSingle(item => item.Type == "FinalOutput")
            .Subject;
        finalApproval.WorkspaceCode.Should().Be(task.WorkspaceCode);

        draftWorkspace = await GetJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{task.WorkspaceCode}");
        AssertArtifactsExist(
            draftWorkspace,
            "draft/report.pdf",
            "draft/report.pptx",
            "draft/report.xlsx");
        draftWorkspace.Artifacts.Should().OnlyContain(item => item.Status != "Final");
        draftWorkspace.Artifacts.Should().OnlyContain(item =>
            !item.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase));

        await PostJsonExpectingStatusAsync(
            $"/api/aigateway/workspace/{task.WorkspaceCode}/finalize",
            new { },
            HttpStatusCode.BadRequest);

        await ApproveAgentApprovalAsync(finalApproval.Id, "Final output approved.");

        var finalizedWorkspace = await PostJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{task.WorkspaceCode}/finalize",
            new { });
        finalizedWorkspace.Status.Should().Be("Finalized");
        finalizedWorkspace.Artifacts.Should().NotBeEmpty();
        finalizedWorkspace.Artifacts.Should().OnlyContain(item => item.Status == "Final");
        finalizedWorkspace.Artifacts.Should().OnlyContain(item =>
            item.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase));
        finalizedWorkspace.Manifest.Should().OnlyContain(item =>
            item.Status == "Final" &&
            item.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase));

        (await DownloadArtifactAsync(finalizedWorkspace.Artifacts.First().Id))
            .Should()
            .NotBeEmpty();

        var auditSummary = await GetJsonAsync<List<AgentTaskAuditSummaryDto>>(
            $"/api/aigateway/agent/task/{task.Id}/audit-summary");
        auditSummary.Should().Contain(item => item.ActionCode == "Agent.Plan");
        auditSummary.Should().Contain(item => item.ActionCode == "Agent.ApprovalDecision");
        auditSummary.Should().Contain(item =>
            item.ActionCode == "Agent.ToolExecution" &&
            item.Metadata.ContainsKey("toolName") &&
            item.Metadata["toolName"] == "generate_markdown_report");
        auditSummary.Should().Contain(item => item.ActionCode == "Agent.ArtifactDownload");
        auditSummary.Should().Contain(item =>
            item.ActionCode == "Agent.WorkspaceFinalize" &&
            item.WorkspaceCode == task.WorkspaceCode);
        auditSummary.Should().OnlyContain(item => item.TaskId == task.Id);

        var timelineEvents = await QueryMessageTimelineEventsAsync(session.Id);
        timelineEvents.Select(item => item.Sequence).Should().BeInAscendingOrder();
        timelineEvents.Select(item => item.Sequence).Should().OnlyHaveUniqueItems();

        var taskEvents = timelineEvents
            .Where(item => item.AgentTaskId == task.Id)
            .ToList();
        taskEvents.Should().OnlyContain(item => item.MessageId == null);
        taskEvents.Should().OnlyContain(item => item.PayloadJson == null);
        taskEvents.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.AgentTaskPlanCreated) &&
            item.ApprovalRequestId.HasValue &&
            item.ArtifactWorkspaceId == null);
        taskEvents.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.ApprovalRequested) &&
            item.ApprovalRequestId.HasValue);
        taskEvents.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.ApprovalDecided) &&
            item.ApprovalRequestId.HasValue);
        taskEvents.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.AgentTaskStepStarted) &&
            item.AgentStepId.HasValue);
        taskEvents.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.AgentTaskStepCompleted) &&
            item.AgentStepId.HasValue);
        taskEvents.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.ArtifactReady) &&
            item.ArtifactWorkspaceId == finalizedWorkspace.Id &&
            item.ArtifactId.HasValue);
        taskEvents.Should().ContainSingle(item =>
            item.EventType == nameof(MessageEventType.FinalOutputReady) &&
            item.ArtifactWorkspaceId == finalizedWorkspace.Id);

        var timeline = await GetJsonAsync<SessionTimelinePageDto>(
            $"/api/aigateway/session/timeline?sessionId={session.Id}&count=200");
        timeline.Items.Select(item => item.Sequence).Should().BeInAscendingOrder();
        timeline.Items.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.AgentTaskPlanCreated) &&
            item.AgentTaskId == task.Id &&
            item.AgentTaskStatus == "Completed");
        timeline.Items.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.ApprovalDecided) &&
            item.ApprovalRequestId.HasValue &&
            item.ApprovalStatus == "Approved");
        timeline.Items.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.FinalOutputReady) &&
            item.WorkspaceCode == finalizedWorkspace.WorkspaceCode &&
            item.WorkspaceStatus == "Finalized");
        timeline.Items.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.ArtifactReady) &&
            item.ArtifactStatus == "Final" &&
            item.ArtifactDownloadUrl != null);
    }

    private static void AssertArtifactPrefixesExist(
        ArtifactWorkspaceDto workspace,
        params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            workspace.Artifacts.Should().Contain(item =>
                item.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void AssertArtifactsExist(
        ArtifactWorkspaceDto workspace,
        params string[] relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            workspace.Artifacts.Should().Contain(item => item.RelativePath == relativePath);
        }
    }
}
