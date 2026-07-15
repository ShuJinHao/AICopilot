using System.Net;
using System.Net.Http.Json;
using AICopilot.Core.AiGateway.Aggregates.Sessions;

namespace AICopilot.EndToEndTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class AgentPlanRejectionEndToEndTests(CoreAICopilotAppFixture fixture)
    : AgentTaskHttpScenarioTestBase(fixture)
{
    [Fact]
    public async Task RejectedPlan_ShouldNotRunToolsOrCreateFinalWorkspace()
    {
        await AuthenticateAsAdminAsync();

        var templateId = await CreateAgentReportTemplateAsync();
        var session = await PostJsonAsync<CreatedSessionDto>(
            "/api/aigateway/session",
            new { templateId });
        var task = await PostPlanStreamAsync(new
        {
            sessionId = session.Id,
            goal = "Create a report that must stop before execution.",
            skillCode = "artifact_report",
            taskType = 2,
            modelId = (Guid?)null,
            uploadIds = Array.Empty<Guid>(),
            knowledgeBaseIds = Array.Empty<Guid>()
        });

        var planApproval = (await GetPendingApprovalsAsync(task.Id))
            .Should()
            .ContainSingle(item => item.Type == "Plan")
            .Subject;
        await RejectAgentApprovalAsync(planApproval.Id, "Plan rejected by operator.");

        using var runResponse = await _fixture.HttpClient.PostAsJsonAsync(
            "/api/aigateway/agent/task/run",
            new { id = task.Id },
            JsonOptions);
        runResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var rejectedTask = await GetJsonAsync<AgentTaskDto>(
            $"/api/aigateway/agent/task?id={task.Id}");
        rejectedTask.Status.Should().Be("Rejected");
        rejectedTask.WorkspaceCode.Should().BeNull();

        var auditSummary = await GetJsonAsync<List<AgentTaskAuditSummaryDto>>(
            $"/api/aigateway/agent/task/{task.Id}/audit-summary");
        auditSummary.Should().Contain(item =>
            item.ActionCode == "Agent.ApprovalDecision" &&
            item.Result == "Rejected");
        auditSummary.Should().NotContain(item => item.ActionCode == "Agent.ToolExecution");

        var timeline = await GetJsonAsync<SessionTimelinePageDto>(
            $"/api/aigateway/session/timeline?sessionId={session.Id}&count=200");
        timeline.Items.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.AgentTaskPlanCreated) &&
            item.AgentTaskId == task.Id &&
            item.AgentTaskStatus == "Rejected");
        timeline.Items.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.ApprovalRequested) &&
            item.ApprovalRequestId == planApproval.Id &&
            item.ApprovalStatus == "Rejected");
        timeline.Items.Should().Contain(item =>
            item.EventType == nameof(MessageEventType.ApprovalDecided) &&
            item.ApprovalRequestId == planApproval.Id &&
            item.ApprovalStatus == "Rejected" &&
            item.ApprovalDecidedAt.HasValue);
    }
}
