using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.BackendTests;

[Collection(CoreBackendTestCollection.Name)]
[Trait("Suite", "Batch5ApprovalHardening")]
[Trait("Runtime", "DockerRequired")]
public sealed class AgentApprovalPermissionHardeningTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AICopilotAppFixture _fixture;

    public AgentApprovalPermissionHardeningTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UserRole_ShouldSubmitFinalReview_ButCannotApproveToolFinalOrFinalize()
    {
        await AuthenticateAsAdminAsync();
        var owner = await CreateUserAsync($"batch5-owner-{Guid.NewGuid():N}", "User");
        var seeded = await SeedWorkspaceReadyTaskAsync(Guid.Parse(owner.UserId), includeToolApproval: true);

        await AuthenticateAsync(owner.UserName, "Password123!");

        var submitted = await PostJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{seeded.WorkspaceCode}/submit-final-review",
            new { });
        submitted.Status.Should().Be("Active");

        var task = await GetJsonAsync<AgentTaskDto>($"/api/aigateway/agent/task?id={seeded.TaskId}");
        task.Status.Should().Be("WaitingFinalApproval");
        task.CanApproveFinal.Should().BeFalse();

        var finalApprovalId = await GetApprovalIdAsync(seeded.TaskId, AgentApprovalType.FinalOutput);
        await AssertApprovalForbiddenAsync(
            seeded.ToolApprovalId!.Value,
            approve: true,
            "AiGateway.ApproveAgentToolCall");
        await AssertApprovalForbiddenAsync(
            seeded.ToolApprovalId.Value,
            approve: false,
            "AiGateway.ApproveAgentToolCall");
        await AssertApprovalForbiddenAsync(
            finalApprovalId,
            approve: true,
            "AiGateway.ApproveFinalOutput");

        using var finalizeResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/aigateway/workspace/{seeded.WorkspaceCode}/finalize",
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
        await AuthenticateAsAdminAsync();
        var owner = await CreateUserAsync($"batch5-owner-{Guid.NewGuid():N}", "User");
        var role = await CreateRoleAsync(
            $"Batch5Approver-{Guid.NewGuid():N}",
            [
                "AiGateway.GetAgentTask",
                "AiGateway.ApproveAgentToolCall",
                "AiGateway.ApproveFinalOutput",
                "AiGateway.FinalizeWorkspace"
            ]);
        var approver = await CreateUserAsync($"batch5-approver-{Guid.NewGuid():N}", role.RoleName);

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

        var artifactId = workspace.Artifacts.Single(item => item.RelativePath == "draft/report.md").Id;
        using var downloadResponse = await _fixture.HttpClient.GetAsync($"/api/aigateway/artifact/{artifactId}/download");
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
        var task = new AgentTask(
            new SessionId(Guid.NewGuid()),
            ownerId,
            "Batch 5 tool approval",
            "Batch 5 tool approval",
            AgentTaskType.DataAnalysis,
            AgentTaskRiskLevel.Medium,
            null,
            """{"version":1}""",
            now);
        var step = task.AddStep(
            "Generate PDF",
            "Approval-gated tool step.",
            AgentStepType.ArtifactGeneration,
            "generate_pdf",
            requiresApproval: true,
            now);
        task.ApprovePlan(now);
        task.Start(now);
        task.WaitForToolApproval(now);

        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.ToolCall,
            step.Id.Value.ToString(),
            ownerId,
            now);

        dbContext.AgentTasks.Add(task);
        dbContext.ApprovalRequests.Add(approval);
        await dbContext.SaveChangesAsync();

        return new SeededAgentTask(task.Id.Value, null, approval.Id.Value, null);
    }

    private async Task<SeededAgentTask> SeedWaitingFinalApprovalTaskAsync(Guid ownerId)
    {
        return await CreateSeededTaskAsync(ownerId, markWaitingFinalApproval: true, includeToolApproval: false);
    }

    private async Task<SeededAgentTask> CreateSeededTaskAsync(
        Guid ownerId,
        bool markWaitingFinalApproval,
        bool includeToolApproval)
    {
        var now = DateTimeOffset.UtcNow;
        var workspaceCode = $"ws_batch5_{Guid.NewGuid():N}"[..38];
        var workspaceRoot = Path.Combine(GetWorkspaceRoot(), workspaceCode);
        var draftDirectory = Path.Combine(workspaceRoot, "draft");
        Directory.CreateDirectory(draftDirectory);
        var draftPath = Path.Combine(draftDirectory, "report.md");
        await File.WriteAllTextAsync(draftPath, "# Batch 5 approval hardening", Encoding.UTF8);

        await using var dbContext = await CreateAiGatewayDbContextAsync();
        var task = new AgentTask(
            new SessionId(Guid.NewGuid()),
            ownerId,
            "Batch 5 final output",
            "Batch 5 final output",
            AgentTaskType.CloudDataReport,
            AgentTaskRiskLevel.Medium,
            null,
            """{"version":1}""",
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

        var finalStep = task.AddStep(
            "Finalize",
            "Approval-gated final output step.",
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
        workspace.AddDraftArtifact(
            ArtifactType.Markdown,
            "report.md",
            "draft/report.md",
            new FileInfo(draftPath).Length,
            "text/markdown",
            finalStep.Id,
            now);

        task.AttachWorkspace(workspace.Id, now);
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

        dbContext.AgentTasks.Add(task);
        dbContext.ArtifactWorkspaces.Add(workspace);
        await dbContext.SaveChangesAsync();

        return new SeededAgentTask(
            task.Id.Value,
            workspace.WorkspaceCode,
            toolApproval?.Id.Value,
            finalApproval?.Id.Value);
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
        using var response = await _fixture.HttpClient.PostAsJsonAsync(
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
        _fixture.SetAuthToken(result.Token);
    }

    private async Task<T> GetJsonAsync<T>(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"GET '{uri}' failed: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private async Task<T> PostJsonAsync<T>(string uri, object payload)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(uri, payload, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"POST '{uri}' failed: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
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
        return Path.Combine(Path.GetTempPath(), "AICopilotBackendTests", "artifact-workspaces");
    }

    private sealed record SeededAgentTask(
        Guid TaskId,
        string? WorkspaceCode,
        Guid? ToolApprovalId,
        Guid? FinalApprovalId);

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

    private sealed record AgentTaskDto(Guid Id, string Status, bool CanApproveFinal);

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
