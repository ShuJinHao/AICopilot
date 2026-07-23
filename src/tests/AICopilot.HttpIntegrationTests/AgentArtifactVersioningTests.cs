using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.HttpIntegrationTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class AgentArtifactVersioningTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AICopilotAppFixture _fixture;

    public AgentArtifactVersioningTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Owner_ShouldEditDiffDownloadAndRestoreDraftTextArtifact()
    {
        await AuthenticateAsAdminAsync();
        var owner = await CreateUserAsync($"artifact-owner-{Guid.NewGuid():N}", "User");
        var seeded = await SeedWorkspaceReadyArtifactAsync(
            Guid.Parse(owner.UserId),
            ArtifactType.Markdown,
            "draft/report.md",
            "# Report\nold line\n",
            "text/markdown");

        await AuthenticateAsync(owner.UserName, "Password123!");

        var content = await GetJsonAsync<ArtifactContentDto>($"/api/aigateway/artifact/{seeded.ArtifactId}/content");
        content.Version.Should().Be(1);
        content.Editable.Should().BeTrue();
        content.Content.Should().Contain("old line");

        var updated = await PutJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/artifact/{seeded.ArtifactId}/content",
            new
            {
                content = "# Report\nnew line\nadded line\n",
                expectedVersion = 1,
                comment = "replace old line"
            });
        var updatedArtifact = updated.Artifacts.Single(item => item.Id == seeded.ArtifactId);
        updatedArtifact.Version.Should().Be(2);
        updatedArtifact.RelativePath.Should().Be("draft/report.md");

        using var staleResponse = await _fixture.HttpClient.PutAsJsonAsync(
            $"/api/aigateway/artifact/{seeded.ArtifactId}/content",
            new
            {
                content = "# stale",
                expectedVersion = 1
            },
            JsonOptions);
        staleResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var versions = await GetJsonAsync<List<ArtifactVersionDto>>($"/api/aigateway/artifact/{seeded.ArtifactId}/versions");
        versions.Select(item => item.Version).Should().Equal(1, 2);
        versions.Single(item => item.Version == 1).IsCurrent.Should().BeFalse();
        versions.Single(item => item.Version == 2).IsCurrent.Should().BeTrue();

        var historical = await DownloadStringAsync($"/api/aigateway/artifact/{seeded.ArtifactId}/versions/1/download");
        historical.Should().Contain("old line");

        var diff = await GetJsonAsync<ArtifactTextDiffDto>(
            $"/api/aigateway/artifact/{seeded.ArtifactId}/versions/1/diff/2");
        diff.FromVersion.Should().Be(1);
        diff.ToVersion.Should().Be(2);
        diff.Entries.Should().Contain(item => item.Kind == "modified" && item.OldText == "old line" && item.NewText == "new line");
        diff.Entries.Should().Contain(item => item.Kind == "added" && item.NewText == "added line");

        var restored = await PostJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/artifact/{seeded.ArtifactId}/versions/1/restore",
            new
            {
                expectedVersion = 2,
                comment = "restore v1"
            });
        var restoredArtifact = restored.Artifacts.Single(item => item.Id == seeded.ArtifactId);
        restoredArtifact.Version.Should().Be(3);
        restoredArtifact.RelativePath.Should().Be("draft/report.md");

        var restoredContent = await GetJsonAsync<ArtifactContentDto>($"/api/aigateway/artifact/{seeded.ArtifactId}/content");
        restoredContent.Content.Should().Contain("old line");
        restoredContent.Content.Should().NotContain("added line");

        var restoredVersions = await GetJsonAsync<List<ArtifactVersionDto>>($"/api/aigateway/artifact/{seeded.ArtifactId}/versions");
        restoredVersions.Select(item => item.Version).Should().Equal(1, 2, 3);
        restoredVersions.Single(item => item.Version == 3).IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Owner_ShouldNotEditAfterFinalReviewSubmission()
    {
        await AuthenticateAsAdminAsync();
        var owner = await CreateUserAsync($"artifact-lock-owner-{Guid.NewGuid():N}", "User");
        var seeded = await SeedWorkspaceReadyArtifactAsync(
            Guid.Parse(owner.UserId),
            ArtifactType.Markdown,
            "draft/report.md",
            "# Final review lock\n",
            "text/markdown");

        await AuthenticateAsync(owner.UserName, "Password123!");
        await PostJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{seeded.WorkspaceCode}/submit-final-review",
            new { });

        using var response = await _fixture.HttpClient.PutAsJsonAsync(
            $"/api/aigateway/artifact/{seeded.ArtifactId}/content",
            new
            {
                content = "# should not update",
                expectedVersion = 1
            },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("locked after final review");
    }

    [Fact]
    public async Task UserAndPrivilegedAccount_ShouldNotCrossUserEditDraftArtifact()
    {
        await AuthenticateAsAdminAsync();
        var owner = await CreateUserAsync($"artifact-cross-owner-{Guid.NewGuid():N}", "User");
        var other = await CreateUserAsync($"artifact-cross-other-{Guid.NewGuid():N}", "User");
        var seeded = await SeedWorkspaceReadyArtifactAsync(
            Guid.Parse(owner.UserId),
            ArtifactType.Markdown,
            "draft/report.md",
            "# owner only\n",
            "text/markdown");

        await AuthenticateAsync(other.UserName, "Password123!");
        using var otherResponse = await _fixture.HttpClient.PutAsJsonAsync(
            $"/api/aigateway/artifact/{seeded.ArtifactId}/content",
            new
            {
                content = "# not owner",
                expectedVersion = 1
            },
            JsonOptions);
        otherResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await AuthenticateAsAdminAsync();
        using var adminResponse = await _fixture.HttpClient.PutAsJsonAsync(
            $"/api/aigateway/artifact/{seeded.ArtifactId}/content",
            new
            {
                content = "# admin is not owner",
                expectedVersion = 1
            },
            JsonOptions);
        adminResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UserWithoutEditArtifactPermission_ShouldReceiveMissingPermission()
    {
        await AuthenticateAsAdminAsync();
        var role = await CreateRoleAsync(
            $"ArtifactReadOnly-{Guid.NewGuid():N}",
            [
                "AiGateway.GetWorkspace",
                "AiGateway.DownloadArtifact",
                "AiGateway.SubmitFinalReview"
            ]);
        var owner = await CreateUserAsync($"artifact-readonly-{Guid.NewGuid():N}", role.RoleName);
        var seeded = await SeedWorkspaceReadyArtifactAsync(
            Guid.Parse(owner.UserId),
            ArtifactType.Markdown,
            "draft/report.md",
            "# readonly\n",
            "text/markdown");

        await AuthenticateAsync(owner.UserName, "Password123!");
        using var response = await _fixture.HttpClient.PutAsJsonAsync(
            $"/api/aigateway/artifact/{seeded.ArtifactId}/content",
            new
            {
                content = "# denied",
                expectedVersion = 1
            },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await ReadJsonAsync<ProblemDetailsDto>(response);
        problem.Code.Should().Be("missing_permission");
        problem.MissingPermissions.Should().Contain("AiGateway.EditArtifact");
    }

    [Fact]
    public async Task NonTextArtifact_ShouldRejectContentDiffAndRestore()
    {
        await AuthenticateAsAdminAsync();
        var owner = await CreateUserAsync($"artifact-pdf-owner-{Guid.NewGuid():N}", "User");
        var seeded = await SeedWorkspaceReadyArtifactAsync(
            Guid.Parse(owner.UserId),
            ArtifactType.Pdf,
            "draft/report.pdf",
            "%PDF-1.4 fake",
            "application/pdf");

        await AuthenticateAsync(owner.UserName, "Password123!");

        using var contentResponse = await _fixture.HttpClient.GetAsync($"/api/aigateway/artifact/{seeded.ArtifactId}/content");
        contentResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var editResponse = await _fixture.HttpClient.PutAsJsonAsync(
            $"/api/aigateway/artifact/{seeded.ArtifactId}/content",
            new
            {
                content = "not allowed",
                expectedVersion = 1
            },
            JsonOptions);
        editResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var diffResponse = await _fixture.HttpClient.GetAsync(
            $"/api/aigateway/artifact/{seeded.ArtifactId}/versions/1/diff/1");
        diffResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var restoreResponse = await _fixture.HttpClient.PostAsJsonAsync(
            $"/api/aigateway/artifact/{seeded.ArtifactId}/versions/1/restore",
            new
            {
                expectedVersion = 1
            },
            JsonOptions);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<SeededArtifact> SeedWorkspaceReadyArtifactAsync(
        Guid ownerId,
        ArtifactType artifactType,
        string relativePath,
        string content,
        string mimeType)
    {
        var now = DateTimeOffset.UtcNow;
        var workspaceCode = $"ws_artifact_{Guid.NewGuid():N}"[..38];
        var workspaceRoot = Path.Combine(GetWorkspaceRoot(), workspaceCode);
        var fullPath = Path.Combine(workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);

        await using var dbContext = await CreateAiGatewayDbContextAsync();
        var generationPlanStep = CreateArtifactGenerationPlanStep(artifactType, relativePath);
        var planJson = AgentPlanV2TestData.CreateCanonicalBuiltInPlanDraft(
            [generationPlanStep],
            AgentTaskType.CloudDataReport,
            knowledgeBaseIds: null);
        var task = new AgentTask(
            new SessionId(Guid.NewGuid()),
            ownerId,
            "Artifact versioning workflow",
            "Artifact versioning workflow",
            AgentTaskType.CloudDataReport,
            AgentTaskRiskLevel.Medium,
            null,
            planJson,
            now);
        var step = AgentPlanV2TestData.AddTrackedPlanSteps(task, planJson, now)
            .Single(item => string.Equals(
                item.ToolCode,
                generationPlanStep.ToolCode,
                StringComparison.Ordinal));
        var workspace = new ArtifactWorkspace(
            task.Id,
            workspaceCode,
            workspaceRoot,
            $"/workspaces/{workspaceCode}",
            now);
        var artifact = workspace.AddDraftArtifact(
            artifactType,
            Path.GetFileName(relativePath),
            relativePath,
            new FileInfo(fullPath).Length,
            mimeType,
            step.Id,
            now);

        task.AttachWorkspace(workspace.Id, now);
        task.ConfirmExecutablePlan(task.PlanJson, Array.Empty<int>(), now);
        task.ApprovePlan(now);
        task.MarkWorkspaceReady(now);

        dbContext.AgentTasks.Add(task);
        dbContext.ArtifactWorkspaces.Add(workspace);
        await dbContext.SaveChangesAsync();

        return new SeededArtifact(task.Id.Value, workspace.WorkspaceCode, artifact.Id.Value);
    }

    private static AgentPlanV2TestStep CreateArtifactGenerationPlanStep(
        ArtifactType artifactType,
        string relativePath)
    {
        return artifactType switch
        {
            ArtifactType.Chart or ArtifactType.Json => new AgentPlanV2TestStep(
                "Generate chart data",
                $"Generate chart data for '{relativePath}'.",
                AgentStepType.ArtifactGeneration,
                "generate_chart_data"),
            ArtifactType.Markdown => new AgentPlanV2TestStep(
                "Generate Markdown report",
                $"Generate the Markdown artifact '{relativePath}'.",
                AgentStepType.ArtifactGeneration,
                "generate_markdown_report"),
            ArtifactType.Html => new AgentPlanV2TestStep(
                "Generate HTML report",
                $"Generate the HTML artifact '{relativePath}'.",
                AgentStepType.ArtifactGeneration,
                "generate_html_report"),
            ArtifactType.Pdf => new AgentPlanV2TestStep(
                "Generate PDF",
                $"Generate the PDF artifact '{relativePath}'.",
                AgentStepType.ArtifactGeneration,
                "generate_pdf",
                RequiresApproval: true),
            ArtifactType.Pptx => new AgentPlanV2TestStep(
                "Generate PowerPoint",
                $"Generate the PowerPoint artifact '{relativePath}'.",
                AgentStepType.ArtifactGeneration,
                "generate_pptx",
                RequiresApproval: true),
            ArtifactType.Xlsx => new AgentPlanV2TestStep(
                "Generate Excel workbook",
                $"Generate the Excel artifact '{relativePath}'.",
                AgentStepType.ArtifactGeneration,
                "generate_xlsx",
                RequiresApproval: true),
            _ => throw new InvalidOperationException(
                $"Artifact fixture '{relativePath}' ({artifactType}) has no canonical Plan v2 artifact target.")
        };
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

    private async Task<T> PutJsonAsync<T>(string uri, object payload)
    {
        using var response = await _fixture.HttpClient.PutAsJsonAsync(uri, payload, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"PUT '{uri}' failed: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private async Task<string> DownloadStringAsync(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
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

    private sealed record SeededArtifact(Guid TaskId, string WorkspaceCode, Guid ArtifactId);

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

    private sealed record ArtifactContentDto(
        Guid Id,
        string WorkspaceCode,
        string Name,
        string Type,
        string Status,
        string RelativePath,
        int Version,
        string MimeType,
        string Content,
        DateTimeOffset UpdatedAt,
        bool Editable);

    private sealed record ArtifactVersionDto(
        int Version,
        string FileName,
        long FileSize,
        string MimeType,
        string Sha256,
        DateTimeOffset CreatedAt,
        bool IsCurrent,
        string DownloadUrl);

    private sealed record ArtifactTextDiffDto(
        Guid ArtifactId,
        int FromVersion,
        int ToVersion,
        int FromLineCount,
        int ToLineCount,
        IReadOnlyCollection<ArtifactTextDiffEntryDto> Entries,
        bool Truncated);

    private sealed record ArtifactTextDiffEntryDto(
        string Kind,
        int? OldLine,
        int? NewLine,
        string? OldText,
        string? NewText);
}
