using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Uploads;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AICopilot.AgentWorkflowTestKit;

namespace AICopilot.ApplicationTests;

public sealed class ToolRegistryApplicationTests : ToolRegistryGovernanceTestBase
{
    [Fact]
    public async Task ToolRegistryGuard_ShouldRejectMissingDisabledBlockedAndUnauthorizedTools()
    {
        var missing = await CreateGuard().ValidateAsync("missing_tool", UserId, CancellationToken.None);
        missing.IsAllowed.Should().BeFalse();
        missing.Problem!.Code.Should().Be(AppProblemCodes.ToolNotRegistered);

        var disabled = await CreateGuard(CreateTool("disabled_tool", isEnabled: false))
            .ValidateAsync("disabled_tool", UserId, CancellationToken.None);
        disabled.IsAllowed.Should().BeFalse();
        disabled.Problem!.Code.Should().Be(AppProblemCodes.ToolDisabled);

        var blocked = await CreateGuard(CreateTool("blocked_tool", riskLevel: AiToolRiskLevel.Blocked))
            .ValidateAsync("blocked_tool", UserId, CancellationToken.None);
        blocked.IsAllowed.Should().BeFalse();
        blocked.Problem!.Code.Should().Be(AppProblemCodes.ToolBlocked);

        var guardedTool = CreateTool("manage_tool", requiredPermission: "AiGateway.ToolRegistry.Manage");
        var unauthorized = await CreateGuard(guardedTool)
            .ValidateAsync("manage_tool", UserId, CancellationToken.None);
        unauthorized.IsAllowed.Should().BeFalse();
        unauthorized.Problem!.Code.Should().Be(AppProblemCodes.ToolPermissionDenied);

        var authorized = await CreateGuard(guardedTool, "AiGateway.ToolRegistry.Manage")
            .ValidateAsync("manage_tool", UserId, CancellationToken.None);
        authorized.IsAllowed.Should().BeTrue();
    }
    [Fact]
    public async Task ToolRegistryGuard_ShouldReturnStableCode_WhenCloudReadonlyToolIsDisabled()
    {
        var tool = CreateTool(
            "query_cloud_data_readonly",
            ToolProviderType.CloudReadonly,
            isEnabled: false,
            requiresApproval: true,
            riskLevel: AiToolRiskLevel.RequiresApproval);

        var result = await CreateGuard(tool).ValidateAsync(tool.ToolCode, UserId, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.Problem!.Code.Should().Be(AppProblemCodes.CloudReadonlyToolDisabled);
    }
    [Fact]
    public async Task PlanAgentTask_ShouldCreateDraft_WhenCloudReadonlyToolIsDisabled_AndRejectOnConfirm()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var taskRepository = new InMemoryRepository<AgentTask>();
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var disabledToolGuard = CreateGuard(CreateTool(
            "query_cloud_data_readonly",
            ToolProviderType.CloudReadonly,
            isEnabled: false,
            requiresApproval: true,
            riskLevel: AiToolRiskLevel.RequiresApproval));
        var handler = new PlanAgentTaskCommandHandler(
            new PlanAgentTaskCoordinator(
                taskRepository,
                new AgentTaskPlanPreparationService(
                    new InMemoryRepository<Session>(session),
                    new InMemoryRepository<UploadRecord>(),
                    [],
                    null),
                new AgentAuditRecorder(new CapturingAuditLogWriter()),
                new TestCurrentUser(UserId),
                planToolGuard: CreatePlanToolGuard(disabledToolGuard),
                cloudReadonlyPlanService: new FixedCloudReadonlyAgentPlanService()));

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "生成 Cloud 生产数据报告", AgentTaskType.CloudDataReport, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.WorkspaceId.Should().BeNull();
        result.Value.WorkspaceCode.Should().BeNull();
        var task = taskRepository.Items.Should().ContainSingle().Which;
        task.Status.Should().Be(AgentTaskStatus.Draft);
        task.WorkspaceId.Should().BeNull();
        using var plan = JsonDocument.Parse(task.PlanJson);
        plan.RootElement.GetProperty("planKind").GetString().Should().Be("PlanDraft");
        plan.RootElement.GetProperty("isExecutable").GetBoolean().Should().BeFalse();
        plan.RootElement.GetProperty("capabilityGaps").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(AgentPlanCapabilityGapCodes.PlannedToolUnavailable)
            .And.Contain(AgentPlanCapabilityGapCodes.ExecutionSnapshotUnavailable);

        var approvalAudit = new CapturingAuditLogWriter();
        var confirmationService = new AgentPlanDraftConfirmationService(
            CreatePlanToolGuard(disabledToolGuard),
            AgentPlanV2TestData.CreateMatchingFreshReadGate(),
            AgentPlanV2TestData.CreateMatchingRoutingSnapshotReader());
        var approveHandler = new ApproveAgentTaskPlanCommandHandler(
            taskRepository,
            approvalRepository,
            CreateAgentTaskDtoQueryService(
                new InMemoryRepository<ArtifactWorkspace>(),
                approvalRepository,
                new InMemoryAgentTaskRunQueueStore()),
            new AgentAuditRecorder(approvalAudit),
            new TestCurrentUser(UserId),
            confirmationService);

        var confirmation = await approveHandler.Handle(
            new ApproveAgentTaskPlanCommand(task.Id.Value),
            CancellationToken.None);

        confirmation.IsSuccess.Should().BeFalse();
        confirmation.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "A PlanDraft with unresolved capability gaps cannot be confirmed."));
        task.Status.Should().Be(AgentTaskStatus.Draft);
        approvalRepository.Items.Should().ContainSingle()
            .Which.Status.Should().Be(AgentApprovalStatus.Pending);
        approvalAudit.Requests.Should().BeEmpty();
    }
    [Fact]
    public async Task ConfirmPlanDraft_ShouldRejectCloudDataReport_WhenCloudReadonlyIntentIsUnsupported()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var taskRepository = new InMemoryRepository<AgentTask>();
        var cloudReadonlyPlanService = new FixedCloudReadonlyAgentPlanService(Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.CloudReadonlyIntentUnsupported,
            "Recipe data is outside the Cloud readonly agent boundary.")));
        var handler = new PlanAgentTaskCommandHandler(
            new PlanAgentTaskCoordinator(
                taskRepository,
                new AgentTaskPlanPreparationService(
                    new InMemoryRepository<Session>(session),
                    new InMemoryRepository<UploadRecord>(),
                    [],
                    null),
                new AgentAuditRecorder(new CapturingAuditLogWriter()),
                new TestCurrentUser(UserId),
                planToolGuard: CreatePlanToolGuard(CreateAgentRuntimeGuardWithCloudEnabled()),
                cloudReadonlyPlanService: cloudReadonlyPlanService));

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "read recipe version details", AgentTaskType.CloudDataReport, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        using var plan = JsonDocument.Parse(result.Value!.PlanJson);
        plan.RootElement.GetProperty("planKind").GetString().Should().Be("PlanDraft");
        plan.RootElement.GetProperty("cloudReadonlyIntents").GetArrayLength().Should().Be(0);
        plan.RootElement.GetProperty("capabilityGaps").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(AgentPlanCapabilityGapCodes.CloudReadonlyIntentUnavailable)
            .And.Contain(AgentPlanCapabilityGapCodes.ExecutionSnapshotUnavailable);

        var task = taskRepository.Items.Should().ContainSingle().Which;
        var confirmation = await new AgentPlanDraftConfirmationService(
                CreatePlanToolGuard(CreateAgentRuntimeGuardWithCloudEnabled()),
                AgentPlanV2TestData.CreateMatchingFreshReadGate(),
                AgentPlanV2TestData.CreateMatchingRoutingSnapshotReader())
            .ConfirmAsync(task, DateTimeOffset.UtcNow, CancellationToken.None);

        confirmation.IsSuccess.Should().BeFalse();
        confirmation.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "A PlanDraft with unresolved capability gaps cannot be confirmed."));
    }
    [Fact]
    public async Task ConfirmPlanDraft_ShouldReturnProblem_WhenPlanJsonIsInvalid()
    {
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "Invalid draft",
            "confirm invalid draft",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            "{",
            DateTimeOffset.UtcNow);

        var result = await new AgentPlanDraftConfirmationService(
                CreatePlanToolGuard(CreateAgentRuntimeGuardWithCloudEnabled()),
                AgentPlanV2TestData.CreateMatchingFreshReadGate(),
                AgentPlanV2TestData.CreateMatchingRoutingSnapshotReader())
            .ConfirmAsync(task, DateTimeOffset.UtcNow, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Agent task plan JSON is invalid and cannot be confirmed."));
        task.Status.Should().Be(AgentTaskStatus.Draft);
    }
    [Fact]
    public async Task PlanDraft_ShouldRemainNodeFree_WhenExecutionSnapshotIsUnavailable()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var taskRepository = new InMemoryRepository<AgentTask>();
        var handler = CreatePlanHandler(
            session,
            CreateAgentRuntimeGuardWithCloudEnabled(),
            taskRepository: taskRepository);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "generate a report", AgentTaskType.ReportGeneration, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ModelId.Should().BeNull();
        taskRepository.Items.Should().ContainSingle();
        using var plan = JsonDocument.Parse(result.Value.PlanJson);
        plan.RootElement.GetProperty("plannerValidationVersion").GetInt32().Should().Be(1);
        var draftSummary = new
        {
            AvailableToolCount = plan.RootElement.GetProperty("plannerAvailableToolCount").GetInt32(),
            IsExecutable = plan.RootElement.GetProperty("isExecutable").GetBoolean()
        };
        draftSummary.Should().BeEquivalentTo(new { AvailableToolCount = 8, IsExecutable = false });
        plan.RootElement.GetProperty("capabilityGaps").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(AgentPlanCapabilityGapCodes.ExecutionSnapshotUnavailable);
        plan.RootElement.GetProperty("steps").GetArrayLength().Should().Be(0);
        plan.RootElement.GetProperty("nodes").GetArrayLength().Should().Be(0);
    }
    [Fact]
    public async Task PlannerToolCatalog_ShouldFilterUnavailableTools_AndExposeSanitizedSchemaSummaries()
    {
        var mcpToolCode = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "runtime-mcp", "read_status");
        var guard = CreatePlanToolGuard(
            CreateGuard(
                [
                    CreateTool(
                        "custom_markdown_report",
                        ToolProviderType.Artifact,
                        inputSchemaJson: """{"type":"object","description":"Exact planner schema.","properties":{"format":{"type":"string","enum":["markdown"]}},"required":["format"],"additionalProperties":false}"""),
                    CreateTool("disabled_tool", isEnabled: false),
                    CreateTool("blocked_tool", riskLevel: AiToolRiskLevel.Blocked),
                    CreateTool("permission_tool", requiredPermission: "AiGateway.ToolRegistry.Manage"),
                    CreateTool(
                        "mcp_runtime_missing",
                        ToolProviderType.Mcp,
                        ToolRegistrationTargetType.McpServer,
                        "missing-server"),
                    CreateTool(
                        mcpToolCode,
                        ToolProviderType.Mcp,
                        ToolRegistrationTargetType.McpServer,
                        "runtime-mcp",
                        inputSchemaJson: """{"type":"object","properties":{"query":{"type":"string"},"mode":{"type":"string","enum":["status"]}},"required":["query"]}""")
                ],
                []),
            new AiToolDefinition
            {
                Name = mcpToolCode,
                TargetType = AiToolTargetType.McpServer,
                TargetName = "runtime-mcp"
            });

        var catalogResult = await guard.GetAvailableToolCatalogAsync(UserId, CancellationToken.None);

        catalogResult.IsSuccess.Should().BeTrue();
        var catalog = catalogResult.Value!;
        catalog.Version.Should().Be(PlannerToolCatalog.CurrentVersion);
        catalog.AvailableToolCount.Should().Be(2);
        catalog.Tools.Select(tool => tool.ToolCode).Should().BeEquivalentTo(["custom_markdown_report", mcpToolCode]);
        catalog.Tools.Single(tool => tool.ToolCode == mcpToolCode).RuntimeAvailable.Should().BeTrue();
        catalog.Tools.Single(tool => tool.ToolCode == "custom_markdown_report")
            .InputSchema!.Properties.Should().Contain(property => property.Name == "format" && property.Required);

        var serialized = JsonSerializer.Serialize(catalog, JsonSerializerOptions.Web);
        serialized.Should().NotContain("sk-test");
        serialized.Should().NotContain("C:\\");
        serialized.Should().NotContain("SELECT * FROM");
        serialized.Should().NotContain("production_records");
    }
    [Fact]
    public void ToolRegistration_ShouldRejectUnsupportedInputSchemaAtConstruction()
    {
        Action create = () => CreateTool(
            "bad_schema_tool",
            inputSchemaJson: "[]");

        create.Should().Throw<ArgumentException>()
            .WithMessage("*Tool registry input schema must be an object schema from the supported strict subset.*");
    }
    [Fact]
    public async Task AgentPlanToolGuard_ShouldRejectUnsafeToolsAndSchemaViolations()
    {
        var guard = CreatePlanToolGuard(
            CreateGuard(
                CreateTool("disabled_tool", isEnabled: false),
                CreateTool("blocked_tool", riskLevel: AiToolRiskLevel.Blocked),
                CreateTool("permission_tool", requiredPermission: "AiGateway.ToolRegistry.Manage"),
                CreateTool(
                    "query_cloud_data_readonly",
                    ToolProviderType.CloudReadonly,
                    isEnabled: true,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval),
                CreateTool("generate_markdown_report", ToolProviderType.Artifact),
                CreateTool(
                    "schema_tool",
                    ToolProviderType.Mcp,
                    ToolRegistrationTargetType.McpServer,
                    "readonly-server",
                    inputSchemaJson: """{"type":"object","properties":{"mode":{"type":"string","enum":["safe"]}},"required":["mode"]}"""),
                CreateTool(
                    "mcp_runtime_missing",
                    ToolProviderType.Mcp,
                    ToolRegistrationTargetType.McpServer,
                    "missing-server")),
            new AiToolDefinition
            {
                Name = "schema_tool",
                TargetType = AiToolTargetType.McpServer,
                TargetName = "readonly-server"
            });

        (await ValidateSingleAsync(guard, "missing_tool"))
            .Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.ToolNotRegistered,
                "Tool 'missing_tool' is not registered."));
        (await ValidateSingleAsync(guard, "disabled_tool")).Errors!.Single()
            .Should().BeEquivalentTo(new ApiProblemDescriptor(AppProblemCodes.ToolDisabled, "Tool 'disabled_tool' is disabled."));
        (await ValidateSingleAsync(guard, "blocked_tool")).Errors!.Single()
            .Should().BeEquivalentTo(new ApiProblemDescriptor(AppProblemCodes.ToolBlocked, "Tool 'blocked_tool' is blocked by registry policy."));
        (await ValidateSingleAsync(guard, "permission_tool")).Errors!.Single()
            .Should().BeEquivalentTo(new ApiProblemDescriptor(AppProblemCodes.ToolPermissionDenied, "Current user lacks required tool permission 'AiGateway.ToolRegistry.Manage'."));

        var cloudWrite = await guard.ValidateStepsAsync(
            [new AgentStepPlanDto("Query", "update Cloud device status", AgentStepType.DataQuery, "query_cloud_data_readonly", false)],
            AgentTaskType.CloudDataReport,
            UserId,
            CancellationToken.None);
        cloudWrite.Errors!.Single().Should().BeEquivalentTo(new ApiProblemDescriptor(
            AppProblemCodes.AgentPlanToolDenied,
            "Agent plan contains SQL statement semantics."));

        var shell = await guard.ValidateStepsAsync(
            [new AgentStepPlanDto("Run powershell", "generate report", AgentStepType.ArtifactGeneration, "generate_markdown_report", false)],
            AgentTaskType.ReportGeneration,
            UserId,
            CancellationToken.None);
        shell.Errors!.Single().Should().BeEquivalentTo(new ApiProblemDescriptor(
            AppProblemCodes.AgentPlanToolDenied,
            "Agent plan contains shell or arbitrary path semantics."));

        var schema = await guard.ValidateStepsAsync(
            [new AgentStepPlanDto("Schema", "schema", AgentStepType.Analysis, "schema_tool", false, """{"mode":"unsafe"}""")],
            AgentTaskType.ReportGeneration,
            UserId,
            CancellationToken.None);
        schema.Errors!.Single().Should().BeEquivalentTo(new ApiProblemDescriptor(
            AppProblemCodes.AgentPlanSchemaInvalid,
            "Tool input field '$.mode' is not one of the allowed values."));

        var mcpRuntime = await ValidateSingleAsync(guard, "mcp_runtime_missing");
        mcpRuntime.Errors!.Single().Should().BeEquivalentTo(new ApiProblemDescriptor(
            AppProblemCodes.AgentPlanToolDenied,
            "MCP tool 'mcp_runtime_missing' is not available in the current runtime."));
    }
    [Fact]
    public async Task AgentTaskQueries_ShouldMapWorkspaceApprovalAndQueueViaDtoService()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.ToolCall,
            task.Steps.Single(step => step.ToolCode == "generate_chart_data").Id.Value.ToString(),
            task.UserId,
            DateTimeOffset.UtcNow);
        var queueItem = new AgentTaskRunQueueItem(
            task.Id,
            AgentTaskRunTriggerType.Manual,
            task.UserId,
            DateTimeOffset.UtcNow);
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>(approval);
        var queueRepository = new InMemoryAgentTaskRunQueueStore(queueItem);
        var dtoQueryService = CreateAgentTaskDtoQueryService(
            workspaceRepository,
            approvalRepository,
            queueRepository);
        var getHandler = new GetAgentTaskQueryHandler(
            taskRepository,
            dtoQueryService,
            new TestCurrentUser(UserId),
            new StubIdentityAccessService([]));
        var listHandler = new GetListAgentTasksBySessionQueryHandler(
            taskRepository,
            dtoQueryService,
            new TestCurrentUser(UserId),
            new StubIdentityAccessService([]));

        var single = await getHandler.Handle(new GetAgentTaskQuery(task.Id.Value), CancellationToken.None);
        var list = await listHandler.Handle(new GetListAgentTasksBySessionQuery(task.SessionId.Value), CancellationToken.None);

        single.IsSuccess.Should().BeTrue();
        single.Value!.WorkspaceCode.Should().Be(workspace.WorkspaceCode);
        single.Value.PendingApprovalCount.Should().Be(1);
        single.Value.QueuedRunId.Should().Be(queueItem.Id.Value);
        single.Value.IsRunQueued.Should().BeTrue();
        list.IsSuccess.Should().BeTrue();
        list.Value.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id.Value);
    }
    [Fact]
    public async Task AgentApprovalQueries_ShouldFilterAndMapViaCoordinator()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.Plan,
            task.Id.Value.ToString(),
            task.UserId,
            DateTimeOffset.UtcNow);
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>(approval);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var queryCoordinator = new AgentApprovalQueryCoordinator(
            taskRepository,
            approvalRepository,
            workspaceRepository,
            new TestCurrentUser(UserId),
            new StubIdentityAccessService([AgentApprovalPermissions.ApproveAgentTaskPlan]));
        var pendingHandler = new GetPendingAgentApprovalsQueryHandler(queryCoordinator);
        var taskHandler = new GetAgentTaskApprovalsQueryHandler(queryCoordinator);

        var pending = await pendingHandler.Handle(new GetPendingAgentApprovalsQuery(), CancellationToken.None);
        var byTask = await taskHandler.Handle(new GetAgentTaskApprovalsQuery(task.Id.Value), CancellationToken.None);

        pending.IsSuccess.Should().BeTrue();
        pending.Value.Should().ContainSingle()
            .Which.WorkspaceCode.Should().Be(workspace.WorkspaceCode);
        byTask.IsSuccess.Should().BeTrue();
        byTask.Value.Should().ContainSingle()
            .Which.Id.Should().Be(approval.Id.Value);
    }
    [Fact]
    public async Task UploadRecordCommand_ShouldPersistSessionUploadViaCoordinator()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var uploadRepository = new InMemoryRepository<UploadRecord>();
        var sessionRepository = new InMemoryRepository<Session>(session);
        var taskRepository = new InMemoryRepository<AgentTask>();
        var audit = new CapturingAuditLogWriter();
        var fileStorage = new CapturingFileStorage();
        var coordinator = new UploadRecordCoordinator(
            uploadRepository,
            sessionRepository,
            taskRepository,
            fileStorage,
            audit,
            new TestCurrentUser(UserId));
        var handler = new UploadRecordCommandHandler(coordinator);
        var bytes = "hello upload"u8.ToArray();
        await using var stream = new MemoryStream(bytes);

        var result = await handler.Handle(
            new UploadRecordCommand(
                nameof(UploadRecordScope.SessionTemp),
                new AiGatewayUploadStream("report.txt", "text/plain", bytes.Length, stream),
                SessionId: session.Id.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SessionId.Should().Be(session.Id.Value);
        result.Value.FileName.Should().Be("report.txt");
        uploadRepository.Items.Should().ContainSingle()
            .Which.StoragePath.Should().Be("report.txt");
        audit.Requests.Should().ContainSingle()
            .Which.Result.Should().Be(AuditResults.Succeeded);
        fileStorage.ConfirmCount.Should().Be(1);
    }
    [Fact]
    public async Task UploadRecordCommand_ShouldRejectKnowledgeBaseShadowScopeBeforeWriting()
    {
        var uploadRepository = new InMemoryRepository<UploadRecord>();
        var audit = new CapturingAuditLogWriter();
        var fileStorage = new CapturingFileStorage();
        var coordinator = new UploadRecordCoordinator(
            uploadRepository,
            new InMemoryRepository<Session>(),
            new InMemoryRepository<AgentTask>(),
            fileStorage,
            audit,
            new TestCurrentUser(UserId));
        await using var stream = new MemoryStream("knowledge"u8.ToArray());

        var result = await coordinator.UploadAsync(
            new UploadRecordCommand(
                nameof(UploadRecordScope.KnowledgeBase),
                new AiGatewayUploadStream("rule.md", "text/markdown", stream.Length, stream)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        uploadRepository.Items.Should().BeEmpty();
        fileStorage.SaveCount.Should().Be(0);
        audit.Requests.Should().BeEmpty();
    }
    [Fact]
    public async Task UploadRecordCommand_ShouldRejectMissingAgentTaskBeforeWriting()
    {
        var uploadRepository = new InMemoryRepository<UploadRecord>();
        var audit = new CapturingAuditLogWriter();
        var fileStorage = new CapturingFileStorage();
        var coordinator = new UploadRecordCoordinator(
            uploadRepository,
            new InMemoryRepository<Session>(),
            new InMemoryRepository<AgentTask>(),
            fileStorage,
            audit,
            new TestCurrentUser(UserId));
        await using var stream = new MemoryStream("agent input"u8.ToArray());

        var result = await coordinator.UploadAsync(
            new UploadRecordCommand(
                nameof(UploadRecordScope.AgentInput),
                new AiGatewayUploadStream("input.txt", "text/plain", stream.Length, stream),
                AgentTaskId: Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        uploadRepository.Items.Should().BeEmpty();
        fileStorage.SaveCount.Should().Be(0);
        audit.Requests.Should().BeEmpty();
    }
    [Fact]
    public async Task ArtifactWorkspaceQueries_ShouldMapAndDownloadViaCoordinator()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var artifact = workspace.AddDraftArtifact(
            ArtifactType.Markdown,
            "report.md",
            "draft/report.md",
            12,
            "text/markdown",
            null,
            DateTimeOffset.UtcNow);
        var fileStore = new InMemoryArtifactWorkspaceFileStore();
        fileStore.AddFile(workspace.WorkspaceCode, artifact.RelativePath, "hello report"u8.ToArray(), artifact.MimeType);
        var audit = new CapturingAuditLogWriter();
        var coordinator = new ArtifactWorkspaceQueryCoordinator(
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ApprovalRequest>(),
            fileStore,
            new AgentAuditRecorder(audit),
            audit,
            new TestCurrentUser(UserId),
            new StubIdentityAccessService([
                AgentApprovalPermissions.GetWorkspace,
                AgentApprovalPermissions.DownloadArtifact
            ]));
        var getHandler = new GetArtifactWorkspaceQueryHandler(coordinator);
        var downloadHandler = new DownloadArtifactQueryHandler(coordinator);

        var workspaceResult = await getHandler.Handle(
            new GetArtifactWorkspaceQuery(workspace.WorkspaceCode),
            CancellationToken.None);
        var downloadResult = await downloadHandler.Handle(
            new DownloadArtifactQuery(artifact.Id.Value),
            CancellationToken.None);

        workspaceResult.IsSuccess.Should().BeTrue();
        workspaceResult.Value!.Files.Should().ContainSingle()
            .Which.RelativePath.Should().Be(artifact.RelativePath);
        workspaceResult.Value.Artifacts.Should().ContainSingle()
            .Which.Id.Should().Be(artifact.Id.Value);
        downloadResult.IsSuccess.Should().BeTrue();
        downloadResult.Value!.FileName.Should().Be("report.md");
        downloadResult.Value.FileSize.Should().Be(12);
        using var memory = new MemoryStream();
        await downloadResult.Value.Stream.CopyToAsync(memory);
        memory.ToArray().Should().Equal("hello report"u8.ToArray());
        audit.Requests.Should().Contain(request => request.ActionCode == "Agent.ArtifactDownload");
    }
    [Fact]
    public async Task ArtifactGenerationFailure_ShouldNotCreatePlaceholderArtifact()
    {
        // This frozen runtime case deliberately crosses the test-only downstream boundary;
        // production P0 confirmation and fresh-read validation remain fail-closed.
        var downstreamRuntimeHarnessFreshReadGate = AgentPlanV2TestData.CreateDownstreamRuntimeHarnessFreshReadGate();
        var (task, workspace) = CreateApprovedTask("generate_pdf");
        var executionRepository = new InMemoryToolExecutionAuditStore();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("generate_pdf", ToolProviderType.Artifact)),
            freshReadGate: downstreamRuntimeHarnessFreshReadGate);

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        workspace.Artifacts.Should().BeEmpty();
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Failed);
        record.ErrorCode.Should().Be(AppProblemCodes.ArtifactGenerationFailed);
        record.ArtifactId.Should().BeNull();
    }
    [Fact]
    public async Task ApprovalToolResolver_ShouldHideMcpTools_UntilRegistryAllowsThem()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var loader = new AgentPluginLoader([], provider);
        const string pluginName = "runtime-mcp";
        var mcpTool = new AiToolDefinition
        {
            Name = "mcp_runtime_read",
            ToolName = "read",
            TargetType = AiToolTargetType.McpServer,
            TargetName = pluginName,
            RequiresApproval = false
        };
        var malformedMcpTool = new AiToolDefinition
        {
            Name = "mcp_runtime_missing_canonical_name",
            TargetType = AiToolTargetType.McpServer,
            TargetName = pluginName,
            RequiresApproval = false
        };
        var localPluginTool = new AiToolDefinition
        {
            Name = "local_diagnostic"
        };
        loader.RegisterAgentPlugin(new GenericBridgePlugin
        {
            Name = pluginName,
            Description = "MCP test bridge",
            ChatExposureMode = ChatExposureMode.Advisory,
            Tools = [mcpTool, malformedMcpTool, localPluginTool]
        });

        var registeredTools = loader.GetPluginTools(pluginName);
        registeredTools.Single(tool => tool.Name == malformedMcpTool.Name).Identity.Should().BeNull();
        var registeredLocalTool = registeredTools.Single(tool => tool.Name != mcpTool.Name && tool.Name != malformedMcpTool.Name);
        registeredLocalTool.ToolName.Should().Be(localPluginTool.Name);
        registeredLocalTool.Identity.Should().NotBeNull();
        registeredLocalTool.Identity!.TargetType.Should().Be(AiToolTargetType.Plugin);

        var approvalRequirementResolver = new ApprovalRequirementResolver(new InMemoryRepository<ApprovalPolicy>());
        var hiddenResolver = new ApprovalToolResolver(
            loader,
            approvalRequirementResolver,
            CreateGuard(),
            new TestCurrentUser(UserId));
        var hiddenTools = await hiddenResolver.GetToolsForPluginsAsync([pluginName], CancellationToken.None);

        hiddenTools.Should().ContainSingle()
            .Which.Name.Should().Be(registeredLocalTool.Name);

        var allowedResolver = new ApprovalToolResolver(
            loader,
            approvalRequirementResolver,
            CreateGuard(CreateTool(
                mcpTool.Name,
                ToolProviderType.Mcp,
                ToolRegistrationTargetType.McpServer,
                pluginName,
                requiresApproval: true)),
            new TestCurrentUser(UserId));
        var allowedTools = await allowedResolver.GetToolsForPluginsAsync([pluginName], CancellationToken.None);

        allowedTools.Should().NotContain(tool => tool.Name == malformedMcpTool.Name);
        allowedTools.Should().Contain(tool => tool.Name == registeredLocalTool.Name);
        var exposed = allowedTools.Should().ContainSingle(tool => tool.Name == mcpTool.Name).Which;
        exposed.Name.Should().Be(mcpTool.Name);
        exposed.RequiresApproval.Should().BeTrue();
    }
    [Fact]
    public async Task AgentPlanToolGuard_ShouldNotExposeMockMcpTools_WhenMockRuntimeIsDisabled()
    {
        var mockTool = CreateTool(
            "mock_mcp_health_check",
            ToolProviderType.MockMcp,
            targetName: "MockMcpProvider");
        var realMcpTool = CreateTool(
            "mcp_real_external",
            ToolProviderType.Mcp,
            ToolRegistrationTargetType.McpServer,
            "real-server");
        var criticalTool = CreateTool(
            "critical_preview",
            ToolProviderType.Artifact,
            riskLevel: AiToolRiskLevel.Critical);
        var hiddenTool = new ToolRegistration(
            "hidden_tool",
            "hidden_tool",
            "hidden tool",
            ToolProviderType.BuiltIn,
            ToolRegistrationTargetType.AgentRuntime,
            "AgentTaskRuntime",
            """{"type":"object"}""",
            """{"type":"object"}""",
            AiToolRiskLevel.Low,
            null,
            false,
            true,
            120,
            ToolAuditLevel.Standard,
            DateTimeOffset.UtcNow,
            isVisibleToPlanner: false);

        var guard = CreatePlanToolGuard(CreateGuard([mockTool, realMcpTool, criticalTool, hiddenTool], []));

        var catalog = await guard.GetAvailableToolCatalogAsync(
            UserId,
            simulationOnly: true,
            businessDomains: ["Production"],
            CancellationToken.None);

        catalog.IsSuccess.Should().BeTrue();
        catalog.Value!.Version.Should().Be(BuiltInToolRegistrations.CurrentCatalogVersion);
        catalog.Value.Tools.Should().BeEmpty();
    }
}
