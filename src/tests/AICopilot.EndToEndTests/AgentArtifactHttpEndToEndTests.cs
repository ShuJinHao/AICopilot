using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EndToEndTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class AgentArtifactHttpEndToEndTests(CoreAICopilotAppFixture fixture)
    : AgentTaskHttpScenarioTestBase(fixture)
{
    [Fact]
    public async Task ExecutionSnapshotUnavailable_ShouldFailClosedWithoutWorkspaceArtifactOrApproval()
    {
        await AuthenticateAsAdminAsync();

        var templateId = await CreateAgentReportTemplateAsync();
        var session = await PostJsonAsync<CreatedSessionDto>(
            "/api/aigateway/session",
            new { templateId });
        var task = await PostPlanStreamAsync(new
        {
            sessionId = session.Id,
            goal = "Generate a controlled Markdown report without bypassing the execution snapshot boundary.",
            taskType = AgentTaskType.ReportGeneration,
            modelId = (Guid?)null,
            uploadIds = Array.Empty<Guid>(),
            knowledgeBaseIds = Array.Empty<Guid>(),
            dataSourceIds = Array.Empty<Guid>(),
            businessDomains = Array.Empty<string>(),
            requiresDataApproval = false,
            artifactTargets = new[] { "markdown" },
            forceStaticPlanner = true,
            pluginSelectionMode = "BuiltInOnly",
            selectedPluginIds = Array.Empty<Guid>(),
            capabilitySelectionMode = "InferredFromGoal",
            requestedCapabilityCodes = Array.Empty<string>()
        });

        task.Status.Should().Be(nameof(AgentTaskStatus.Draft));
        task.WorkspaceId.Should().BeNull();
        task.WorkspaceCode.Should().BeNull();
        task.IsPlanExecutable.Should().BeFalse();
        task.PlanIntegrityStatus.Should().Be("ValidV2");

        using (var plan = JsonDocument.Parse(task.PlanJson))
        {
            var root = plan.RootElement;
            root.GetProperty("planKind").GetString().Should().Be("PlanDraft");
            root.GetProperty("isExecutable").GetBoolean().Should().BeFalse();
            root.GetProperty("nodes").GetArrayLength().Should().Be(0);
            root.GetProperty("capabilityGaps")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Should()
                .Contain("execution_snapshot_unavailable");
            root.GetProperty("artifactTargets")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Should()
                .Equal("markdown");
        }

        (await GetPendingApprovalsAsync(task.Id)).Should().BeEmpty();

        using (var approveResponse = await _fixture.HttpClient.PostAsJsonAsync(
                   "/api/aigateway/agent/task/approve-plan",
                   new { id = task.Id },
                   JsonOptions))
        {
            approveResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadProblemCodeAsync(approveResponse)).Should().Be(AppProblemCodes.AgentPlanInvalid);
        }

        using (var runResponse = await _fixture.HttpClient.PostAsJsonAsync(
                   "/api/aigateway/agent/task/run",
                   new { id = task.Id },
                   JsonOptions))
        {
            runResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        await using (var dbContext = await CreateAiGatewayDbContextAsync())
        {
            var taskId = new AgentTaskId(task.Id);
            var persisted = await dbContext.AgentTasks
                .Include(item => item.Steps)
                .AsNoTracking()
                .SingleAsync(item => item.Id == taskId);
            persisted.Status.Should().Be(AgentTaskStatus.Draft);
            persisted.WorkspaceId.Should().BeNull();
            persisted.ActiveRunAttemptId.Should().BeNull();
            persisted.Steps.Should().OnlyContain(step =>
                step.Status == AgentStepStatus.Pending ||
                step.Status == AgentStepStatus.WaitingApproval);
            persisted.Steps.Should().OnlyContain(step =>
                !step.StartedAt.HasValue &&
                !step.FinishedAt.HasValue &&
                step.OutputJson == null &&
                step.ErrorMessage == null);

            (await dbContext.ApprovalRequests.AnyAsync(item => item.TaskId == taskId)).Should().BeFalse();
            (await dbContext.ArtifactWorkspaces.AnyAsync(item => item.TaskId == taskId)).Should().BeFalse();
            (await dbContext.AgentTaskRunAttempts.AnyAsync(item => item.TaskId == taskId)).Should().BeFalse();
            (await dbContext.AgentTaskRunQueueItems.AnyAsync(item => item.TaskId == taskId)).Should().BeFalse();
            (await dbContext.ToolExecutionRecords.AnyAsync(item => item.TaskId == taskId)).Should().BeFalse();
        }

        var auditSummary = await GetJsonAsync<List<AgentTaskAuditSummaryDto>>(
            $"/api/aigateway/agent/task/{task.Id}/audit-summary");
        auditSummary.Should().Contain(item => item.ActionCode == "Agent.Plan" && item.Result == "Succeeded");
        auditSummary.Should().NotContain(item =>
            item.ActionCode == "Agent.ToolExecution" ||
            item.ActionCode == "Agent.WorkspaceFinalize" ||
            item.ActionCode == "Agent.ArtifactDownload");

        var timeline = await GetJsonAsync<SessionTimelinePageDto>(
            $"/api/aigateway/session/timeline?sessionId={session.Id}&count=200");
        timeline.Items.Should().ContainSingle(item =>
            item.EventType == nameof(MessageEventType.AgentTaskPlanCreated) &&
            item.AgentTaskId == task.Id &&
            item.AgentTaskStatus == nameof(AgentTaskStatus.Draft));
        timeline.Items.Should().NotContain(item =>
            item.EventType == nameof(MessageEventType.ApprovalRequested) ||
            item.EventType == nameof(MessageEventType.AgentTaskStepStarted) ||
            item.EventType == nameof(MessageEventType.AgentTaskStepCompleted) ||
            item.EventType == nameof(MessageEventType.ArtifactReady) ||
            item.EventType == nameof(MessageEventType.FinalOutputReady));
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        return problem.RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }
}
