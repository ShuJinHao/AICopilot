using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AICopilot.AgentWorkflowTestKit;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.HttpIntegrationTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class AgentApprovalPermissionHardeningTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CoreAICopilotAppFixture _fixture;
    private HttpClient? downstreamClient;

    private HttpClient Client => downstreamClient ?? _fixture.HttpClient;

    public AgentApprovalPermissionHardeningTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UserRole_ShouldRejectLegacyWorkspaceReadySubmit_AndCannotApproveOrFinalize()
    {
        await AuthenticateAsAdminAsync();
        var owner = await CreateUserAsync($"approval-owner-{Guid.NewGuid():N}", "User");
        var seeded = await SeedWorkspaceReadyTaskAsync(Guid.Parse(owner.UserId), includeToolApproval: true);

        await AuthenticateAsync(owner.UserName, "Password123!");

        using (var submitResponse = await Client.PostAsJsonAsync(
                   $"/api/aigateway/workspace/{seeded.WorkspaceCode}/submit-final-review",
                   new { },
                   JsonOptions))
        {
            submitResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadJsonAsync<ProblemDetailsDto>(submitResponse)).Code
                .Should().Be("agent_finalization_state_conflict");
        }

        var task = await GetJsonAsync<AgentTaskDto>($"/api/aigateway/agent/task?id={seeded.TaskId}");
        task.Status.Should().Be("WorkspaceReady");
        task.CanSubmitFinalReview.Should().BeFalse();
        task.CanApproveFinal.Should().BeFalse();
        task.CanFinalizeWorkspace.Should().BeFalse();

        await AssertApprovalForbiddenAsync(
            seeded.ToolApprovalId!.Value,
            approve: true,
            "AiGateway.ApproveAgentToolCall");
        await AssertApprovalForbiddenAsync(
            seeded.ToolApprovalId.Value,
            approve: false,
            "AiGateway.ApproveAgentToolCall");

        var finalTask = await SeedWaitingFinalApprovalTaskAsync(Guid.Parse(owner.UserId));
        await AssertApprovalForbiddenAsync(
            finalTask.FinalApprovalId!.Value,
            approve: true,
            "AiGateway.ApproveFinalOutput");

        using var finalizeResponse = await Client.PostAsJsonAsync(
            $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
            new { },
            JsonOptions);
        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var finalizeProblem = await ReadJsonAsync<ProblemDetailsDto>(finalizeResponse);
        finalizeProblem.Code.Should().Be("missing_permission");
        finalizeProblem.MissingPermissions.Should().Contain("AiGateway.FinalizeWorkspace");
    }

    [Fact]
    public async Task PrivilegedApprover_ShouldCrossUserApproveToolFinalOutput_AndFinalize()
    {
        downstreamClient = await _fixture.GetDownstreamRuntimeHarnessClientAsync();
        await AuthenticateAsAdminAsync();
        var owner = await CreateUserAsync($"approval-owner-{Guid.NewGuid():N}", "User");
        var role = await CreateRoleAsync(
            $"ApprovalApprover-{Guid.NewGuid():N}",
            [
                "AiGateway.GetAgentTask",
                "AiGateway.ApproveAgentToolCall",
                "AiGateway.ApproveFinalOutput",
                "AiGateway.FinalizeWorkspace"
            ]);
        var approver = await CreateUserAsync($"approval-approver-{Guid.NewGuid():N}", role.RoleName);

        var toolTask = await SeedWaitingToolApprovalTaskAsync(Guid.Parse(owner.UserId));
        var finalTask = await SeedWaitingFinalApprovalTaskAsync(Guid.Parse(owner.UserId));

        await AuthenticateAsync(approver.UserName, "Password123!");

        var pending = await GetJsonAsync<List<AgentApprovalRequestDto>>("/api/aigateway/agent/approval/pending");
        pending.Should().Contain(item => item.Id == toolTask.ToolApprovalId);
        pending.Should().Contain(item => item.Id == finalTask.FinalApprovalId);

        var approvedTool = await PostJsonAsync<AgentApprovalRequestDto>(
            $"/api/aigateway/agent/approval/{toolTask.ToolApprovalId}/approve",
            new { comment = "cross-user tool approved" });
        approvedTool.Type.Should().Be("ToolCall");
        approvedTool.Status.Should().Be("Approved");

        var workspace = await GetJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{finalTask.WorkspaceCode}");
        workspace.Artifacts.Should().ContainSingle(item => item.RelativePath == "draft/report.md");

        using (var pendingFinalizeResponse = await Client.PostAsJsonAsync(
                   $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
                   new { },
                   JsonOptions))
        {
            pendingFinalizeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        var artifactId = workspace.Artifacts.Single(item => item.RelativePath == "draft/report.md").Id;
        using var downloadResponse = await Client.GetAsync($"/api/aigateway/artifact/{artifactId}/download");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var approvedFinal = await PostJsonAsync<AgentApprovalRequestDto>(
            $"/api/aigateway/agent/approval/{finalTask.FinalApprovalId}/approve",
            new { comment = "cross-user final output approved" });
        approvedFinal.Type.Should().Be("FinalOutput");
        approvedFinal.Status.Should().Be("Approved");

        var finalized = await PostJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
            new { });
        finalized.Status.Should().Be("Finalized");
        finalized.Artifacts.Should().OnlyContain(item => item.Status == "Final");
        finalized.Artifacts.Should().OnlyContain(item => item.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase));

        await AuthenticateAsync(owner.UserName, "Password123!");
        var completedTask = await GetJsonAsync<AgentTaskDto>(
            $"/api/aigateway/agent/task?id={finalTask.TaskId}");
        completedTask.Status.Should().Be(nameof(AgentTaskStatus.Completed));
        completedTask.Steps.Single(step => step.ToolCode == "finalize_artifacts")
            .Status.Should().Be(nameof(AgentStepStatus.Completed));
        await using var dbContext = await CreateAiGatewayDbContextAsync();
        var persistedTask = await dbContext.AgentTasks
            .Include(item => item.Steps)
            .SingleAsync(item => item.Id == new AgentTaskId(finalTask.TaskId));
        persistedTask.Steps.Single(step => step.ToolCode == "finalize_artifacts")
            .OutputJson.Should().Be(
                """{"resultType":"finalization-checkpoint","status":"finalized"}""");
        persistedTask.ActiveRunAttemptId.Should().BeNull();
        persistedTask.RunLeaseId.Should().BeNull();
        persistedTask.RunLeaseOwner.Should().BeNull();
        persistedTask.RunLeaseExpiresAt.Should().BeNull();
        var persistedAttempt = await dbContext.AgentTaskRunAttempts
            .SingleAsync(item => item.Id == new AgentTaskRunAttemptId(finalTask.RunAttemptId!.Value));
        persistedAttempt.Status.Should().Be(AgentTaskRunAttemptStatus.Succeeded);
        persistedAttempt.IsTerminal.Should().BeTrue();
        persistedAttempt.LeaseId.Should().BeNull();
        persistedAttempt.LeaseOwner.Should().BeNull();
        persistedAttempt.LeaseExpiresAt.Should().BeNull();
        var finalApprovalCount = await dbContext.ApprovalRequests.CountAsync(item =>
            item.TaskId == new AgentTaskId(finalTask.TaskId) &&
            item.ApprovalType == AgentApprovalType.FinalOutput);
        finalApprovalCount.Should().Be(
            1,
            "approval, resume, and finalization must not create duplicate checkpoints");
        var toolExecutionCount = await dbContext.ToolExecutionRecords.CountAsync(item =>
            item.TaskId == new AgentTaskId(finalTask.TaskId));
        toolExecutionCount.Should().Be(0, "finalize_artifacts is never dispatched as a provider tool");

        var auditsBeforeRepeat = await GetJsonAsync<List<AgentTaskAuditSummaryDto>>(
            $"/api/aigateway/agent/task/{finalTask.TaskId}/audit-summary");
        auditsBeforeRepeat.Count(item => item.ActionCode == "Agent.WorkspaceFinalize" && item.Result == "Succeeded")
            .Should().Be(1);
        auditsBeforeRepeat.Count(item => item.ActionCode == "Agent.ApprovalDecision" && item.Result == "Succeeded")
            .Should().Be(1);
        await AuthenticateAsync(approver.UserName, "Password123!");
        var finalizedAgain = await PostJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
            new { });
        finalizedAgain.Status.Should().Be("Finalized");
        await AuthenticateAsync(owner.UserName, "Password123!");
        var auditsAfterRepeat = await GetJsonAsync<List<AgentTaskAuditSummaryDto>>(
            $"/api/aigateway/agent/task/{finalTask.TaskId}/audit-summary");
        auditsAfterRepeat.Should().BeEquivalentTo(auditsBeforeRepeat, options => options.WithStrictOrdering());

        await dbContext.Entry(persistedTask).ReloadAsync();
        (await dbContext.ApprovalRequests.CountAsync(item =>
                item.TaskId == new AgentTaskId(finalTask.TaskId) &&
                item.ApprovalType == AgentApprovalType.FinalOutput))
            .Should().Be(finalApprovalCount);
        (await dbContext.ToolExecutionRecords.CountAsync(item =>
                item.TaskId == new AgentTaskId(finalTask.TaskId)))
            .Should().Be(toolExecutionCount);

        await AuthenticateAsync(approver.UserName, "Password123!");
        var persistedFinalStep = persistedTask.Steps.Single(step => step.ToolCode == "finalize_artifacts");
        dbContext.Entry(persistedFinalStep)
            .Property(nameof(AgentStep.ErrorMessage)).CurrentValue = "stale terminal failure";
        await dbContext.SaveChangesAsync();
        using (var staleStepResponse = await Client.PostAsJsonAsync(
                   $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
                   new { },
                   JsonOptions))
        {
            staleStepResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadJsonAsync<ProblemDetailsDto>(staleStepResponse)).Code
                .Should().Be("agent_finalization_state_conflict");
        }

        dbContext.Entry(persistedFinalStep)
            .Property(nameof(AgentStep.ErrorMessage)).CurrentValue = null;
        var completedAt = persistedAttempt.CompletedAt;
        completedAt.Should().NotBeNull();
        dbContext.Entry(persistedAttempt)
            .Property(nameof(AgentTaskRunAttempt.CompletedAt)).CurrentValue = null;
        await dbContext.SaveChangesAsync();
        using (var missingCompletedAtResponse = await Client.PostAsJsonAsync(
                   $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
                   new { },
                   JsonOptions))
        {
            missingCompletedAtResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadJsonAsync<ProblemDetailsDto>(missingCompletedAtResponse)).Code
                .Should().Be("agent_finalization_state_conflict");
        }

        dbContext.Entry(persistedAttempt)
            .Property(nameof(AgentTaskRunAttempt.CompletedAt)).CurrentValue = completedAt;
        dbContext.Entry(persistedAttempt)
            .Property(nameof(AgentTaskRunAttempt.FailureCode)).CurrentValue = "stale_failure";
        await dbContext.SaveChangesAsync();
        using var staleFailureResponse = await Client.PostAsJsonAsync(
            $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
            new { },
            JsonOptions);
        staleFailureResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync<ProblemDetailsDto>(staleFailureResponse)).Code
            .Should().Be("agent_finalization_state_conflict");

        dbContext.Entry(persistedAttempt)
            .Property(nameof(AgentTaskRunAttempt.FailureCode)).CurrentValue = null;
        var taskCompletedAt = persistedTask.CompletedAt;
        var finalSummary = persistedTask.FinalSummary;
        taskCompletedAt.Should().NotBeNull();
        finalSummary.Should().NotBeNullOrWhiteSpace();
        dbContext.Entry(persistedTask)
            .Property(nameof(AgentTask.CompletedAt)).CurrentValue = null;
        await dbContext.SaveChangesAsync();
        using (var missingTaskCompletedAtResponse = await Client.PostAsJsonAsync(
                   $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
                   new { },
                   JsonOptions))
        {
            missingTaskCompletedAtResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadJsonAsync<ProblemDetailsDto>(missingTaskCompletedAtResponse)).Code
                .Should().Be("agent_finalization_state_conflict");
        }

        dbContext.Entry(persistedTask)
            .Property(nameof(AgentTask.CompletedAt)).CurrentValue = taskCompletedAt;
        dbContext.Entry(persistedTask)
            .Property(nameof(AgentTask.FinalSummary)).CurrentValue = null;
        await dbContext.SaveChangesAsync();
        using var missingFinalSummaryResponse = await Client.PostAsJsonAsync(
            $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
            new { },
            JsonOptions);
        missingFinalSummaryResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync<ProblemDetailsDto>(missingFinalSummaryResponse)).Code
            .Should().Be("agent_finalization_state_conflict");

        dbContext.Entry(persistedTask)
            .Property(nameof(AgentTask.FinalSummary)).CurrentValue = finalSummary;
        var persistedWorkspace = await dbContext.ArtifactWorkspaces
            .Include(item => item.Artifacts)
            .SingleAsync(item => item.WorkspaceCode == finalTask.WorkspaceCode);
        var persistedArtifact = persistedWorkspace.Artifacts.Single();
        var artifactFinalizedAt = persistedArtifact.FinalizedAt;
        artifactFinalizedAt.Should().NotBeNull();
        dbContext.Entry(persistedArtifact)
            .Property(nameof(Artifact.FinalizedAt)).CurrentValue = null;
        await dbContext.SaveChangesAsync();
        using (var missingArtifactFinalizedAtResponse = await Client.PostAsJsonAsync(
                   $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
                   new { },
                   JsonOptions))
        {
            missingArtifactFinalizedAtResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadJsonAsync<ProblemDetailsDto>(missingArtifactFinalizedAtResponse)).Code
                .Should().Be("agent_finalization_state_conflict");
        }

        dbContext.Entry(persistedArtifact)
            .Property(nameof(Artifact.FinalizedAt)).CurrentValue = artifactFinalizedAt;
        var persistedApproval = await dbContext.ApprovalRequests.SingleAsync(item =>
            item.Id == new ApprovalRequestId(finalTask.FinalApprovalId!.Value));
        persistedApproval.ApprovedAt.Should().NotBeNull();
        var producerStep = persistedTask.Steps.Single(step =>
            step.Id == persistedArtifact.CreatedByStepId!.Value);
        producerStep.FinishedAt.Should().NotBeNull();
        persistedFinalStep.FinishedAt.Should().NotBeNull();

        var causalCorruptions = new (object Target, string PropertyName, object InvalidValue, object OriginalValue, string Reason)[]
        {
            (
                persistedArtifact,
                nameof(Artifact.FinalizedAt),
                persistedApproval.ApprovedAt!.Value.AddTicks(-1),
                artifactFinalizedAt.Value,
                "approval decision must precede artifact finalization"),
            (
                producerStep,
                nameof(AgentStep.FinishedAt),
                artifactFinalizedAt.Value.AddTicks(1),
                producerStep.FinishedAt!.Value,
                "producer completion must precede artifact finalization"),
            (
                persistedWorkspace,
                nameof(ArtifactWorkspace.UpdatedAt),
                artifactFinalizedAt.Value.AddTicks(-1),
                persistedWorkspace.UpdatedAt,
                "artifact finalization must precede workspace finalization"),
            (
                persistedFinalStep,
                nameof(AgentStep.FinishedAt),
                persistedWorkspace.UpdatedAt.AddTicks(-1),
                persistedFinalStep.FinishedAt!.Value,
                "workspace finalization must precede checkpoint completion"),
            (
                persistedTask,
                nameof(AgentTask.CompletedAt),
                persistedFinalStep.FinishedAt!.Value.AddTicks(-1),
                taskCompletedAt.Value,
                "checkpoint completion must precede task completion"),
            (
                persistedAttempt,
                nameof(AgentTaskRunAttempt.CompletedAt),
                taskCompletedAt.Value.AddTicks(-1),
                completedAt!.Value,
                "task completion must precede latest attempt completion"),
            (
                persistedTask,
                nameof(AgentTask.CompletedAt),
                persistedTask.CreatedAt.AddTicks(-1),
                taskCompletedAt.Value,
                "task completion cannot precede task creation"),
            (
                persistedAttempt,
                nameof(AgentTaskRunAttempt.CompletedAt),
                persistedAttempt.StartedAt.AddTicks(-1),
                completedAt.Value,
                "attempt completion cannot precede attempt start")
        };

        foreach (var corruption in causalCorruptions)
        {
            dbContext.Entry(corruption.Target)
                .Property(corruption.PropertyName).CurrentValue = corruption.InvalidValue;
            await dbContext.SaveChangesAsync();
            using var causalConflictResponse = await Client.PostAsJsonAsync(
                $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
                new { },
                JsonOptions);
            causalConflictResponse.StatusCode.Should().Be(
                HttpStatusCode.BadRequest,
                corruption.Reason);
            (await ReadJsonAsync<ProblemDetailsDto>(causalConflictResponse)).Code
                .Should().Be("agent_finalization_state_conflict", corruption.Reason);
            dbContext.Entry(corruption.Target)
                .Property(corruption.PropertyName).CurrentValue = corruption.OriginalValue;
            await dbContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Finalize_ShouldFailClosedBeforeApprovalAndAfterRejection()
    {
        downstreamClient = await _fixture.GetDownstreamRuntimeHarnessClientAsync();
        await AuthenticateAsAdminAsync();
        var seeded = await SeedWaitingFinalApprovalTaskAsync(Guid.NewGuid());

        using (var pendingResponse = await Client.PostAsJsonAsync(
                   $"/api/aigateway/workspace/{seeded.WorkspaceCode}/finalize",
                   new { },
                   JsonOptions))
        {
            pendingResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadJsonAsync<ProblemDetailsDto>(pendingResponse)).Code
                .Should().Be("approval_pending");
        }

        var rejected = await PostJsonAsync<AgentApprovalRequestDto>(
            $"/api/aigateway/agent/approval/{seeded.FinalApprovalId}/reject",
            new { comment = "reject final output" });
        rejected.Status.Should().Be("Rejected");

        using var rejectedResponse = await Client.PostAsJsonAsync(
            $"/api/aigateway/workspace/{seeded.WorkspaceCode}/finalize",
            new { },
            JsonOptions);
        rejectedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync<ProblemDetailsDto>(rejectedResponse)).Code
            .Should().Be("agent_approval_rejected");

        await using var dbContext = await CreateAiGatewayDbContextAsync();
        var workspace = await dbContext.ArtifactWorkspaces
            .SingleAsync(item => item.WorkspaceCode == seeded.WorkspaceCode);
        workspace.Status.Should().NotBe(ArtifactWorkspaceStatus.Finalized);
        (await dbContext.ToolExecutionRecords.CountAsync(item =>
                item.TaskId == new AgentTaskId(seeded.TaskId)))
            .Should().Be(0);
    }

    [Fact]
    public async Task FinalizationConsumers_ShouldFailClosedForCompetingPendingApproval()
    {
        await AuthenticateAsAdminAsync();
        var owner = await CreateUserAsync($"finalization-owner-{Guid.NewGuid():N}", "User");
        var seeded = await SeedWaitingFinalApprovalTaskAsync(Guid.Parse(owner.UserId));

        await using (var dbContext = await CreateAiGatewayDbContextAsync())
        {
            var task = await dbContext.AgentTasks
                .Include(item => item.Steps)
                .SingleAsync(item => item.Id == new AgentTaskId(seeded.TaskId));
            var generationStep = task.Steps.Single(step => step.ToolCode == "generate_markdown_report");
            dbContext.ApprovalRequests.Add(new ApprovalRequest(
                task.Id,
                AgentApprovalType.ToolCall,
                generationStep.Id.Value.ToString("D"),
                task.UserId,
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        await AuthenticateAsync(owner.UserName, "Password123!");
        var taskDto = await GetJsonAsync<AgentTaskDto>($"/api/aigateway/agent/task?id={seeded.TaskId}");
        taskDto.CanSubmitFinalReview.Should().BeFalse();
        taskDto.CanApproveFinal.Should().BeFalse();
        taskDto.CanFinalizeWorkspace.Should().BeFalse();
        using (var submitResponse = await Client.PostAsJsonAsync(
                   $"/api/aigateway/workspace/{seeded.WorkspaceCode}/submit-final-review",
                   new { },
                   JsonOptions))
        {
            submitResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadJsonAsync<ProblemDetailsDto>(submitResponse)).Code
                .Should().Be("agent_finalization_state_conflict");
        }

        await AuthenticateAsAdminAsync();
        using (var approveResponse = await Client.PostAsJsonAsync(
                   $"/api/aigateway/agent/approval/{seeded.FinalApprovalId}/approve",
                   new { comment = "must fail closed" },
                   JsonOptions))
        {
            approveResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadJsonAsync<ProblemDetailsDto>(approveResponse)).Code
                .Should().Be("agent_approval_state_conflict");
        }

        await using (var dbContext = await CreateAiGatewayDbContextAsync())
        {
            var task = await dbContext.AgentTasks
                .Include(item => item.Steps)
                .SingleAsync(item => item.Id == new AgentTaskId(seeded.TaskId));
            var finalApproval = await dbContext.ApprovalRequests.SingleAsync(item =>
                item.Id == new ApprovalRequestId(seeded.FinalApprovalId!.Value));
            var attempt = await dbContext.AgentTaskRunAttempts.SingleAsync(item =>
                item.Id == new AgentTaskRunAttemptId(seeded.RunAttemptId!.Value));
            finalApproval.Status.Should().Be(AgentApprovalStatus.Pending);
            task.Status.Should().Be(AgentTaskStatus.WaitingFinalApproval);
            task.Steps.Single(step => step.ToolCode == "finalize_artifacts")
                .Status.Should().Be(AgentStepStatus.WaitingApproval);
            attempt.Status.Should().Be(AgentTaskRunAttemptStatus.WaitingApproval);

            finalApproval.Approve(Guid.NewGuid(), "direct corruption fixture", DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        using var finalizeResponse = await Client.PostAsJsonAsync(
            $"/api/aigateway/workspace/{seeded.WorkspaceCode}/finalize",
            new { },
            JsonOptions);
        finalizeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync<ProblemDetailsDto>(finalizeResponse)).Code
            .Should().Be("agent_finalization_state_conflict");
    }

    [Fact]
    public async Task Finalize_ShouldRejectForeignApprovalIdentityAndDuplicateFinalOutput()
    {
        downstreamClient = await _fixture.GetDownstreamRuntimeHarnessClientAsync();
        await AuthenticateAsAdminAsync();
        var foreignIdentity = await SeedWaitingFinalApprovalTaskAsync(Guid.NewGuid());
        await using (var dbContext = await CreateAiGatewayDbContextAsync())
        {
            var approval = await dbContext.ApprovalRequests.SingleAsync(item =>
                item.Id == new ApprovalRequestId(foreignIdentity.FinalApprovalId!.Value));
            dbContext.Entry(approval).Property(nameof(ApprovalRequest.RequestedBy)).CurrentValue = Guid.NewGuid();
            await dbContext.SaveChangesAsync();
        }

        using (var identityResponse = await Client.PostAsJsonAsync(
                   $"/api/aigateway/workspace/{foreignIdentity.WorkspaceCode}/finalize",
                   new { },
                   JsonOptions))
        {
            identityResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadJsonAsync<ProblemDetailsDto>(identityResponse)).Code
                .Should().Be("agent_finalization_state_conflict");
        }

        var duplicate = await SeedWaitingFinalApprovalTaskAsync(Guid.NewGuid());
        await using (var dbContext = await CreateAiGatewayDbContextAsync())
        {
            var task = await dbContext.AgentTasks.SingleAsync(item =>
                item.Id == new AgentTaskId(duplicate.TaskId));
            var historical = new ApprovalRequest(
                task.Id,
                AgentApprovalType.FinalOutput,
                duplicate.WorkspaceCode!,
                task.UserId,
                DateTimeOffset.UtcNow);
            historical.Reject(Guid.NewGuid(), "historical duplicate", DateTimeOffset.UtcNow);
            dbContext.ApprovalRequests.Add(historical);
            await dbContext.SaveChangesAsync();
        }

        using var duplicateResponse = await Client.PostAsJsonAsync(
            $"/api/aigateway/workspace/{duplicate.WorkspaceCode}/finalize",
            new { },
            JsonOptions);
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync<ProblemDetailsDto>(duplicateResponse)).Code
            .Should().Be("agent_finalization_state_conflict");
    }

    [Fact]
    public async Task Finalize_ShouldRejectStalePendingAndApprovedMissingDecisionProof()
    {
        downstreamClient = await _fixture.GetDownstreamRuntimeHarnessClientAsync();
        await AuthenticateAsAdminAsync();
        var stalePending = await SeedWaitingFinalApprovalTaskAsync(Guid.NewGuid());
        await using (var dbContext = await CreateAiGatewayDbContextAsync())
        {
            var approval = await dbContext.ApprovalRequests.SingleAsync(item =>
                item.Id == new ApprovalRequestId(stalePending.FinalApprovalId!.Value));
            dbContext.Entry(approval)
                .Property(nameof(ApprovalRequest.ApprovalComment)).CurrentValue = "stale decision";
            await dbContext.SaveChangesAsync();
        }

        using (var stalePendingResponse = await Client.PostAsJsonAsync(
                   $"/api/aigateway/workspace/{stalePending.WorkspaceCode}/finalize",
                   new { },
                   JsonOptions))
        {
            stalePendingResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ReadJsonAsync<ProblemDetailsDto>(stalePendingResponse)).Code
                .Should().Be("agent_finalization_state_conflict");
        }

        var approvedWithoutProof = await SeedWaitingFinalApprovalTaskAsync(Guid.NewGuid());
        await using (var dbContext = await CreateAiGatewayDbContextAsync())
        {
            var approval = await dbContext.ApprovalRequests.SingleAsync(item =>
                item.Id == new ApprovalRequestId(approvedWithoutProof.FinalApprovalId!.Value));
            dbContext.Entry(approval)
                .Property(nameof(ApprovalRequest.Status)).CurrentValue = AgentApprovalStatus.Approved;
            await dbContext.SaveChangesAsync();
        }

        using var missingProofResponse = await Client.PostAsJsonAsync(
            $"/api/aigateway/workspace/{approvedWithoutProof.WorkspaceCode}/finalize",
            new { },
            JsonOptions);
        missingProofResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync<ProblemDetailsDto>(missingProofResponse)).Code
            .Should().Be("agent_finalization_state_conflict");

        await using var verificationContext = await CreateAiGatewayDbContextAsync();
        var workspaces = await verificationContext.ArtifactWorkspaces
            .Include(item => item.Artifacts)
            .Where(item =>
                item.WorkspaceCode == stalePending.WorkspaceCode ||
                item.WorkspaceCode == approvedWithoutProof.WorkspaceCode)
            .ToArrayAsync();
        workspaces.Should().HaveCount(2);
        workspaces.Should().OnlyContain(item => item.Status == ArtifactWorkspaceStatus.Active);
        workspaces.SelectMany(item => item.Artifacts)
            .Should().OnlyContain(item => item.Status == ArtifactStatus.Draft);
    }

    [Fact]
    public async Task Finalize_ShouldPreserveFinalSubpaths_WhenDraftFileNamesCollide()
    {
        downstreamClient = await _fixture.GetDownstreamRuntimeHarnessClientAsync();
        await AuthenticateAsAdminAsync();
        var finalTask = await CreateSeededTaskAsync(
            Guid.NewGuid(),
            markWaitingFinalApproval: true,
            includeToolApproval: false,
            [
                new SeedArtifactInput(ArtifactType.Chart, "charts/report.json", """{"source":"charts"}""", "application/json"),
                new SeedArtifactInput(ArtifactType.Markdown, "draft/report.json", """{"source":"draft"}""", "application/json")
            ]);

        _ = await PostJsonAsync<AgentApprovalRequestDto>(
            $"/api/aigateway/agent/approval/{finalTask.FinalApprovalId!.Value}/approve",
            new { comment = "final output approved" });

        var finalized = await PostJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{finalTask.WorkspaceCode}/finalize",
            new { });

        var finalPaths = finalized.Artifacts.Select(item => item.RelativePath).ToArray();
        finalPaths.Should().OnlyHaveUniqueItems();
        finalPaths.Should().Contain(["final/charts/report.json", "final/draft/report.json"]);

        var chartArtifact = finalized.Artifacts.Single(item => item.RelativePath == "final/charts/report.json");
        var draftArtifact = finalized.Artifacts.Single(item => item.RelativePath == "final/draft/report.json");
        var chartContent = await DownloadStringAsync($"/api/aigateway/artifact/{chartArtifact.Id}/download");
        var draftContent = await DownloadStringAsync($"/api/aigateway/artifact/{draftArtifact.Id}/download");
        chartContent.Should().Contain("\"charts\"");
        draftContent.Should().Contain("\"draft\"");
    }

    private async Task<SeededAgentTask> SeedWorkspaceReadyTaskAsync(Guid ownerId, bool includeToolApproval)
    {
        var seeded = await CreateSeededTaskAsync(ownerId, markWaitingFinalApproval: false, includeToolApproval);
        return seeded;
    }

    private async Task<SeededAgentTask> SeedWaitingToolApprovalTaskAsync(Guid ownerId)
    {
        var now = DateTimeOffset.UtcNow;
        await using var dbContext = await CreateAiGatewayDbContextAsync();
        var planStep = new AgentPlanV2TestStep(
            "Generate PDF",
            "Approval-gated tool step.",
            AgentStepType.ArtifactGeneration,
            "generate_pdf",
            RequiresApproval: true);
        var planJson = AgentPlanV2TestData.CreateCanonicalBuiltInPlanDraft(
            [planStep],
            AgentTaskType.DataAnalysis,
            skillCode: null,
            knowledgeBaseIds: null);
        AgentPlanV2TestData.AssertCanonicalBuiltInPlanIdentity(
            planJson,
            AgentTaskType.DataAnalysis,
            AgentTaskRiskLevel.Low,
            [planStep]);
        var task = new AgentTask(
            new SessionId(Guid.NewGuid()),
            ownerId,
            "Tool approval permission hardening",
            "Tool approval permission hardening",
            AgentTaskType.DataAnalysis,
            AgentTaskRiskLevel.Low,
            null,
            planJson,
            now);
        var step = task.AddStep(
            planStep.Title,
            planStep.Description,
            planStep.StepType,
            planStep.ToolCode,
            planStep.RequiresApproval,
            now);
        task.AddStep(
            "Finalize artifacts",
            "Wait for final output approval before publishing workspace artifacts.",
            AgentStepType.Finalize,
            "finalize_artifacts",
            requiresApproval: true,
            now);
        task.ConfirmExecutablePlan(task.PlanJson, Array.Empty<int>(), now);
        task.ApprovePlan(now);
        task.Start(now);
        task.WaitForToolApproval(now);

        var runAttempt = new AgentTaskRunAttempt(
            task.Id,
            attemptNo: 1,
            AgentTaskRunTriggerType.Manual,
            "integration-tool-approval-fixture",
            now,
            TimeSpan.FromMinutes(5));
        task.BeginRunAttempt(
            runAttempt.Id,
            runAttempt.AttemptNo,
            runAttempt.LeaseId!.Value,
            runAttempt.LeaseOwner!,
            runAttempt.LeaseExpiresAt!.Value,
            now);
        runAttempt.WaitForApproval(now, "Waiting for tool approval.");
        task.ReleaseRunLease(now, clearActiveAttempt: false);

        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.ToolCall,
            step.Id.Value.ToString(),
            ownerId,
            now);

        dbContext.AgentTasks.Add(task);
        dbContext.ApprovalRequests.Add(approval);
        dbContext.AgentTaskRunAttempts.Add(runAttempt);
        await dbContext.SaveChangesAsync();

        return new SeededAgentTask(task.Id.Value, null, approval.Id.Value, null, runAttempt.Id.Value);
    }

    private async Task<SeededAgentTask> SeedWaitingFinalApprovalTaskAsync(Guid ownerId)
    {
        return await CreateSeededTaskAsync(ownerId, markWaitingFinalApproval: true, includeToolApproval: false);
    }

    private async Task<SeededAgentTask> CreateSeededTaskAsync(
        Guid ownerId,
        bool markWaitingFinalApproval,
        bool includeToolApproval,
        IReadOnlyCollection<SeedArtifactInput>? artifactInputs = null)
    {
        var now = DateTimeOffset.UtcNow;
        var workspaceCode = $"ws_approval_{Guid.NewGuid():N}"[..38];
        var workspaceRoot = Path.Combine(GetWorkspaceRoot(), workspaceCode);
        var artifacts = artifactInputs ?? new[]
        {
            new SeedArtifactInput(
                ArtifactType.Markdown,
                "draft/report.md",
                "# Approval permission hardening",
                "text/markdown")
        };
        var artifactFiles = new List<(SeedArtifactInput Artifact, string FullPath)>();
        foreach (var artifact in artifacts)
        {
            var fullPath = Path.Combine(workspaceRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, artifact.Content, Encoding.UTF8);
            artifactFiles.Add((artifact, fullPath));
        }

        await using var dbContext = await CreateAiGatewayDbContextAsync();
        var artifactPlanSteps = artifactFiles
            .Select((artifact, index) => CreateArtifactPlanStep(artifact.Artifact, index))
            .ToArray();
        var requestedPlanSteps = includeToolApproval
            ? new[]
            {
                new AgentPlanV2TestStep(
                    "Generate PDF",
                    "Approval-gated tool step.",
                    AgentStepType.ArtifactGeneration,
                    "generate_pdf",
                    RequiresApproval: true)
            }.Concat(artifactPlanSteps).ToArray()
            : artifactPlanSteps;
        const AgentTaskType taskType = AgentTaskType.ReportGeneration;
        var planJson = AgentPlanV2TestData.CreateCanonicalBuiltInPlanDraft(
            requestedPlanSteps,
            taskType,
            skillCode: null,
            knowledgeBaseIds: null);
        AgentPlanV2TestData.AssertCanonicalBuiltInPlanIdentity(
            planJson,
            taskType,
            AgentTaskRiskLevel.Low,
            requestedPlanSteps);
        var task = new AgentTask(
            new SessionId(Guid.NewGuid()),
            ownerId,
            "Approval permission final output",
            "Approval permission final output",
            taskType,
            AgentTaskRiskLevel.Low,
            null,
            planJson,
            now);
        AgentStep? toolStep = null;
        if (includeToolApproval)
        {
            toolStep = task.AddStep(
                "Generate PDF",
                "Approval-gated tool step.",
                AgentStepType.ArtifactGeneration,
                "generate_pdf",
                requiresApproval: true,
                now);
        }

        var generationSteps = artifactPlanSteps
            .Select(planStep => task.AddStep(
                planStep.Title,
                planStep.Description,
                planStep.StepType,
                planStep.ToolCode,
                planStep.RequiresApproval,
                now))
            .ToArray();

        var finalStep = task.AddStep(
            "Finalize artifacts",
            "Wait for final output approval before publishing workspace artifacts.",
            AgentStepType.Finalize,
            "finalize_artifacts",
            requiresApproval: true,
            now);
        var workspace = new ArtifactWorkspace(
            task.Id,
            workspaceCode,
            workspaceRoot,
            $"/workspaces/{workspaceCode}",
            now);
        for (var index = 0; index < artifactFiles.Count; index++)
        {
            var artifactFile = artifactFiles[index];
            var generationStep = generationSteps[index];
            var artifact = workspace.AddDraftArtifact(
                artifactFile.Artifact.Type,
                Path.GetFileName(artifactFile.Artifact.RelativePath),
                artifactFile.Artifact.RelativePath,
                new FileInfo(artifactFile.FullPath).Length,
                artifactFile.Artifact.MimeType,
                generationStep.Id,
                now);
            generationStep.Start(now);
            generationStep.Complete(JsonSerializer.Serialize(new
            {
                status = "completed",
                resultType = "artifact",
                artifactType = artifactFile.Artifact.Type.ToString().ToLowerInvariant(),
                artifactId = artifact.Id.Value
            }, JsonOptions), now);
        }

        task.AttachWorkspace(workspace.Id, now);
        task.ConfirmExecutablePlan(task.PlanJson, Array.Empty<int>(), now);
        task.ApprovePlan(now);
        task.MarkWorkspaceReady(now);
        if (markWaitingFinalApproval)
        {
            task.WaitForFinalApproval(now);
        }

        ApprovalRequest? toolApproval = null;
        if (toolStep is not null)
        {
            toolApproval = new ApprovalRequest(
                task.Id,
                AgentApprovalType.ToolCall,
                toolStep.Id.Value.ToString(),
                ownerId,
                now);
            dbContext.ApprovalRequests.Add(toolApproval);
        }

        ApprovalRequest? finalApproval = null;
        if (markWaitingFinalApproval)
        {
            finalApproval = new ApprovalRequest(
                task.Id,
                AgentApprovalType.FinalOutput,
                workspace.WorkspaceCode,
                ownerId,
                now);
            dbContext.ApprovalRequests.Add(finalApproval);
        }

        AgentTaskRunAttempt? runAttempt = null;
        if (markWaitingFinalApproval)
        {
            runAttempt = new AgentTaskRunAttempt(
                task.Id,
                attemptNo: 1,
                AgentTaskRunTriggerType.Manual,
                "integration-finalization-fixture",
                now,
                TimeSpan.FromMinutes(5));
            task.BeginRunAttempt(
                runAttempt.Id,
                runAttempt.AttemptNo,
                runAttempt.LeaseId!.Value,
                runAttempt.LeaseOwner!,
                runAttempt.LeaseExpiresAt!.Value,
                now);
            runAttempt.WaitForApproval(now, "Waiting for final output approval.");
            task.ReleaseRunLease(now, clearActiveAttempt: false);
            dbContext.AgentTaskRunAttempts.Add(runAttempt);
        }

        dbContext.AgentTasks.Add(task);
        dbContext.ArtifactWorkspaces.Add(workspace);
        await dbContext.SaveChangesAsync();

        return new SeededAgentTask(
            task.Id.Value,
            workspace.WorkspaceCode,
            toolApproval?.Id.Value,
            finalApproval?.Id.Value,
            runAttempt?.Id.Value);
    }

    private static AgentPlanV2TestStep CreateArtifactPlanStep(
        SeedArtifactInput artifact,
        int index)
    {
        var toolCode = artifact.Type switch
        {
            ArtifactType.Chart => "generate_chart_data",
            ArtifactType.Markdown => "generate_markdown_report",
            ArtifactType.Html => "generate_html_report",
            ArtifactType.Pdf => "generate_pdf",
            ArtifactType.Pptx => "generate_pptx",
            ArtifactType.Xlsx => "generate_xlsx",
            _ => throw new ArgumentOutOfRangeException(
                nameof(artifact),
                artifact.Type,
                "The Plan v2 HTTP fixture only supports canonical artifact generator types.")
        };
        return new AgentPlanV2TestStep(
            $"Generate artifact {index + 1}",
            "Generate the persisted draft artifact for finalization.",
            artifact.Type == ArtifactType.Chart
                ? AgentStepType.ChartGeneration
                : AgentStepType.ArtifactGeneration,
            toolCode,
            RequiresApproval: artifact.Type is ArtifactType.Pdf or ArtifactType.Pptx or ArtifactType.Xlsx);
    }

    private async Task<Guid> GetApprovalIdAsync(Guid taskId, AgentApprovalType approvalType)
    {
        await using var dbContext = await CreateAiGatewayDbContextAsync();
        return await dbContext.ApprovalRequests
            .Where(item => item.TaskId == new AgentTaskId(taskId) && item.ApprovalType == approvalType)
            .Select(item => item.Id.Value)
            .SingleAsync();
    }

    private async Task AssertApprovalForbiddenAsync(Guid approvalId, bool approve, string missingPermission)
    {
        using var response = await Client.PostAsJsonAsync(
            $"/api/aigateway/agent/approval/{approvalId}/{(approve ? "approve" : "reject")}",
            new { comment = "should be forbidden" },
            JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await ReadJsonAsync<ProblemDetailsDto>(response);
        problem.Code.Should().Be("missing_permission");
        problem.MissingPermissions.Should().Contain(missingPermission);
    }

    private async Task<CreatedRoleDto> CreateRoleAsync(string roleName, IReadOnlyCollection<string> permissions)
    {
        return await PostJsonAsync<CreatedRoleDto>("/api/identity/role", new
        {
            roleName,
            permissions
        });
    }

    private async Task<CreatedUserDto> CreateUserAsync(string userName, string roleName)
    {
        return await PostJsonAsync<CreatedUserDto>("/api/identity/user", new
        {
            userName,
            password = "Password123!",
            roleName
        });
    }

    private async Task AuthenticateAsAdminAsync()
    {
        await AuthenticateAsync(_fixture.BootstrapAdminUserName, _fixture.BootstrapAdminPassword);
    }

    private async Task AuthenticateAsync(string userName, string password)
    {
        var result = await PostJsonAsync<LoginUserDto>("/api/identity/login", new
        {
            username = userName,
            password
        });
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", result.Token);
    }

    private async Task<T> GetJsonAsync<T>(string uri)
    {
        using var response = await Client.GetAsync(uri);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"GET '{uri}' failed: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private async Task<T> PostJsonAsync<T>(string uri, object payload)
    {
        using var response = await Client.PostAsJsonAsync(uri, payload, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"POST '{uri}' failed: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private async Task<string> DownloadStringAsync(string uri)
    {
        using var response = await Client.GetAsync(uri);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"GET '{uri}' failed: {body}");
        return body;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<AiGatewayDbContext> CreateAiGatewayDbContextAsync()
    {
        var connectionString = await _fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.AiGateway)
            .Options;

        return new AiGatewayDbContext(options);
    }

    private static string GetWorkspaceRoot()
    {
        return Path.Combine(Path.GetTempPath(), "AICopilotIntegrationTests", "artifact-workspaces");
    }

    private sealed record SeededAgentTask(
        Guid TaskId,
        string? WorkspaceCode,
        Guid? ToolApprovalId,
        Guid? FinalApprovalId,
        Guid? RunAttemptId);

    private sealed record SeedArtifactInput(
        ArtifactType Type,
        string RelativePath,
        string Content,
        string MimeType);

    private sealed record LoginUserDto(string UserName, string Token);

    private sealed record CreatedRoleDto(
        string RoleId,
        string RoleName,
        IReadOnlyCollection<string> Permissions,
        bool IsSystemRole,
        int AssignedUserCount);

    private sealed record CreatedUserDto(
        string UserId,
        string UserName,
        string RoleName,
        bool IsEnabled,
        string Status);

    private sealed record ProblemDetailsDto(
        string? Title,
        string? Detail,
        int? Status,
        string? Code,
        IReadOnlyCollection<string> MissingPermissions);

    private sealed record AgentApprovalRequestDto(
        Guid Id,
        Guid TaskId,
        string? WorkspaceCode,
        string Type,
        string TargetId,
        string TargetName,
        string RiskLevel,
        string Status,
        string? Reason,
        DateTimeOffset RequestedAt,
        DateTimeOffset? DecidedAt,
        Guid? DecidedBy);

    private sealed record ArtifactWorkspaceDto(
        Guid Id,
        string WorkspaceCode,
        Guid TaskId,
        string Status,
        IReadOnlyCollection<ArtifactWorkspaceFileDto> Files,
        IReadOnlyCollection<ArtifactDto> Artifacts);

    private sealed record ArtifactWorkspaceFileDto(
        string Name,
        string RelativePath,
        bool IsDirectory,
        long FileSize,
        DateTimeOffset UpdatedAt);

    private sealed record ArtifactDto(
        Guid Id,
        string Name,
        string Type,
        string Status,
        string RelativePath,
        long FileSize,
        string MimeType,
        int Version,
        DateTimeOffset UpdatedAt,
        string PreviewKind,
        string DownloadUrl,
        int? GeneratedByStepOrder,
        bool RequiresApproval,
        string ApprovalStatus,
        DateTimeOffset? FinalizedAt);
}
