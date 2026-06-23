using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Infrastructure.AiGateway;
using AICopilot.Services.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AICopilot.BackendTests;

[Collection(CoreBackendTestCollection.Name)]
[Trait("Suite", "AcceptanceClosure")]
[Trait("Runtime", "DockerRequired")]
public sealed class AcceptanceClosureVerificationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AICopilotAppFixture _fixture;

    public AcceptanceClosureVerificationTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MigrationSchema_ShouldContainOnsiteAttestationColumns_AndUtcSafeTypes()
    {
        var connectionString = await _fixture.GetConnectionStringAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var sessionColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "sessions",
            ["onsite_confirmed_at", "onsite_confirmation_expires_at", "onsite_confirmed_by"]);

        sessionColumns.Should().ContainKey("onsite_confirmed_at");
        sessionColumns["onsite_confirmed_at"].Should().Be("timestamp with time zone");
        sessionColumns.Should().ContainKey("onsite_confirmation_expires_at");
        sessionColumns["onsite_confirmation_expires_at"].Should().Be("timestamp with time zone");
        sessionColumns.Should().ContainKey("onsite_confirmed_by");
        sessionColumns["onsite_confirmed_by"].Should().BeOneOf("text", "character varying");

        var approvalPolicyColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "approval_policies",
            ["requires_onsite_attestation"]);

        approvalPolicyColumns.Should().ContainKey("requires_onsite_attestation");
        approvalPolicyColumns["requires_onsite_attestation"].Should().Be("boolean");

        var queueColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "agent_task_run_queue_items",
            ["task_id", "trigger_type", "status", "requested_by", "run_attempt_id", "lease_expires_at", "available_at"]);

        queueColumns["task_id"].Should().Be("uuid");
        queueColumns["trigger_type"].Should().Be("character varying");
        queueColumns["status"].Should().Be("character varying");
        queueColumns["requested_by"].Should().Be("uuid");
        queueColumns["run_attempt_id"].Should().Be("uuid");
        queueColumns["lease_expires_at"].Should().Be("timestamp with time zone");
        queueColumns["available_at"].Should().Be("timestamp with time zone");

        await using var migrationCommand = connection.CreateCommand();
        migrationCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM public."__EFMigrationsHistory_AiCopilot"
            WHERE "MigrationId" = '20260423064237_Phase44OnsiteAttestationColumns';
            """;

        var migrationCount = Convert.ToInt32(await migrationCommand.ExecuteScalarAsync());
        migrationCount.Should().Be(1);
    }

    [Fact]
    public async Task MigrationSchema_ShouldContainDynamicModelRoutingColumns_AndSingleActiveRoutingIndex()
    {
        var connectionString = await _fixture.GetConnectionStringAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var languageModelColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "language_models",
            [
                "protocol_type",
                "usage",
                "is_enabled",
                "max_output_tokens",
                "connectivity_status",
                "connectivity_checked_at",
                "connectivity_error"
            ]);

        languageModelColumns["protocol_type"].Should().Be("character varying");
        languageModelColumns["usage"].Should().Be("integer");
        languageModelColumns["is_enabled"].Should().Be("boolean");
        languageModelColumns["max_output_tokens"].Should().Be("integer");
        languageModelColumns["connectivity_status"].Should().Be("integer");
        languageModelColumns["connectivity_checked_at"].Should().Be("timestamp with time zone");
        languageModelColumns["connectivity_error"].Should().Be("character varying");

        var messageColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "messages",
            [
                "final_model_id",
                "final_model_name",
                "routing_model_id",
                "routing_model_name",
                "context_window_tokens",
                "max_output_tokens"
            ]);

        messageColumns["final_model_id"].Should().Be("uuid");
        messageColumns["final_model_name"].Should().Be("character varying");
        messageColumns["routing_model_id"].Should().Be("uuid");
        messageColumns["routing_model_name"].Should().Be("character varying");
        messageColumns["context_window_tokens"].Should().Be("integer");
        messageColumns["max_output_tokens"].Should().Be("integer");

        var routingModelColumns = await QueryColumnMetadataAsync(
            connection,
            "aigateway",
            "routing_model_configurations",
            ["id", "name", "model_id", "is_active"]);

        routingModelColumns["id"].Should().Be("uuid");
        routingModelColumns["name"].Should().Be("character varying");
        routingModelColumns["model_id"].Should().Be("uuid");
        routingModelColumns["is_active"].Should().Be("boolean");

        var indexDefinitions = await QueryIndexDefinitionsAsync(
            connection,
            "aigateway",
            "routing_model_configurations");

        indexDefinitions.Should().Contain(definition =>
            definition.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            && definition.Contains("is_active", StringComparison.OrdinalIgnoreCase)
            && definition.Contains("WHERE is_active", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RedisFinalAgentContextStore_ShouldShareContextAcrossStoreInstances()
    {
        var redisConnectionString = await _fixture.GetConnectionStringAsync("final-agent-context-redis");
        using var serviceProviderA = CreateRedisServiceProvider(redisConnectionString);
        using var serviceProviderB = CreateRedisServiceProvider(redisConnectionString);

        var storeA = new RedisFinalAgentContextStore(serviceProviderA.GetRequiredService<IDistributedCache>());
        var storeB = new RedisFinalAgentContextStore(serviceProviderB.GetRequiredService<IDistributedCache>());

        var sessionId = Guid.NewGuid();
        var storedContext = new StoredFinalAgentContext(
            sessionId,
            "prepare diagnostic checklist for device DEV-001",
            128,
            64,
            new ChatTokenTelemetryContext(sessionId, "fake-model", "fake-template", 4096, 512),
            512,
            0.3f,
            ["GenerateDiagnosticChecklist"],
            """{"threadId":"acceptance-redis-test"}""",
            [
                new StoredToolApprovalRequest(
                    "request-1",
                    "call-1",
                    "Function",
                    "GenerateDiagnosticChecklist",
                    null,
                    new Dictionary<string, object?>
                    {
                        ["deviceCode"] = "DEV-001"
                    })
            ]);

        await storeA.SetAsync(sessionId, storedContext);

        var restoredContext = await storeB.GetAsync(sessionId);
        restoredContext.Should().NotBeNull();
        restoredContext!.SessionId.Should().Be(sessionId);
        restoredContext.InputText.Should().Be(storedContext.InputText);
        restoredContext.ToolNames.Should().Equal(storedContext.ToolNames);
        restoredContext.PendingApprovals.Should().HaveCount(1);
        restoredContext.PendingApprovals[0].CallId.Should().Be("call-1");
        restoredContext.PendingApprovals[0].ToolName.Should().Be("GenerateDiagnosticChecklist");
        restoredContext.PendingApprovals[0].Arguments.Should().ContainKey("deviceCode");
        restoredContext.PendingApprovals[0].Arguments["deviceCode"]?.ToString().Should().Be("DEV-001");

        await storeB.RemoveAsync(sessionId);

        var removedContext = await storeA.GetAsync(sessionId);
        removedContext.Should().BeNull();
    }

    [Fact]
    public async Task AgentArtifactClosure_ShouldFinalizeDownloadAndExposeTaskAuditSummary()
    {
        await AuthenticateAsAdminAsync();

        var templateId = await CreateConversationTemplateAsync();
        var session = await PostJsonAsync<CreatedSessionDto>("/api/aigateway/session", new { templateId });
        var upload = await UploadAiGatewayFileAsync(
            session.Id,
            $"acceptance-{Guid.NewGuid():N}.csv",
            "station,count\nA,2\nB,3\nC,5\n");

        var task = await PostJsonAsync<AgentTaskDto>("/api/aigateway/agent/task/plan", new
        {
            sessionId = session.Id,
            goal = "Generate a controlled acceptance report from the uploaded CSV.",
            taskType = 2,
            modelId = (Guid?)null,
            uploadIds = new[] { upload.Id },
            knowledgeBaseIds = Array.Empty<Guid>()
        });

        task.Status.Should().Be("WaitingPlanApproval");
        task.WorkspaceCode.Should().NotBeNullOrWhiteSpace();

        var initialWorkspace = await GetJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{task.WorkspaceCode}");
        initialWorkspace.Artifacts.Should().BeEmpty();

        var planApproval = (await GetPendingApprovalsAsync(task.Id))
            .Should()
            .ContainSingle(item => item.Type == "Plan")
            .Subject;
        await ApproveAgentApprovalAsync(planApproval.Id, "Acceptance plan approved.");

        task = await PostJsonAsync<AgentTaskDto>("/api/aigateway/agent/task/run", new { id = task.Id });
        task.IsRunQueued.Should().BeTrue();
        task.RunQueueStatus.Should().Be("Queued");
        task = await WaitForTaskStatusAsync(task.Id, "WaitingToolApproval");
        task.Status.Should().Be("WaitingToolApproval");

        var draftWorkspace = await GetJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{task.WorkspaceCode}");
        draftWorkspace.Artifacts.Should().Contain(item => item.RelativePath.StartsWith("source/", StringComparison.OrdinalIgnoreCase));
        draftWorkspace.Artifacts.Should().Contain(item => item.RelativePath.StartsWith("data/", StringComparison.OrdinalIgnoreCase));
        draftWorkspace.Artifacts.Should().Contain(item => item.RelativePath == "charts/chart-data.json");
        draftWorkspace.Artifacts.Should().Contain(item => item.RelativePath == "draft/report.md");
        draftWorkspace.Artifacts.Should().Contain(item => item.RelativePath == "draft/report.html");
        draftWorkspace.Artifacts.Should().OnlyContain(item => item.Status != "Final");
        draftWorkspace.Artifacts.Should().OnlyContain(item => !item.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase));
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

            pendingApprovals.Should().NotBeEmpty("the runtime should pause before each high-risk tool");
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
        draftWorkspace.Artifacts.Should().Contain(item => item.RelativePath == "draft/report.pdf");
        draftWorkspace.Artifacts.Should().Contain(item => item.RelativePath == "draft/report.pptx");
        draftWorkspace.Artifacts.Should().Contain(item => item.RelativePath == "draft/report.xlsx");
        draftWorkspace.Artifacts.Should().OnlyContain(item => item.Status != "Final");
        draftWorkspace.Artifacts.Should().OnlyContain(item => !item.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase));

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
        finalizedWorkspace.Artifacts.Should().OnlyContain(item => item.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase));
        finalizedWorkspace.Manifest.Should().OnlyContain(item => item.Status == "Final" &&
                                                                item.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase));

        var downloadedBytes = await DownloadArtifactAsync(finalizedWorkspace.Artifacts.First().Id);
        downloadedBytes.Should().NotBeEmpty();

        var auditSummary = await GetJsonAsync<List<AgentTaskAuditSummaryDto>>(
            $"/api/aigateway/agent/task/{task.Id}/audit-summary");
        auditSummary.Should().Contain(item => item.ActionCode == "Agent.Plan");
        auditSummary.Should().Contain(item => item.ActionCode == "Agent.ApprovalDecision");
        auditSummary.Should().Contain(item => item.ActionCode == "Agent.ToolExecution" &&
                                             item.Metadata.ContainsKey("toolName") &&
                                             item.Metadata["toolName"] == "generate_markdown_report");
        auditSummary.Should().Contain(item => item.ActionCode == "Agent.ArtifactDownload");
        auditSummary.Should().Contain(item => item.ActionCode == "Agent.WorkspaceFinalize" &&
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
            item.ArtifactWorkspaceId == finalizedWorkspace.Id);
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

    [Fact]
    public async Task AgentPlanRejection_ShouldNotExecuteOrCreateFinalArtifacts()
    {
        await AuthenticateAsAdminAsync();

        var templateId = await CreateConversationTemplateAsync();
        var session = await PostJsonAsync<CreatedSessionDto>("/api/aigateway/session", new { templateId });
        var task = await PostJsonAsync<AgentTaskDto>("/api/aigateway/agent/task/plan", new
        {
            sessionId = session.Id,
            goal = "Create a report that should be rejected before execution.",
            taskType = 2,
            modelId = (Guid?)null,
            uploadIds = Array.Empty<Guid>(),
            knowledgeBaseIds = Array.Empty<Guid>()
        });

        var planApproval = (await GetPendingApprovalsAsync(task.Id))
            .Should()
            .ContainSingle(item => item.Type == "Plan")
            .Subject;
        await RejectAgentApprovalAsync(planApproval.Id, "Acceptance rejection.");

        using var runResponse = await _fixture.HttpClient.PostAsJsonAsync(
            "/api/aigateway/agent/task/run",
            new { id = task.Id },
            JsonOptions);
        runResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var rejectedTask = await GetJsonAsync<AgentTaskDto>($"/api/aigateway/agent/task?id={task.Id}");
        rejectedTask.Status.Should().Be("Rejected");

        var workspace = await GetJsonAsync<ArtifactWorkspaceDto>(
            $"/api/aigateway/workspace/{task.WorkspaceCode}");
        workspace.Artifacts.Should().BeEmpty();

        var auditSummary = await GetJsonAsync<List<AgentTaskAuditSummaryDto>>(
            $"/api/aigateway/agent/task/{task.Id}/audit-summary");
        auditSummary.Should().Contain(item => item.ActionCode == "Agent.ApprovalDecision" &&
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

    private static ServiceProvider CreateRedisServiceProvider(string redisConnectionString)
    {
        var services = new ServiceCollection();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "acceptance:";
        });

        return services.BuildServiceProvider();
    }

    private async Task AuthenticateAsAdminAsync()
    {
        _fixture.ClearAuthToken();

        var result = await PostJsonAsync<LoginUserDto>("/api/identity/login", new
        {
            username = _fixture.BootstrapAdminUserName,
            password = _fixture.BootstrapAdminPassword
        });

        _fixture.SetAuthToken(result.Token);
    }

    private async Task<UploadRecordDto> UploadAiGatewayFileAsync(Guid sessionId, string fileName, string content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("SessionTemp"), "scope");
        form.Add(new StringContent(sessionId.ToString()), "sessionId");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", fileName);

        using var response = await _fixture.HttpClient.PostAsync("/api/aigateway/upload", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<UploadRecordDto>(JsonOptions))!;
    }

    private async Task<Guid> CreateConversationTemplateAsync()
    {
        var model = await PostJsonAsync<CreatedLanguageModelDto>("/api/aigateway/language-model", new
        {
            provider = "OpenAI",
            name = $"acceptance-lm-{Guid.NewGuid():N}",
            baseUrl = new Uri(_fixture.FakeAiBaseUri, "/v1").ToString().TrimEnd('/'),
            apiKey = "sk-acceptance",
            maxTokens = 4096,
            contextWindowTokens = 4096,
            maxOutputTokens = 1024,
            usages = new[] { "Chat" },
            temperature = 0.1
        });

        var template = await PostJsonAsync<CreatedConversationTemplateDto>("/api/aigateway/conversation-template", new
        {
            name = $"AcceptanceAgent-{Guid.NewGuid():N}",
            description = "acceptance closure template",
            systemPrompt = "You are a controlled acceptance assistant.",
            modelId = model.Id,
            maxTokens = 512,
            temperature = 0.1
        });

        return template.Id;
    }

    private async Task<List<AgentApprovalRequestDto>> GetPendingApprovalsAsync(Guid taskId)
    {
        var approvals = await GetJsonAsync<List<AgentApprovalRequestDto>>(
            $"/api/aigateway/agent/task/{taskId}/approvals");
        return approvals.Where(item => item.Status == "Pending").ToList();
    }

    private async Task<AgentTaskDto> WaitForTaskStatusAsync(Guid taskId, string expectedStatus)
    {
        return await WaitForTaskAsync(
            taskId,
            task => string.Equals(task.Status, expectedStatus, StringComparison.OrdinalIgnoreCase),
            $"status {expectedStatus}");
    }

    private async Task<AgentTaskDto> WaitForTaskToPauseAsync(Guid taskId)
    {
        return await WaitForTaskAsync(
            taskId,
            task => !task.IsRunQueued &&
                    !task.IsRunInProgress &&
                    task.Status is "WaitingToolApproval" or "WaitingFinalApproval" or "WorkspaceReady" or "Failed",
            "worker pause");
    }

    private async Task<AgentTaskDto> WaitForTaskAsync(
        Guid taskId,
        Func<AgentTaskDto, bool> predicate,
        string reason)
    {
        AgentTaskDto? latest = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            latest = await GetJsonAsync<AgentTaskDto>($"/api/aigateway/agent/task?id={taskId}");
            if (predicate(latest))
            {
                return latest;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new Xunit.Sdk.XunitException(
            $"Timed out waiting for agent task {taskId} to reach {reason}. " +
            $"Last status={latest?.Status}, queue={latest?.RunQueueStatus}, queued={latest?.IsRunQueued}, running={latest?.IsRunInProgress}.");
    }

    private async Task ApproveAgentApprovalAsync(Guid approvalId, string comment)
    {
        _ = await PostJsonAsync<AgentApprovalRequestDto>(
            $"/api/aigateway/agent/approval/{approvalId}/approve",
            new { comment });
    }

    private async Task RejectAgentApprovalAsync(Guid approvalId, string comment)
    {
        _ = await PostJsonAsync<AgentApprovalRequestDto>(
            $"/api/aigateway/agent/approval/{approvalId}/reject",
            new { comment });
    }

    private async Task<byte[]> DownloadArtifactAsync(Guid artifactId)
    {
        using var response = await _fixture.HttpClient.GetAsync($"/api/aigateway/artifact/{artifactId}/download");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<T> GetJsonAsync<T>(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException(
                $"GET '{uri}' failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<T> PostJsonAsync<T>(string uri, object payload)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(uri, payload, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException(
                $"POST '{uri}' failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task PostJsonExpectingStatusAsync(string uri, object payload, HttpStatusCode statusCode)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(uri, payload, JsonOptions);
        response.StatusCode.Should().Be(statusCode);
    }

    private static async Task<Dictionary<string, string>> QueryColumnMetadataAsync(
        NpgsqlConnection connection,
        string schemaName,
        string tableName,
        IReadOnlyCollection<string> columnNames)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = @schemaName
              AND table_name = @tableName
              AND column_name = ANY(@columnNames)
            ORDER BY column_name;
            """;
        command.Parameters.AddWithValue("schemaName", schemaName);
        command.Parameters.AddWithValue("tableName", tableName);
        command.Parameters.AddWithValue("columnNames", columnNames.ToArray());

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    private static async Task<List<string>> QueryIndexDefinitionsAsync(
        NpgsqlConnection connection,
        string schemaName,
        string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = @schemaName
              AND tablename = @tableName
            ORDER BY indexname;
            """;
        command.Parameters.AddWithValue("schemaName", schemaName);
        command.Parameters.AddWithValue("tableName", tableName);

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private async Task<List<MessageTimelineEventRow>> QueryMessageTimelineEventsAsync(Guid sessionId)
    {
        var connectionString = await _fixture.GetConnectionStringAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT sequence,
                   event_type,
                   message_id,
                   agent_task_id,
                   agent_step_id,
                   approval_request_id,
                   artifact_workspace_id,
                   artifact_id,
                   payload_json
            FROM aigateway.message_events
            WHERE session_id = @sessionId
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("sessionId", sessionId);

        var result = new List<MessageTimelineEventRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new MessageTimelineEventRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return result;
    }

    private sealed record LoginUserDto(string UserName, string Token);

    private sealed record CreatedSessionDto(
        Guid Id,
        string Title,
        DateTimeOffset? OnsiteConfirmedAt,
        string? OnsiteConfirmedBy,
        DateTimeOffset? OnsiteConfirmationExpiresAt);

    private sealed record CreatedLanguageModelDto(Guid Id, string Provider, string Name);

    private sealed record CreatedConversationTemplateDto(Guid Id, string Name);

    private sealed record UploadRecordDto(
        Guid Id,
        string Scope,
        Guid? SessionId,
        Guid? AgentTaskId,
        Guid? KnowledgeBaseId,
        int? RagDocumentId,
        string FileName,
        string ContentType,
        long FileSize,
        string Sha256,
        string Status,
        DateTimeOffset CreatedAt);

    private sealed record AgentTaskDto(
        Guid Id,
        string TaskCode,
        Guid SessionId,
        string Title,
        string Goal,
        string TaskType,
        string Status,
        string RiskLevel,
        Guid? ModelId,
        Guid? WorkspaceId,
        string? WorkspaceCode,
        string PlanJson,
        string? FinalSummary,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? CompletedAt,
        IReadOnlyCollection<AgentStepDto> Steps,
        int PendingApprovalCount,
        string? LastFailureReason,
        bool CanRetry,
        bool IsRunInProgress = false,
        Guid? QueuedRunId = null,
        string? RunQueueStatus = null,
        bool IsRunQueued = false);

    private sealed record AgentStepDto(
        Guid Id,
        int StepIndex,
        string Title,
        string Description,
        string StepType,
        string Status,
        string? ToolCode,
        bool RequiresApproval,
        string? ErrorMessage);

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
        IReadOnlyCollection<ArtifactDto> Artifacts,
        IReadOnlyCollection<ArtifactManifestItemDto> Manifest);

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

    private sealed record ArtifactManifestItemDto(
        Guid ArtifactId,
        string Type,
        string Name,
        string RelativePath,
        string Status,
        int Version,
        int? GeneratedByStep,
        string DownloadUrl,
        DateTimeOffset CreatedAt);

    private sealed record AgentTaskAuditSummaryDto(
        Guid Id,
        Guid TaskId,
        string? WorkspaceCode,
        string ActionCode,
        string TargetType,
        string TargetName,
        string Result,
        string Summary,
        DateTime CreatedAt,
        IReadOnlyDictionary<string, string> Metadata);

    private sealed record SessionTimelinePageDto(
        IReadOnlyList<SessionTimelineEventDto> Items,
        int? BeforeSequence,
        int? AfterSequence,
        bool HasMore,
        bool HasMoreBefore,
        bool HasMoreAfter);

    private sealed record SessionTimelineEventDto(
        int Sequence,
        string EventType,
        DateTimeOffset CreatedAt,
        int? MessageId,
        Guid? AgentTaskId,
        string? AgentTaskTitle,
        string? AgentTaskGoal,
        string? AgentTaskStatus,
        Guid? AgentStepId,
        int? AgentStepIndex,
        string? AgentStepTitle,
        string? AgentStepStatus,
        string? AgentStepToolCode,
        Guid? ApprovalRequestId,
        string? ApprovalType,
        string? ApprovalStatus,
        string? ApprovalTargetName,
        DateTimeOffset? ApprovalDecidedAt,
        Guid? ArtifactWorkspaceId,
        string? WorkspaceCode,
        string? WorkspaceStatus,
        Guid? ArtifactId,
        string? ArtifactName,
        string? ArtifactType,
        string? ArtifactStatus,
        string? ArtifactRelativePath,
        string? ArtifactDownloadUrl);

    private sealed record MessageTimelineEventRow(
        int Sequence,
        string EventType,
        int? MessageId,
        Guid? AgentTaskId,
        Guid? AgentStepId,
        Guid? ApprovalRequestId,
        Guid? ArtifactWorkspaceId,
        Guid? ArtifactId,
        string? PayloadJson);
}
