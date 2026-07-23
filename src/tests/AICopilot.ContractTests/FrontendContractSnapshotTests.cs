using System.Text.Json;
using System.Text;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.RagService.EmbeddingModels;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Result;

namespace AICopilot.ContractTests;

public sealed class FrontendContractSnapshotTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("ReportGeneration", AgentTaskType.ReportGeneration)]
    [InlineData("CloudDataReport", AgentTaskType.CloudDataReport)]
    [InlineData("DataAnalysis", AgentTaskType.DataAnalysis)]
    public void PlanAgentTaskStreamRequest_ShouldAcceptFrontendStringTaskType(
        string taskType,
        AgentTaskType expected)
    {
        var sessionId = Guid.NewGuid();
        var json = $$"""
        {
          "sessionId": "{{sessionId}}",
          "goal": "生成可确认的计划",
          "taskType": "{{taskType}}",
          "modelId": null,
          "pluginSelectionMode": "BuiltInOnly",
          "selectedPluginIds": [],
          "capabilitySelectionMode": "InferredFromGoal",
          "requestedCapabilityCodes": []
        }
        """;

        var request = JsonSerializer.Deserialize<PlanAgentTaskStreamRequest>(json, JsonOptions);

        request.Should().NotBeNull();
        request!.TaskType.Should().Be(expected);
        request.PluginSelectionMode.Should().Be(AgentPluginSelectionMode.BuiltInOnly);
        request.SelectedPluginIds.Should().BeEmpty();
        request.CapabilitySelectionMode.Should().Be(AgentCapabilitySelectionMode.InferredFromGoal);
        request.RequestedCapabilityCodes.Should().BeEmpty();

        Action numericPluginMode = () => JsonSerializer.Deserialize<PlanAgentTaskStreamRequest>(
            json.Replace("\"pluginSelectionMode\": \"BuiltInOnly\"", "\"pluginSelectionMode\": 1", StringComparison.Ordinal),
            JsonOptions);
        Action unknownCapabilityMode = () => JsonSerializer.Deserialize<PlanAgentTaskStreamRequest>(
            json.Replace("\"capabilitySelectionMode\": \"InferredFromGoal\"", "\"capabilitySelectionMode\": \"Unknown\"", StringComparison.Ordinal),
            JsonOptions);

        numericPluginMode.Should().Throw<JsonException>();
        unknownCapabilityMode.Should().Throw<JsonException>();
    }

    [Fact]
    public void AgentTaskDto_ShouldExposeFrontendComputedStateFieldsAndPlanMetadata()
    {
        var taskId = Guid.NewGuid();
        var planJson = """
        {
          "plannerModelId": "11111111-1111-1111-1111-111111111111",
          "plannerValidationVersion": "2026-05",
          "plannerToolCatalogVersion": "2026-05",
          "plannerAvailableToolCount": 3,
          "cloudReadonlyIntents": [
            {
              "intent": "Analysis.Device.Status",
              "confidence": 0.91,
              "querySummary": "设备状态"
            }
          ],
          "steps": [
            {
              "title": "查询设备状态",
              "toolCode": "query_cloud_data_readonly",
              "requiresApproval": true,
              "inputJson": { "target": "Device", "deviceCode": "D-001" }
            }
          ]
        }
        """;

        var dto = new AgentTaskDto(
            Id: taskId,
            TaskCode: "agt_202605170001",
            SessionId: Guid.NewGuid(),
            Title: "设备状态报告",
            Goal: "生成设备状态报告",
            TaskType: "CloudDataReport",
            Status: "WaitingToolApproval",
            RiskLevel: "Medium",
            ModelId: Guid.NewGuid(),
            WorkspaceId: Guid.NewGuid(),
            PlanJson: planJson,
            FinalSummary: null,
            CreatedAt: DateTimeOffset.Parse("2026-05-17T01:00:00Z"),
            UpdatedAt: DateTimeOffset.Parse("2026-05-17T01:05:00Z"),
            CompletedAt: null,
            Steps:
            [
                new AgentStepDto(
                    Id: Guid.NewGuid(),
                    StepIndex: 1,
                    Title: "查询设备状态",
                    Description: "调用 Cloud 只读工具",
                    StepType: "DataQuery",
                    Status: "WaitingApproval",
                    ToolCode: "query_cloud_data_readonly",
                    RequiresApproval: true,
                    ErrorMessage: null)
            ],
            WorkspaceCode: "ws_202605170001",
            PendingApprovalCount: 1,
            LastFailureReason: null,
            CanRun: true,
            CanRetry: false,
            CanSubmitFinalReview: false,
            CanApproveFinal: false,
            FailureSummary: null,
            ActiveRunAttemptId: Guid.NewGuid(),
            RunAttemptCount: 1,
            IsRunInProgress: true,
            QueuedRunId: Guid.NewGuid(),
            RunQueueStatus: "Leased",
            IsRunQueued: true,
            PlanSchemaVersion: "2.0",
            PlanDigest: new string('a', 64),
            TopologyProfile: "LinearV1",
            IsPlanExecutable: true,
            PlanIntegrityStatus: "ValidV2");

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        AssertProperties(
            root,
            "id",
            "taskCode",
            "sessionId",
            "title",
            "goal",
            "taskType",
            "status",
            "riskLevel",
            "workspaceCode",
            "planJson",
            "pendingApprovalCount",
            "canRun",
            "canRetry",
            "canSubmitFinalReview",
            "canApproveFinal",
            "activeRunAttemptId",
            "runAttemptCount",
            "isRunInProgress",
            "queuedRunId",
            "runQueueStatus",
            "isRunQueued",
            "planSchemaVersion",
            "planDigest",
            "topologyProfile",
            "isPlanExecutable",
            "planIntegrityStatus",
            "steps");
        var serializedPlan = root.GetProperty("planJson").GetString();
        serializedPlan.Should().Contain("\"plannerModelId\"");
        serializedPlan.Should().Contain("\"plannerValidationVersion\"");
        serializedPlan.Should().Contain("\"plannerToolCatalogVersion\"");
        serializedPlan.Should().Contain("\"plannerAvailableToolCount\"");
        serializedPlan.Should().Contain("\"cloudReadonlyIntents\"");
        serializedPlan.Should().Contain("\"inputJson\"");

        var boundaryPrefix = "{\"schemaVersion\":\"2.0\",\"padding\":\"";
        const string boundarySuffix = "\"}";
        var boundaryPlan = boundaryPrefix +
                           new string('x', 262_144 - boundaryPrefix.Length - boundarySuffix.Length) +
                           boundarySuffix;
        var boundaryJson = JsonSerializer.Serialize(dto with { PlanJson = boundaryPlan }, JsonOptions);
        using var boundaryDocument = JsonDocument.Parse(boundaryJson);
        var roundTripPlan = boundaryDocument.RootElement.GetProperty("planJson").GetString();
        System.Text.Encoding.UTF8.GetByteCount(boundaryPlan).Should().Be(262_144);
        roundTripPlan.Should().Be(boundaryPlan);
    }

    [Fact]
    public void PlanStreamError_ShouldPreservePlanPayloadTooLargeCodeForFrontend()
    {
        var cases = new[]
        {
            (AppProblemCodes.AgentPlanInvalid, "Agent task plan failed integrity validation."),
            (AppProblemCodes.AgentPlanSchemaInvalid, "Agent task plan does not match the required schema."),
            (AppProblemCodes.PlanPayloadTooLarge, "Agent task plan exceeds the maximum allowed size of 262144 UTF-8 bytes.")
        };

        foreach (var (code, expectedDetail) in cases)
        {
            Result<AgentTaskDto> failure = Result.Failure(
                new ApiProblemDescriptor(
                    "agent_plan_internal_roundtrip_failed",
                    "first-result-opaque-secret-7a1b raw owner=internal-result-controlled"),
                new ApiProblemDescriptor(
                    code,
                    "opaque-plan-secret-9f4c raw owner=node-controlled SELECT payroll"));

            var eventChunk = PlanAgentTaskStreamHandler.CreateFailureEventChunk(failure);
            var chunk = PlanAgentTaskStreamHandler.CreateProblemChunk(new StringBuilder(), failure);
            using var eventPayload = JsonDocument.Parse(eventChunk.Content);
            using var payload = JsonDocument.Parse(chunk.Content);

            eventChunk.Type.Should().Be(ChunkType.AgentEvent);
            chunk.Type.Should().Be(ChunkType.Error);
            (eventChunk.Content + chunk.Content).Should().NotContain("first-result-opaque-secret-7a1b")
                .And.NotContain("internal-result-controlled")
                .And.NotContain("opaque-plan-secret-9f4c")
                .And.NotContain("node-controlled")
                .And.NotContain("payroll");
            eventPayload.RootElement.GetProperty("stage").GetString().Should().Be("plan_draft_failed");
            eventPayload.RootElement.GetProperty("code").GetString().Should().Be(code);
            eventPayload.RootElement.GetProperty("detail").GetString().Should().Be(expectedDetail);
            eventPayload.RootElement.GetProperty("recoverable").GetBoolean().Should().BeTrue();
            eventPayload.RootElement.GetProperty("suggestedAction").GetString().Should()
                .Be("Adjust the goal or model configuration, then retry PlanDraft generation.");
            eventPayload.RootElement.GetProperty("metadata").EnumerateObject().Should().BeEmpty();
            foreach (var pascalCaseProperty in new[]
                     {
                         "Stage", "Code", "Detail", "Recoverable", "SuggestedAction", "Metadata"
                     })
            {
                eventPayload.RootElement.TryGetProperty(pascalCaseProperty, out _).Should().BeFalse();
            }
            payload.RootElement.GetProperty("code").GetString().Should().Be(code);
            payload.RootElement.GetProperty("detail").GetString().Should().Be(expectedDetail);
        }

        Result<AgentTaskDto> ordinaryFailure = Result.Failure(
            new ApiProblemDescriptor("ordinary_first_failure", "ordinary first detail"),
            new ApiProblemDescriptor("ordinary_second_failure", "ordinary second detail"));
        var ordinaryEvent = PlanAgentTaskStreamHandler.CreateFailureEventChunk(ordinaryFailure);
        var ordinaryError = PlanAgentTaskStreamHandler.CreateProblemChunk(new StringBuilder(), ordinaryFailure);
        using var ordinaryEventPayload = JsonDocument.Parse(ordinaryEvent.Content);
        using var ordinaryErrorPayload = JsonDocument.Parse(ordinaryError.Content);
        ordinaryEventPayload.RootElement.GetProperty("code").GetString().Should().Be("ordinary_first_failure");
        ordinaryEventPayload.RootElement.GetProperty("detail").GetString().Should().Be("ordinary first detail");
        ordinaryErrorPayload.RootElement.GetProperty("code").GetString().Should().Be("ordinary_first_failure");
        ordinaryErrorPayload.RootElement.GetProperty("detail").GetString().Should().Be("ordinary first detail");

        Result<AgentTaskDto> stringOnlyFailure = Result.Failure(
            "opaque-string-only-plan-secret-5566 raw owner=string-result-controlled");
        var stringOnlyEvent = PlanAgentTaskStreamHandler.CreateFailureEventChunk(stringOnlyFailure);
        var stringOnlyError = PlanAgentTaskStreamHandler.CreateProblemChunk(new StringBuilder(), stringOnlyFailure);
        using var stringOnlyEventPayload = JsonDocument.Parse(stringOnlyEvent.Content);
        using var stringOnlyErrorPayload = JsonDocument.Parse(stringOnlyError.Content);
        (stringOnlyEvent.Content + stringOnlyError.Content).Should()
            .NotContain("opaque-string-only-plan-secret-5566")
            .And.NotContain("string-result-controlled");
        stringOnlyEventPayload.RootElement.GetProperty("code").GetString()
            .Should().Be(AppProblemCodes.AgentPlanInvalid);
        stringOnlyEventPayload.RootElement.GetProperty("detail").GetString()
            .Should().Be("Agent task plan failed integrity validation.");
        stringOnlyErrorPayload.RootElement.GetProperty("code").GetString()
            .Should().Be(AppProblemCodes.AgentPlanInvalid);
        stringOnlyErrorPayload.RootElement.GetProperty("detail").GetString()
            .Should().Be("Agent task plan failed integrity validation.");

        Result<AgentTaskDto> emptyFailure = Result.Failure();
        var emptyEvent = PlanAgentTaskStreamHandler.CreateFailureEventChunk(emptyFailure);
        using var emptyEventPayload = JsonDocument.Parse(emptyEvent.Content);
        emptyEventPayload.RootElement.GetProperty("code").GetString()
            .Should().Be(AppProblemCodes.AgentPlanInvalid);
        emptyEventPayload.RootElement.GetProperty("detail").GetString()
            .Should().Be("Agent task plan failed integrity validation.");

        var aggregate = new AggregateException(
            new AgentTaskPlanPersistenceIntegrityException(
                Guid.NewGuid(),
                "agent_plan_internal_roundtrip_failed",
                "first-inner-opaque-secret"),
            new AgentTaskPlanPersistenceIntegrityException(
                Guid.NewGuid(),
                AppProblemCodes.AgentPlanSchemaInvalid,
                "non-first-plan-opaque-secret raw owner=aggregate-controlled"));
        var aggregateEvent = PlanAgentTaskStreamHandler.CreateFailureEventChunk(aggregate);
        var aggregateError = AgentStreamRuntime.CreateErrorChunk(
            aggregate,
            "test",
            AppProblemCodes.ChatStreamFailed,
            "opaque-fallback-user-secret");
        using var aggregateEventPayload = JsonDocument.Parse(aggregateEvent.Content);
        using var aggregateErrorPayload = JsonDocument.Parse(aggregateError.Content);

        aggregateEventPayload.RootElement.GetProperty("stage").GetString().Should().Be("plan_draft_failed");
        aggregateEventPayload.RootElement.GetProperty("code").GetString()
            .Should().Be(AppProblemCodes.AgentPlanSchemaInvalid);
        aggregateErrorPayload.RootElement.GetProperty("code").GetString()
            .Should().Be(AppProblemCodes.AgentPlanSchemaInvalid);
        aggregateEventPayload.RootElement.GetProperty("detail").GetString()
            .Should().Be("Agent task plan does not match the required schema.");
        aggregateEventPayload.RootElement.GetProperty("recoverable").GetBoolean().Should().BeTrue();
        aggregateEventPayload.RootElement.GetProperty("suggestedAction").GetString().Should()
            .Be("Retry plan draft generation after checking model and session state.");
        aggregateEventPayload.RootElement.GetProperty("metadata").EnumerateObject().Should().BeEmpty();
        foreach (var pascalCaseProperty in new[]
                 {
                     "Stage", "Code", "Detail", "Recoverable", "SuggestedAction", "Metadata"
                 })
        {
            aggregateEventPayload.RootElement.TryGetProperty(pascalCaseProperty, out _).Should().BeFalse();
        }
        aggregateErrorPayload.RootElement.GetProperty("detail").GetString()
            .Should().Be("Agent task plan does not match the required schema.");
        (aggregateEvent.Content + aggregateError.Content).Should().NotContain("first-inner-opaque-secret")
            .And.NotContain("non-first-plan-opaque-secret")
            .And.NotContain("aggregate-controlled")
            .And.NotContain("opaque-fallback-user-secret");
    }

    [Fact]
    public void WorkspaceAndArtifactDtos_ShouldExposeManifestAndDownloadUrlsFromBackend()
    {
        var artifactId = Guid.NewGuid();
        var artifact = new ArtifactDto(
            Id: artifactId,
            Name: "设备状态报告.md",
            Type: "Markdown",
            Status: "Draft",
            RelativePath: "draft/report-attempt-1.md",
            FileSize: 2048,
            MimeType: "text/markdown",
            Version: 1,
            UpdatedAt: DateTimeOffset.Parse("2026-05-17T01:10:00Z"),
            PreviewKind: "markdown",
            DownloadUrl: $"/api/aigateway/artifact/{artifactId}/download",
            GeneratedByStepOrder: 2,
            RequiresApproval: true,
            ApprovalStatus: "Pending",
            FinalizedAt: null,
            ArtifactVersion: 1,
            ArtifactStatus: "Draft",
            SourceMode: "SimulationBusiness",
            Boundary: "SimulationBusiness",
            IsSimulation: true,
            IsSandbox: false,
            SourceLabel: "AI 独立模拟业务库",
            QueryHash: "query-hash",
            ResultHash: "result-hash",
            RowCount: 12,
            IsTruncated: false)
        {
            CreatedAt = DateTimeOffset.Parse("2026-05-17T01:10:00Z"),
            GeneratedByStep = 2
        };
        var workspace = new ArtifactWorkspaceDto(
            Id: Guid.NewGuid(),
            WorkspaceCode: "ws_202605170001",
            TaskId: Guid.NewGuid(),
            Status: "Draft",
            Files:
            [
                new ArtifactWorkspaceFileDto(
                    "report-attempt-1.md",
                    "draft/report-attempt-1.md",
                    IsDirectory: false,
                    FileSize: 2048,
                    UpdatedAt: DateTimeOffset.Parse("2026-05-17T01:10:00Z"))
            ],
            Artifacts: [artifact])
        {
            DraftArtifacts = [artifact],
            FinalArtifacts = [],
            Manifest =
            [
                new ArtifactManifestItemDto(
                    ArtifactId: artifactId,
                    Type: "Markdown",
                    Name: "设备状态报告.md",
                    RelativePath: "draft/report-attempt-1.md",
                    Status: "Draft",
                    Version: 1,
                    GeneratedByStep: 2,
                    DownloadUrl: $"/api/aigateway/artifact/{artifactId}/download",
                    CreatedAt: DateTimeOffset.Parse("2026-05-17T01:10:00Z"))
            ]
        };

        var json = JsonSerializer.Serialize(workspace, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        AssertProperties(root, "id", "workspaceCode", "taskId", "status", "files", "artifacts", "manifest");
        AssertProperties(root, "draftArtifacts", "finalArtifacts");
        AssertProperties(
            root.GetProperty("artifacts")[0],
            "artifactVersion",
            "artifactStatus",
            "sourceMode",
            "boundary",
            "isSimulation",
            "isSandbox",
            "sourceLabel",
            "queryHash",
            "resultHash",
            "rowCount",
            "isTruncated");
        var manifestItem = root.GetProperty("manifest")[0];
        AssertProperties(
            manifestItem,
            "artifactId",
            "type",
            "name",
            "relativePath",
            "status",
            "version",
            "generatedByStep",
            "downloadUrl",
            "createdAt");
        manifestItem.GetProperty("downloadUrl").GetString()
            .Should()
            .Be($"/api/aigateway/artifact/{artifactId}/download");
    }

    [Fact]
    public void ToolQueueWorkerDtos_ShouldExposeOperationalContractFields()
    {
        var now = DateTimeOffset.Parse("2026-05-17T01:15:00Z");
        var taskId = Guid.NewGuid();
        var record = new ToolExecutionRecordDto(
            Id: Guid.NewGuid(),
            TaskId: taskId,
            StepId: Guid.NewGuid(),
            RunAttemptId: Guid.NewGuid(),
            ToolCode: "query_cloud_data_readonly",
            InputSummary: """{"target":"Device","deviceCode":"D-001"}""",
            OutputSummary: """{"rowCount":1,"truncated":false}""",
            Status: "Succeeded",
            StartedAt: now,
            CompletedAt: now.AddSeconds(2),
            DurationMs: 2000,
            ErrorCode: null,
            ErrorMessage: null,
            ArtifactId: null,
            AuditMetadata: """{"providerType":"CloudReadonly","auditLevel":"Full"}""");
        var registration = new ToolRegistrationDto(
            Id: Guid.NewGuid(),
            ToolCode: "query_cloud_data_readonly",
            DisplayName: "Cloud 只读查询",
            Description: "受控 Cloud 只读工具",
            ProviderType: "CloudReadonly",
            TargetType: "CloudAiRead",
            TargetName: "CloudAiRead",
            InputSchemaJson: """{"type":"object","required":["target"]}""",
            OutputSchemaJson: """{"type":"object"}""",
            RiskLevel: "RequiresApproval",
            RequiredPermission: "AiGateway.RunAgentTask",
            RequiresApproval: true,
            IsEnabled: false,
            TimeoutSeconds: 60,
            AuditLevel: "Full",
            Category: "CloudReadonly",
            BusinessDomains: [],
            DataBoundary: "NoData",
            IsVisibleToPlanner: false,
            IsExecutableByAgent: false,
            SchemaVersion: 1,
            CatalogVersion: 4,
            ApprovalPolicy: "DisabledRealCloudReadonly",
            CreatedAt: now,
            UpdatedAt: now,
            RuntimeAvailable: true,
            LastDiscoveredAt: null,
            SourceServerName: null);
        var queueItem = new AgentRunQueueItemDto(
            Id: Guid.NewGuid(),
            TaskId: taskId,
            TriggerType: "Manual",
            Status: "Queued",
            RequestedBy: Guid.NewGuid(),
            RunAttemptId: null,
            LeaseId: null,
            LeaseOwner: null,
            LeaseExpiresAt: null,
            AvailableAt: now,
            StartedAt: null,
            CompletedAt: null,
            FailureCode: null,
            SafeMessage: null,
            CreatedAt: now,
            UpdatedAt: now);
        var summary = new AgentRunQueueSummaryDto(
            QueuedCount: 1,
            LeasedCount: 0,
            SucceededCount: 2,
            FailedCount: 0,
            CancelledCount: 0,
            DeadLetterCount: 0,
            StaleLeasedCount: 0,
            OldestQueuedAt: now,
            AverageWaitMs: 250,
            AverageRunMs: 500,
            OldestQueuedWaitMs: 1000,
            ActiveWorkerCount: 1,
            WorkspaceMismatchCount: 0,
            GeneratedAt: now);
        var worker = new AgentWorkerStatusDto(
            StatusCode: "healthy",
            HasActiveWorkers: true,
            WorkspaceConsistent: true,
            HttpApiWorkspaceRootHash: "sha256:workspace-httpapi",
            ActiveWorkerCount: 1,
            QueuedCount: 1,
            LeasedCount: 0,
            StaleLeasedCount: 0,
            OldestQueuedAt: now,
            GeneratedAt: now,
            Workers:
            [
                new AgentWorkerHeartbeatDto(
                    Id: Guid.NewGuid(),
                    WorkerId: "worker-1",
                    WorkerName: "AICopilot.DataWorker",
                    StartedAt: now.AddMinutes(-10),
                    LastSeenAt: now,
                    IsActive: true,
                    ActiveQueueItemId: queueItem.Id,
                    ActiveTaskId: taskId,
                    WorkspaceRootHash: "sha256:workspace-httpapi",
                    Version: "test",
                    WorkspaceMatchesHttpApi: true)
            ]);

        var json = JsonSerializer.Serialize(new { record, registration, queueItem, summary, worker }, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        AssertProperties(root.GetProperty("record"), "runAttemptId", "toolCode", "inputSummary", "outputSummary", "status", "auditMetadata");
        AssertProperties(root.GetProperty("registration"), "toolCode", "providerType", "targetType", "inputSchemaJson", "requiresApproval", "runtimeAvailable", "sourceServerName");
        AssertProperties(root.GetProperty("queueItem"), "id", "taskId", "triggerType", "status", "runAttemptId", "leaseExpiresAt", "safeMessage");
        AssertProperties(root.GetProperty("summary"), "queuedCount", "leasedCount", "failedCount", "deadLetterCount", "staleLeasedCount", "oldestQueuedAt", "averageWaitMs", "averageRunMs", "oldestQueuedWaitMs", "activeWorkerCount", "workspaceMismatchCount");
        AssertProperties(root.GetProperty("worker"), "statusCode", "hasActiveWorkers", "workspaceConsistent", "httpApiWorkspaceRootHash", "workers");
        root.GetProperty("worker").GetProperty("workers")[0].TryGetProperty("workspaceMatchesHttpApi", out _)
            .Should()
            .BeTrue();
    }

    private static void AssertProperties(JsonElement element, params string[] properties)
    {
        foreach (var property in properties)
        {
            element.TryGetProperty(property, out _)
                .Should()
                .BeTrue($"contract JSON should contain {property}");
        }
    }
}

public sealed class ContractSecretRedactionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void FrontendContractExamples_ShouldNotExposeSecretsOrInternalPaths()
    {
        var now = DateTimeOffset.Parse("2026-05-17T01:20:00Z");
        var sample = new
        {
            languageModel = new LanguageModelDto
            {
                Id = Guid.NewGuid(),
                Provider = "OpenAICompatible",
                ProtocolType = "OpenAICompatible",
                Name = "planner",
                BaseUrl = "https://models.example.invalid/v1",
                MaxTokens = 8192,
                ContextWindowTokens = 8192,
                MaxOutputTokens = 2048,
                Temperature = 0.2,
                IsEnabled = true,
                Usages = ["Chat", "Planner"],
                HasApiKey = true,
                ApiKeyPreview = "******",
                ConnectivityStatus = "Ready"
            },
            embeddingModel = new EmbeddingModelDto
            {
                Id = Guid.NewGuid(),
                Name = "embedding",
                Provider = "OpenAICompatible",
                BaseUrl = "https://embeddings.example.invalid/v1",
                ModelName = "text-embedding-contract",
                Dimensions = 1536,
                MaxTokens = 8191,
                IsEnabled = true,
                HasApiKey = true,
                ApiKeyPreview = "******"
            },
            toolExecution = new ToolExecutionRecordDto(
                Id: Guid.NewGuid(),
                TaskId: Guid.NewGuid(),
                StepId: Guid.NewGuid(),
                RunAttemptId: Guid.NewGuid(),
                ToolCode: "render_markdown",
                InputSummary: "apiKey=******; token=******; path=[redacted-path]",
                OutputSummary: "[redacted-sql]; rows=3; truncated=false",
                Status: "Failed",
                StartedAt: now,
                CompletedAt: now.AddSeconds(1),
                DurationMs: 1000,
                ErrorCode: AppProblemCodes.ArtifactGenerationFailed,
                ErrorMessage: "connection string=******; [redacted-path]",
                ArtifactId: null,
                AuditMetadata: """{"providerType":"BuiltIn","auditLevel":"Standard"}"""),
            workerStatus = new AgentWorkerStatusDto(
                StatusCode: "workspace_mismatch",
                HasActiveWorkers: true,
                WorkspaceConsistent: false,
                HttpApiWorkspaceRootHash: "sha256:httpapi",
                ActiveWorkerCount: 1,
                QueuedCount: 0,
                LeasedCount: 0,
                StaleLeasedCount: 0,
                OldestQueuedAt: null,
                GeneratedAt: now,
                Workers:
                [
                    new AgentWorkerHeartbeatDto(
                        Id: Guid.NewGuid(),
                        WorkerId: "worker-1",
                        WorkerName: "AICopilot.DataWorker",
                        StartedAt: now.AddMinutes(-5),
                        LastSeenAt: now,
                        IsActive: false,
                        ActiveQueueItemId: null,
                        ActiveTaskId: null,
                        WorkspaceRootHash: "sha256:worker",
                        Version: "test",
                        WorkspaceMatchesHttpApi: false)
                ]),
            ragPermissionDenied = new
            {
                code = AuthProblemCodes.MissingPermission,
                message = "Resource was not found or is not visible."
            }
        };

        var json = JsonSerializer.Serialize(sample, JsonOptions);
        json.Should().Contain("\"hasApiKey\":true");
        json.Should().Contain("\"apiKeyPreview\":\"******\"");
        json.Should().NotContain("\"apiKey\":");
        json.Should().NotContain("apiKeyMasked");
        json.Should().NotContain("sk-live");
        json.Should().NotContain("Bearer ");
        json.Should().NotContain("Password=");
        json.Should().NotContain("Host=");
        json.Should().NotContain("Server=");
        json.Should().NotContain("C:\\");
        json.Should().NotContain("SELECT ");
        json.Should().NotContain("FROM ");
        json.Should().NotContain("device_logs");
    }
}
