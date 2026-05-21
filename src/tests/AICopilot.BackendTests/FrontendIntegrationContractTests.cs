using System.Reflection;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.RagService.EmbeddingModels;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Result;

namespace AICopilot.BackendTests;

[Trait("Suite", "FrontendIntegrationContract")]
public sealed class OpenApiContractTests(OpenApiContractFixture fixture)
    : IClassFixture<OpenApiContractFixture>
{
    [Fact]
    public async Task OpenApi_ShouldExposeStableAigatewayAndRagRoutes()
    {
        using var response = await fixture.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        AssertPath(document, "/api/aigateway/language-model/list", "get");
        AssertPath(document, "/api/aigateway/runtime-settings", "get");
        AssertPath(document, "/api/aigateway/session", "post");
        AssertPath(document, "/api/aigateway/session/list", "get");
        AssertPath(document, "/api/aigateway/upload", "post");
        AssertPath(document, "/api/aigateway/agent/trial-scenarios", "get");
        AssertPath(document, "/api/aigateway/agent/trial-scenarios/create-task", "post");
        AssertPath(document, "/api/aigateway/agent/task/plan", "post");
        AssertPath(document, "/api/aigateway/agent/cloud-sandbox-controlled-trial/plan", "post");
        AssertPath(document, "/api/aigateway/agent/cloud-production-controlled-pilot/plan", "post");
        AssertPath(document, "/api/aigateway/agent/task/run", "post");
        AssertPath(document, "/api/aigateway/agent/task/retry", "post");
        AssertPath(document, "/api/aigateway/agent/task/cancel", "post");
        AssertPath(document, "/api/aigateway/agent/task/{id}/approvals", "get");
        AssertPath(document, "/api/aigateway/agent/task/{id}/audit-summary", "get");
        AssertPath(document, "/api/aigateway/agent/task/{id}/tool-executions", "get");
        AssertPath(document, "/api/aigateway/agent/task/{id}/run-attempts", "get");
        AssertPath(document, "/api/aigateway/agent/task/{id}/run-queue", "get");
        AssertPath(document, "/api/aigateway/agent/run-queue", "get");
        AssertPath(document, "/api/aigateway/agent/run-queue/summary", "get");
        AssertPath(document, "/api/aigateway/agent/worker/status", "get");
        AssertPath(document, "/api/aigateway/tools", "get");
        AssertPath(document, "/api/aigateway/tools/{toolCode}", "get");
        AssertPath(document, "/api/aigateway/tools/{toolCode}", "patch");
        AssertPath(document, "/api/aigateway/workspace/{code}", "get");
        AssertPath(document, "/api/aigateway/artifact/{id}/download", "get");
        AssertPath(document, "/api/aigateway/artifact/{id}/preview", "get");
        AssertPath(document, "/api/aigateway/artifact/{id}/revision-comment", "post");
        AssertPath(document, "/api/aigateway/artifact/{id}/regenerate-draft", "post");
        AssertPath(document, "/api/aigateway/artifact/{id}/submit-final-approval", "post");
        AssertPath(document, "/api/rag/embedding-model/list", "get");
        AssertPath(document, "/api/rag/knowledge-base/list", "get");
        AssertPath(document, "/api/rag/document", "post");
        AssertPath(document, "/api/rag/document/list", "get");
        AssertPath(document, "/api/rag/document/governance", "put");
        AssertPath(document, "/api/rag/search", "post");
    }

    private static void AssertPath(JsonDocument document, string path, string method)
    {
        var paths = document.RootElement.GetProperty("paths");
        var availablePaths = paths
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        paths.TryGetProperty(path, out var pathElement)
            .Should()
            .BeTrue($"OpenAPI should expose {path}; available paths: {string.Join(", ", availablePaths)}");
        pathElement.TryGetProperty(method, out _)
            .Should()
            .BeTrue($"OpenAPI should expose {method.ToUpperInvariant()} {path}");
    }

    [Theory]
    [InlineData("GET", "/api/aigateway/cloud-readonly/readiness")]
    [InlineData("GET", "/api/aigateway/cloud-readonly/readiness/history")]
    [InlineData("GET", "/api/aigateway/cloud-readonly/readiness/sandbox")]
    [InlineData("GET", "/api/aigateway/cloud-readonly/readiness/sandbox/history")]
    [InlineData("GET", "/api/aigateway/cloud-readonly/readiness/sandbox-agent-trial")]
    [InlineData("GET", "/api/aigateway/cloud-readonly/readiness/sandbox-controlled-trial")]
    [InlineData("GET", "/api/aigateway/cloud-readonly/readiness/pilot-readiness")]
    [InlineData("GET", "/api/aigateway/cloud-readonly/readiness/production-pilot")]
    [InlineData("GET", "/api/aigateway/cloud-readonly/readiness/production-controlled-pilot")]
    [InlineData("GET", "/api/aigateway/cloud-readonly/readiness/production-operations")]
    [InlineData("GET", "/api/aigateway/cloud-readonly/readiness/production-operations/ledger")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/sandbox-agent-trial/run")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/pilot-readiness/config-package")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/pilot-readiness/gate")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/pilot-readiness/approval-rehearsal")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/pilot-readiness/contract-rehearsal")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/production-pilot/window")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/production-pilot/window/status")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/production-pilot/gate")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/production-pilot/run")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/production-controlled-pilot/run")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/production-operations/emergency-stop")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/production-operations/emergency-stop/clear")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/production-operations/incidents")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/production-operations/ga-readiness")]
    [InlineData("POST", "/api/aigateway/cloud-readonly/readiness/run")]
    public async Task CloudReadonlyReadinessRoutes_ShouldBeRoutable(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(new { mode = "FakeEndpoint" });
        }

        using var response = await fixture.HttpClient.SendAsync(request);

        response.StatusCode.Should().NotBe(
            System.Net.HttpStatusCode.NotFound,
            "P5 CloudReadonly readiness route should be registered even before authentication succeeds");
    }
}

public sealed class OpenApiContractFixture : AICopilotAppFixture
{
    protected override bool EnableRagWorker => false;

    protected override bool EnableDataWorker => false;
}

[Trait("Suite", "FrontendIntegrationContract")]
public sealed class FrontendContractSnapshotTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AgentTaskDto_ShouldExposeFrontendComputedStateFieldsAndPlanMetadata()
    {
        var taskId = Guid.NewGuid();
        var planJson = """
        {
          "plannerMode": "Dynamic",
          "plannerModelId": "11111111-1111-1111-1111-111111111111",
          "plannerValidationVersion": "2026-05",
          "plannerToolCatalogVersion": "2026-05",
          "plannerAvailableToolCount": 3,
          "cloudReadonlyIntent": {
            "intent": "Analysis.Device.Status",
            "confidence": 0.91,
            "querySummary": "设备状态"
          },
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
            IsRunQueued: true);

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
            "steps");
        var serializedPlan = root.GetProperty("planJson").GetString();
        serializedPlan.Should().Contain("\"plannerMode\"");
        serializedPlan.Should().Contain("\"plannerModelId\"");
        serializedPlan.Should().Contain("\"plannerValidationVersion\"");
        serializedPlan.Should().Contain("\"plannerToolCatalogVersion\"");
        serializedPlan.Should().Contain("\"plannerAvailableToolCount\"");
        serializedPlan.Should().Contain("\"cloudReadonlyIntent\"");
        serializedPlan.Should().Contain("\"inputJson\"");
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

[Trait("Suite", "FrontendIntegrationContract")]
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

[Trait("Suite", "FrontendIntegrationContract")]
public sealed class ErrorCodeCatalogTests
{
    [Fact]
    public void ContractDocument_ShouldListBackendProblemCodes()
    {
        var document = File.ReadAllText(FindContractDocumentPath());
        var problemCodes = typeof(AuthProblemCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Concat(typeof(AppProblemCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            .Concat(typeof(CloudAiReadProblemCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        problemCodes.Should().NotBeEmpty();
        foreach (var problemCode in problemCodes)
        {
            document.Should().Contain($"`{problemCode}`");
        }
    }

    private static string FindContractDocumentPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "docs",
                "frontend-integration-contract-package-2026-05-17.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("AICopilot frontend integration contract document was not found.");
    }
}
