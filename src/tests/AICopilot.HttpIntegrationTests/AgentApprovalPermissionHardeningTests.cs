using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
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

    private readonly AICopilotAppFixture _fixture;

    public AgentApprovalPermissionHardeningTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UserRole_ShouldSubmitFinalReview_ButCannotApproveToolFinalOrFinalize()
    {
        await AuthenticateAsAdminAsync();
        var owner = await CreateUserAsync($"approval-owner-{Guid.NewGuid():N}", "User");
        var seeded = await SeedWorkspaceReadyTaskAsync(Guid.Parse(owner.UserId), includeToolApproval: false);
        var toolTask = await SeedWaitingToolApprovalTaskAsync(Guid.Parse(owner.UserId));

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
            toolTask.ToolApprovalId!.Value,
            approve: true,
            "AiGateway.ApproveAgentToolCall");
        await AssertApprovalForbiddenAsync(
            toolTask.ToolApprovalId.Value,
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
    public async Task PrivilegedApprover_ShouldCrossUserApproveToolAndFinalOutput()
    {
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

        var artifactId = workspace.Artifacts.Single(item => item.RelativePath == "draft/report.md").Id;
        using var downloadResponse = await _fixture.HttpClient.GetAsync($"/api/aigateway/artifact/{artifactId}/download");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var approvedFinal = await PostJsonAsync<AgentApprovalRequestDto>(
            $"/api/aigateway/agent/approval/{finalTask.FinalApprovalId}/approve",
            new { comment = "cross-user final output approved" });
        approvedFinal.Type.Should().Be("FinalOutput");
        approvedFinal.Status.Should().Be("Approved");
        approvedFinal.DecidedBy.Should().Be(Guid.Parse(approver.UserId));
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
        var planJson = AgentPlanV2TestData.Create(
            [new AgentPlanV2TestStep(
                "Generate PDF",
                "Approval-gated tool step.",
                AgentStepType.ArtifactGeneration,
                "generate_pdf",
                RequiresApproval: true)],
            executable: true,
            taskType: AgentTaskType.DataAnalysis,
            knowledgeBaseIds: null);
        var session = new Session(ownerId, ConversationTemplateId.New());
        var task = new AgentTask(
            session.Id,
            ownerId,
            "Tool approval permission hardening",
            "Tool approval permission hardening",
            AgentTaskType.DataAnalysis,
            AgentTaskRiskLevel.Medium,
            null,
            planJson,
            now);
        var step = AgentPlanV2TestData.AddTrackedPlanSteps(task, planJson, now)
            .Single(item => string.Equals(item.ToolCode, "generate_pdf", StringComparison.Ordinal));
        task.ConfirmExecutablePlan(task.PlanJson, Array.Empty<int>(), now);
        task.ApprovePlan(now);
        task.Start(now);
        task.WaitForToolApproval(now);

        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.ToolCall,
            step.Id.Value.ToString(),
            ownerId,
            now);

        dbContext.Sessions.Add(session);
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
        bool includeToolApproval,
        IReadOnlyCollection<SeedArtifactInput>? artifactInputs = null)
    {
        // Keep the fixture's synthetic multi-step timeline behind wall-clock time so an
        // immediate HTTP decision never predates the immutable approval proof.
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var workspaceCode = $"ws_approval_{Guid.NewGuid():N}"[..38];
        var workspaceRoot = Path.Combine(GetWorkspaceRoot(), workspaceCode);
        var artifacts = artifactInputs ?? (includeToolApproval
            ?
            [
                new SeedArtifactInput(
                    ArtifactType.Pdf,
                    "draft/report.pdf",
                    "%PDF-1.4 approval permission hardening",
                    "application/pdf")
            ]
            :
            [
                new SeedArtifactInput(
                    ArtifactType.Markdown,
                    "draft/report.md",
                    "# Approval permission hardening",
                    "text/markdown")
            ]);
        var artifactFiles = new List<(SeedArtifactInput Artifact, string FullPath)>();
        foreach (var artifact in artifacts)
        {
            var fullPath = Path.Combine(workspaceRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, artifact.Content, Encoding.UTF8);
            artifactFiles.Add((artifact, fullPath));
        }

        await using var dbContext = await CreateAiGatewayDbContextAsync();
        var generationPlanSteps = artifacts
            .Select(CreateArtifactGenerationPlanStep)
            .Select((step, index) => index == 0 && includeToolApproval
                ? step with { RequiresApproval = true }
                : step)
            .ToArray();
        var planJson = AgentPlanV2TestData.Create(
            generationPlanSteps,
            executable: true,
            taskType: AgentTaskType.ReportGeneration,
            knowledgeBaseIds: null);
        var session = new Session(ownerId, ConversationTemplateId.New());
        var task = new AgentTask(
            session.Id,
            ownerId,
            "Approval permission final output",
            "Approval permission final output",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            planJson,
            now);
        var trackedPlanSteps = AgentPlanV2TestData.AddTrackedPlanSteps(task, planJson, now);
        var generationSteps = trackedPlanSteps
            .Where(step => step.StepType is AgentStepType.ArtifactGeneration or AgentStepType.ChartGeneration)
            .OrderBy(step => step.StepIndex)
            .ToArray();
        var toolStep = generationSteps.SingleOrDefault(step => step.RequiresApproval);
        var workspace = new ArtifactWorkspace(
            task.Id,
            workspaceCode,
            workspaceRoot,
            $"/workspaces/{workspaceCode}",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.ConfirmExecutablePlan(
            task.PlanJson,
            trackedPlanSteps
                .Where(step => step.RequiresApproval)
                .Select(step => step.StepIndex)
                .ToArray(),
            now);
        task.ApprovePlan(now);
        var artifactContents = new Dictionary<Guid, byte[]>();

        for (var index = 0; index < artifactFiles.Count; index++)
        {
            var step = generationSteps[index];
            var stepStartedAt = now.AddSeconds(2 + index * 3);
            if (!step.RequiresApproval)
            {
                step.Start(stepStartedAt);
            }

            var input = artifactFiles[index];
            var created = workspace.AddDraftArtifact(
                input.Artifact.Type,
                Path.GetFileName(input.Artifact.RelativePath),
                input.Artifact.RelativePath,
                new FileInfo(input.FullPath).Length,
                input.Artifact.MimeType,
                step.Id,
                stepStartedAt.AddSeconds(1));
            if (!step.RequiresApproval)
            {
                step.Complete(
                    JsonSerializer.Serialize(new
                    {
                        status = "completed",
                        resultType = "artifact",
                        artifactType = created.ArtifactType.ToString().ToLowerInvariant(),
                        artifactId = created.Id.Value
                    }, JsonOptions),
                    stepStartedAt.AddSeconds(2));
            }

            artifactContents[created.Id.Value] =
                await File.ReadAllBytesAsync(input.FullPath);
        }

        var checkpointAt = now.AddSeconds(3 + artifactFiles.Count * 3);
        var authority = await FinalOutputApprovalTestData.CreatePreApprovalAuthorityAsync(
            task,
            workspace,
            artifactContents,
            checkpointAt);
        if (markWaitingFinalApproval)
        {
            task.WaitForFinalApproval(checkpointAt);
            authority.RunAttempt.WaitForApproval(
                checkpointAt,
                "Waiting for final output approval.");
            task.ReleaseRunLease(checkpointAt, clearActiveAttempt: false);
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
            finalApproval = ApprovalRequest.CreateFinalOutput(
                task.Id,
                ownerId,
                checkpointAt,
                authority.Proof);
            dbContext.ApprovalRequests.Add(finalApproval);
        }

        dbContext.Sessions.Add(session);
        dbContext.AgentTasks.Add(task);
        dbContext.ArtifactWorkspaces.Add(workspace);
        dbContext.AgentTaskRunAttempts.Add(authority.RunAttempt);
        dbContext.AgentTaskRunQueueItems.Add(authority.OriginalQueueItem);
        dbContext.Set<AgentNodeRun>().AddRange(authority.NodeRuns);
        dbContext.Set<AgentEvidenceRecord>().AddRange(authority.Evidence);
        await dbContext.SaveChangesAsync();

        return new SeededAgentTask(
            task.Id.Value,
            workspace.WorkspaceCode,
            toolApproval?.Id.Value,
            finalApproval?.Id.Value);
    }

    private static AgentPlanV2TestStep CreateArtifactGenerationPlanStep(SeedArtifactInput artifact)
    {
        return ResolveArtifactGenerationToolCode(artifact) switch
        {
            "generate_chart_data" => new AgentPlanV2TestStep(
                "Generate chart data",
                "Generate chart data for workspace artifacts.",
                AgentStepType.ArtifactGeneration,
                "generate_chart_data"),
            "generate_markdown_report" => new AgentPlanV2TestStep(
                "Generate Markdown report",
                "Generate the Markdown workspace artifact.",
                AgentStepType.ArtifactGeneration,
                "generate_markdown_report"),
            "generate_html_report" => new AgentPlanV2TestStep(
                "Generate HTML report",
                "Generate the HTML workspace artifact.",
                AgentStepType.ArtifactGeneration,
                "generate_html_report"),
            "generate_pdf" => new AgentPlanV2TestStep(
                "Generate PDF",
                "Generate the PDF workspace artifact.",
                AgentStepType.ArtifactGeneration,
                "generate_pdf",
                RequiresApproval: true),
            "generate_pptx" => new AgentPlanV2TestStep(
                "Generate PowerPoint",
                "Generate the PowerPoint workspace artifact.",
                AgentStepType.ArtifactGeneration,
                "generate_pptx",
                RequiresApproval: true),
            "generate_xlsx" => new AgentPlanV2TestStep(
                "Generate Excel workbook",
                "Generate the Excel workspace artifact.",
                AgentStepType.ArtifactGeneration,
                "generate_xlsx",
                RequiresApproval: true),
            var toolCode => throw new InvalidOperationException(
                $"Unsupported canonical artifact generation tool '{toolCode}'.")
        };
    }

    private static string ResolveArtifactGenerationToolCode(SeedArtifactInput artifact)
    {
        return artifact.Type switch
        {
            ArtifactType.Chart or ArtifactType.Json => "generate_chart_data",
            ArtifactType.Markdown => "generate_markdown_report",
            ArtifactType.Html => "generate_html_report",
            ArtifactType.Pdf => "generate_pdf",
            ArtifactType.Pptx => "generate_pptx",
            ArtifactType.Xlsx => "generate_xlsx",
            _ => throw new InvalidOperationException(
                $"Artifact fixture '{artifact.RelativePath}' ({artifact.Type}) has no canonical Plan v2 artifact target.")
        };
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
        return Path.Combine(Path.GetTempPath(), "AICopilotIntegrationTests", "artifact-workspaces");
    }

    private sealed record SeededAgentTask(
        Guid TaskId,
        string? WorkspaceCode,
        Guid? ToolApprovalId,
        Guid? FinalApprovalId);

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
