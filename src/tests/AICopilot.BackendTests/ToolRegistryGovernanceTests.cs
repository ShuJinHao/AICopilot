using System.Linq.Expressions;
using System.Net.Http;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Tools;
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
using AICopilot.EntityFrameworkCore.Specification;
using AICopilot.Infrastructure.Mcp;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "ToolRegistryGovernance")]
public sealed class ToolRegistryGovernanceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-4111-8111-111111111111");

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
    public async Task PlanAgentTask_ShouldRejectCloudDataReport_WhenCloudReadonlyToolIsDisabled()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var handler = new PlanAgentTaskCommandHandler(
            new InMemoryRepository<AgentTask>(),
            new InMemoryRepository<ApprovalRequest>(),
            new InMemoryRepository<Session>(session),
            new InMemoryRepository<UploadRecord>(),
            new InMemoryRepository<ConversationTemplate>(),
            new InMemoryRepository<LanguageModel>(),
            new FixedRuntimeSettingsProvider(),
            new ThrowingWorkspaceService(),
            new AgentAuditRecorder(new CapturingAuditLogWriter()),
            [],
            CreatePlanToolGuard(CreateGuard(CreateTool(
                    "query_cloud_data_readonly",
                    ToolProviderType.CloudReadonly,
                    isEnabled: false,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval))),
            new ThrowingDynamicPlanner(),
            new FixedCloudReadonlyAgentPlanService(),
            new TestCurrentUser(UserId));

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "生成 Cloud 生产数据报告", AgentTaskType.CloudDataReport, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Error);
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
            AppProblemCodes.CloudReadonlyToolDisabled,
            "Tool 'query_cloud_data_readonly' is disabled."));
    }

    [Fact]
    public async Task PlanAgentTask_ShouldRejectCloudDataReport_WhenCloudReadonlyIntentIsUnsupported()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var handler = new PlanAgentTaskCommandHandler(
            new InMemoryRepository<AgentTask>(),
            new InMemoryRepository<ApprovalRequest>(),
            new InMemoryRepository<Session>(session),
            new InMemoryRepository<UploadRecord>(),
            new InMemoryRepository<ConversationTemplate>(),
            new InMemoryRepository<LanguageModel>(),
            new FixedRuntimeSettingsProvider(),
            new ThrowingWorkspaceService(),
            new AgentAuditRecorder(new CapturingAuditLogWriter()),
            [],
            CreatePlanToolGuard(CreateAgentRuntimeGuardWithCloudEnabled()),
            new ThrowingDynamicPlanner(),
            new FixedCloudReadonlyAgentPlanService(Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "Recipe data is outside the Cloud readonly agent boundary."))),
            new TestCurrentUser(UserId));

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "read recipe version details", AgentTaskType.CloudDataReport, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Error);
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "Recipe data is outside the Cloud readonly agent boundary."));
    }

    [Fact]
    public async Task DynamicPlannerFallback_ShouldUseStaticPlan_WhenNoPlannerModelIsAvailable()
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
        plan.RootElement.GetProperty("plannerMode").GetString().Should().Be("Static");
        plan.RootElement.GetProperty("plannerValidationVersion").GetInt32().Should().Be(1);
        plan.RootElement.GetProperty("steps").EnumerateArray()
            .Select(step => step.GetProperty("toolCode").GetString())
            .Should().Contain("generate_markdown_report");
    }

    [Fact]
    public async Task DynamicPlannerPositive_ShouldPersistInputJsonAndPlannerModel()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var plannerModel = CreatePlannerModel();
        var dynamicPlanner = new FixedDynamicPlanner(
            new AgentStepPlanDto(
                "Build markdown",
                "Generate markdown report.",
                AgentStepType.ArtifactGeneration,
                "generate_markdown_report",
                false,
                """{"format":"markdown"}"""),
            new AgentStepPlanDto(
                "Confirm final output",
                "Wait for final approval.",
                AgentStepType.Finalize,
                "finalize_artifacts",
                true));
        var handler = CreatePlanHandler(
            session,
            CreateGuard(
                CreateTool(
                    "generate_markdown_report",
                    ToolProviderType.Artifact,
                    inputSchemaJson: """{"type":"object","properties":{"format":{"type":"string","enum":["markdown"]}},"required":["format"]}"""),
                CreateTool(
                    "finalize_artifacts",
                    ToolProviderType.Artifact,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval)),
            dynamicPlanner,
            [plannerModel]);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "generate a markdown report", AgentTaskType.ReportGeneration, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ModelId.Should().Be(plannerModel.Id.Value);
        dynamicPlanner.LastRequest.Should().NotBeNull();
        dynamicPlanner.LastRequest!.ToolCatalog.Version.Should().Be(PlannerToolCatalog.CurrentVersion);
        dynamicPlanner.LastRequest.ToolCatalog.AvailableToolCount.Should().Be(2);
        using var plan = JsonDocument.Parse(result.Value.PlanJson);
        plan.RootElement.GetProperty("plannerMode").GetString().Should().Be("Dynamic");
        plan.RootElement.GetProperty("plannerModelId").GetGuid().Should().Be(plannerModel.Id.Value);
        plan.RootElement.GetProperty("plannerValidationVersion").GetInt32().Should().Be(1);
        plan.RootElement.GetProperty("plannerToolCatalogVersion").GetInt32().Should().Be(PlannerToolCatalog.CurrentVersion);
        plan.RootElement.GetProperty("plannerAvailableToolCount").GetInt32().Should().Be(2);
        var markdownStep = plan.RootElement.GetProperty("steps").EnumerateArray()
            .Single(step => step.GetProperty("toolCode").GetString() == "generate_markdown_report");
        markdownStep.GetProperty("inputJson").GetString().Should().Be("""{"format":"markdown"}""");
    }

    [Fact]
    public async Task PlannerToolCatalog_ShouldFilterUnavailableTools_AndExposeSanitizedSchemaSummaries()
    {
        var mcpToolCode = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "runtime-mcp", "read_status");
        var guard = CreatePlanToolGuard(
            CreateGuard(
                [
                    CreateTool(
                        "generate_markdown_report",
                        ToolProviderType.Artifact,
                        inputSchemaJson: """{"type":"object","description":"apiKey: sk-test C:\\secrets\\tool.txt SELECT * FROM production_records","properties":{"format":{"type":"string","enum":["markdown"]}},"required":["format"]}"""),
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
        catalog.Tools.Select(tool => tool.ToolCode).Should().BeEquivalentTo(["generate_markdown_report", mcpToolCode]);
        catalog.Tools.Single(tool => tool.ToolCode == mcpToolCode).RuntimeAvailable.Should().BeTrue();
        catalog.Tools.Single(tool => tool.ToolCode == "generate_markdown_report")
            .InputSchema!.Properties.Should().Contain(property => property.Name == "format" && property.Required);

        var serialized = JsonSerializer.Serialize(catalog, JsonSerializerOptions.Web);
        serialized.Should().NotContain("sk-test");
        serialized.Should().NotContain("C:\\");
        serialized.Should().NotContain("SELECT * FROM");
        serialized.Should().NotContain("production_records");
    }

    [Fact]
    public async Task PlannerToolCatalog_ShouldRejectUnsupportedSchema()
    {
        var guard = CreatePlanToolGuard(CreateGuard(CreateTool(
            "bad_schema_tool",
            inputSchemaJson: "[]")));

        var result = await guard.GetAvailableToolCatalogAsync(UserId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.PlannerToolSchemaUnsupported,
                "Tool registry bad_schema_tool input schema must be a JSON object."));
    }

    [Fact]
    public async Task DynamicPlanner_ShouldRejectEmptyToolCatalog_WhenPlannerModelIsAvailable()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var plannerModel = CreatePlannerModel();
        var handler = CreatePlanHandler(
            session,
            CreateGuard(),
            new ThrowingDynamicPlanner(),
            [plannerModel]);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "generate a report", AgentTaskType.ReportGeneration, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.PlannerToolCatalogEmpty,
                "Planner model is available, but no enabled and authorized tools are available for the current user."));
    }

    [Fact]
    public async Task DynamicPlannerMcpPositive_ShouldAllowRuntimeAvailableMcpToolAndPersistInputJson()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var plannerModel = CreatePlannerModel();
        var mcpToolCode = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "runtime-mcp", "read_status");
        var dynamicPlanner = new FixedDynamicPlanner(
            new AgentStepPlanDto(
                "Read MCP status",
                "Read status through MCP runtime.",
                AgentStepType.Analysis,
                mcpToolCode,
                false,
                """{"query":"DEV-001","mode":"status"}"""),
            new AgentStepPlanDto(
                "Confirm final output",
                "Wait for final approval.",
                AgentStepType.Finalize,
                "finalize_artifacts",
                true));
        var taskRepository = new InMemoryRepository<AgentTask>();
        var handler = CreatePlanHandler(
            session,
            CreateGuard(
                CreateTool(
                    mcpToolCode,
                    ToolProviderType.Mcp,
                    ToolRegistrationTargetType.McpServer,
                    "runtime-mcp",
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval,
                    inputSchemaJson: """{"type":"object","properties":{"query":{"type":"string"},"mode":{"type":"string","enum":["status"]}},"required":["query","mode"]}"""),
                CreateTool(
                    "finalize_artifacts",
                    ToolProviderType.Artifact,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval)),
            dynamicPlanner,
            [plannerModel],
            runtimeTools:
            [
                new AiToolDefinition
                {
                    Name = mcpToolCode,
                    TargetType = AiToolTargetType.McpServer,
                    TargetName = "runtime-mcp"
                }
            ],
            taskRepository: taskRepository);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "read mcp status", AgentTaskType.ReportGeneration, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dynamicPlanner.LastRequest!.ToolCatalog.Tools.Should().Contain(tool =>
            tool.ToolCode == mcpToolCode &&
            tool.RuntimeAvailable &&
            tool.InputSchema!.Properties.Any(property => property.Name == "query" && property.Required));
        var task = taskRepository.Items.Should().ContainSingle().Which;
        var mcpStep = task.Steps.Single(step => string.Equals(step.ToolCode, mcpToolCode, StringComparison.OrdinalIgnoreCase));
        mcpStep.RequiresApproval.Should().BeTrue();
        mcpStep.InputJson.Should().Be("""{"query":"DEV-001","mode":"status"}""");
        using var plan = JsonDocument.Parse(task.PlanJson);
        plan.RootElement.GetProperty("plannerAvailableToolCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task DynamicPlannerMcpSchema_ShouldRejectInputJson_WhenSchemaDoesNotMatch()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var plannerModel = CreatePlannerModel();
        var dynamicPlanner = new FixedDynamicPlanner(
            new AgentStepPlanDto(
                "Build markdown",
                "Generate markdown report.",
                AgentStepType.ArtifactGeneration,
                "generate_markdown_report",
                false,
                """{"format":"html"}"""));
        var handler = CreatePlanHandler(
            session,
            CreateGuard(
                CreateTool(
                    "generate_markdown_report",
                    ToolProviderType.Artifact,
                    inputSchemaJson: """{"type":"object","properties":{"format":{"type":"string","enum":["markdown"]}},"required":["format"]}"""),
                CreateTool(
                    "finalize_artifacts",
                    ToolProviderType.Artifact,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval)),
            dynamicPlanner,
            [plannerModel]);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "generate a markdown report", AgentTaskType.ReportGeneration, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanSchemaInvalid,
                "Tool input field '$.format' is not one of the allowed values."));
    }

    [Fact]
    public async Task DynamicPlanner_ShouldRejectExplicitNonPlannerModel()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var chatOnlyModel = new LanguageModel(
            "FakeEval",
            "chat-only",
            "http://localhost/fake",
            "fake-key",
            new ModelParameters { MaxTokens = 4096, MaxOutputTokens = 1024, Temperature = 0.2f },
            "FakeEval",
            LanguageModelUsage.Chat,
            true);
        var handler = CreatePlanHandler(
            session,
            CreateAgentRuntimeGuardWithCloudEnabled(),
            models: [chatOnlyModel]);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "generate a report", AgentTaskType.ReportGeneration, chatOnlyModel.Id.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.PlannerModelUnavailable,
                "Specified planner model is unavailable or does not support Planner usage."));
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
            "Cloud readonly tool 'query_cloud_data_readonly' is not allowed for this plan."));

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
    public void ToolInputSchemaValidator_ShouldValidateNestedObjectsAndArrayItems()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "filters": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "field": { "type": "string", "enum": ["deviceCode"] },
                      "value": { "type": "string" }
                    },
                    "required": ["field", "value"]
                  }
                }
              },
              "required": ["filters"]
            }
            """;

        ToolInputSchemaValidator.ValidateAndParse(
                """{"filters":[{"field":"deviceCode","value":"DEV-001"}]}""",
                schema)
            .IsValid.Should().BeTrue();

        ToolInputSchemaValidator.ValidateAndParse("""{"filters":[{"field":"deviceCode"}]}""", schema)
            .Should().BeEquivalentTo(ToolInputValidationResult.Failure("Tool input is missing required field 'filters[0].value'."));
        ToolInputSchemaValidator.ValidateAndParse("""{"filters":[{"field":"recipeId","value":"R-1"}]}""", schema)
            .Should().BeEquivalentTo(ToolInputValidationResult.Failure("Tool input field '$.filters[0].field' is not one of the allowed values."));
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldRejectRun_WhenLeaseIsActive()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        var attempt = new AgentTaskRunAttempt(task.Id, 1, AgentTaskRunTriggerType.Manual, "test-runner", now, TimeSpan.FromMinutes(5));
        task.BeginRunAttempt(
            attempt.Id,
            attempt.AttemptNo,
            attempt.LeaseId!.Value,
            attempt.LeaseOwner!,
            attempt.LeaseExpiresAt!.Value,
            now);
        var executionRepository = new InMemoryRepository<ToolExecutionRecord>();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("generate_chart_data")),
            toolExecutors: [new TestAgentToolExecutor(_ => true)],
            runAttemptRepository: new InMemoryRepository<AgentTaskRunAttempt>(attempt));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentTaskRunInProgress);
        executionRepository.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldCreateRunAttempt_AndLinkExecutionRecord()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var runAttemptRepository = new InMemoryRepository<AgentTaskRunAttempt>();
        var executionRepository = new InMemoryRepository<ToolExecutionRecord>();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("generate_chart_data")),
            toolExecutors: [new TestAgentToolExecutor(_ => true)],
            runAttemptRepository: runAttemptRepository);

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.RunAttemptCount.Should().Be(1);
        task.IsRunInProgress(DateTimeOffset.UtcNow).Should().BeFalse();
        var attempt = runAttemptRepository.Items.Should().ContainSingle().Which;
        attempt.AttemptNo.Should().Be(1);
        attempt.Status.Should().Be(AgentTaskRunAttemptStatus.WaitingApproval);
        task.ActiveRunAttemptId.Should().Be(attempt.Id);
        executionRepository.Items.Should().ContainSingle()
            .Which.RunAttemptId.Should().Be(attempt.Id);
    }

    [Fact]
    public async Task RetryAgentTaskCommand_ShouldResetFailedStep_AndEnqueueRetry()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        task.Start(now);
        var step = task.Steps.Single();
        step.Start(now);
        step.Fail("generator failed", now);
        task.Fail("generator failed", now);

        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>(
            new ApprovalRequest(task.Id, AgentApprovalType.ToolCall, step.Id.Value.ToString(), task.UserId, now));
        var queueRepository = new InMemoryRepository<AgentTaskRunQueueItem>();
        var handler = new RetryAgentTaskCommandHandler(
            taskRepository,
            workspaceRepository,
            approvalRepository,
            queueRepository,
            new AgentTaskRunQueue(queueRepository),
            new TestCurrentUser(UserId));

        var result = await handler.Handle(new RetryAgentTaskCommand(task.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        step.Status.Should().Be(AgentStepStatus.Pending);
        task.RunAttemptCount.Should().Be(0);
        approvalRepository.Items.Single().Status.Should().Be(AgentApprovalStatus.Cancelled);
        var item = queueRepository.Items.Should().ContainSingle().Which;
        item.TriggerType.Should().Be(AgentTaskRunTriggerType.Retry);
        item.Status.Should().Be(AgentTaskRunQueueStatus.Queued);
        result.Value!.QueuedRunId.Should().Be(item.Id.Value);
        result.Value.IsRunQueued.Should().BeTrue();
    }

    [Fact]
    public async Task RetryAgentTaskCommand_ShouldRejectTerminalTasks()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        task.Start(DateTimeOffset.UtcNow);
        task.MarkWorkspaceReady(DateTimeOffset.UtcNow);
        task.WaitForFinalApproval(DateTimeOffset.UtcNow);
        task.Complete("done", DateTimeOffset.UtcNow);
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var queueRepository = new InMemoryRepository<AgentTaskRunQueueItem>();
        var handler = new RetryAgentTaskCommandHandler(
            taskRepository,
            workspaceRepository,
            approvalRepository,
            queueRepository,
            new AgentTaskRunQueue(queueRepository),
            new TestCurrentUser(UserId));

        var result = await handler.Handle(new RetryAgentTaskCommand(task.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentTaskRetryNotAllowed);
    }

    [Fact]
    public async Task CancelAgentTaskCommand_ShouldCancelActiveAttemptAndPendingApprovals()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        task.Start(now);
        task.WaitForToolApproval(now);
        var attempt = new AgentTaskRunAttempt(task.Id, 1, AgentTaskRunTriggerType.Manual, "test-runner", now, TimeSpan.FromMinutes(5));
        attempt.WaitForApproval(now, "Waiting for approval.");
        task.BeginRunAttempt(
            attempt.Id,
            attempt.AttemptNo,
            Guid.NewGuid(),
            "test-runner",
            now.AddMinutes(5),
            now);
        task.ReleaseRunLease(now, clearActiveAttempt: false);
        var approval = new ApprovalRequest(task.Id, AgentApprovalType.ToolCall, task.Steps.Single().Id.Value.ToString(), task.UserId, now);
        var approvalRepository = new InMemoryRepository<ApprovalRequest>(approval);
        var runAttemptRepository = new InMemoryRepository<AgentTaskRunAttempt>(attempt);
        var queueItem = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        var queueRepository = new InMemoryRepository<AgentTaskRunQueueItem>(queueItem);
        var handler = new CancelAgentTaskCommandHandler(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            approvalRepository,
            runAttemptRepository,
            queueRepository,
            new AgentTaskRunQueue(queueRepository),
            new TestCurrentUser(UserId));

        var result = await handler.Handle(new CancelAgentTaskCommand(task.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.Cancelled);
        attempt.Status.Should().Be(AgentTaskRunAttemptStatus.Cancelled);
        approval.Status.Should().Be(AgentApprovalStatus.Cancelled);
        queueItem.Status.Should().Be(AgentTaskRunQueueStatus.Cancelled);
    }

    [Fact]
    public async Task AgentTaskRunAttemptsQuery_ShouldPageAndIsolateByOwner()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var foreignTask = CreateApprovedTask("generate_chart_data").Task;
        var first = new AgentTaskRunAttempt(task.Id, 1, AgentTaskRunTriggerType.Manual, "runner", DateTimeOffset.UtcNow.AddMinutes(-5), TimeSpan.FromMinutes(5));
        first.MarkFailed(AppProblemCodes.ArtifactGenerationFailed, "failed", DateTimeOffset.UtcNow.AddMinutes(-4));
        var second = new AgentTaskRunAttempt(task.Id, 2, AgentTaskRunTriggerType.Retry, "runner", DateTimeOffset.UtcNow.AddMinutes(-1), TimeSpan.FromMinutes(5));
        var foreign = new AgentTaskRunAttempt(foreignTask.Id, 1, AgentTaskRunTriggerType.Manual, "runner", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        var handler = new GetAgentTaskRunAttemptsQueryHandler(
            new InMemoryRepository<AgentTask>(task, foreignTask),
            new InMemoryRepository<AgentTaskRunAttempt>(first, second, foreign),
            new TestCurrentUser(UserId));

        var result = await handler.Handle(new GetAgentTaskRunAttemptsQuery(task.Id.Value, 1, 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
        result.Value.Items.Should().NotContain(item => item.TaskId == foreignTask.Id.Value);
        result.Value.Items.First().AttemptNo.Should().Be(2);

        var unauthorized = await new GetAgentTaskRunAttemptsQueryHandler(
                new InMemoryRepository<AgentTask>(task),
                new InMemoryRepository<AgentTaskRunAttempt>(first, second),
                new TestCurrentUser(Guid.Parse("22222222-2222-4222-8222-222222222222")))
            .Handle(new GetAgentTaskRunAttemptsQuery(task.Id.Value, 1, 10), CancellationToken.None);
        unauthorized.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task AgentRunQueueEnqueue_ShouldPreventDuplicateActiveItems()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var queueRepository = new InMemoryRepository<AgentTaskRunQueueItem>();
        var queue = new AgentTaskRunQueue(queueRepository);
        var handler = new RunAgentTaskCommandHandler(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            queueRepository,
            queue,
            new TestCurrentUser(UserId));

        var first = await handler.Handle(new RunAgentTaskCommand(task.Id.Value), CancellationToken.None);
        var duplicate = await handler.Handle(new RunAgentTaskCommand(task.Id.Value), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        first.Value!.IsRunQueued.Should().BeTrue();
        queueRepository.Items.Should().ContainSingle();
        duplicate.IsSuccess.Should().BeFalse();
        duplicate.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentTaskRunInProgress);
    }

    [Fact]
    public async Task AgentRunQueueWorkerPositive_ShouldLeaseRunAndCompleteQueueItem()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var queueRepository = new InMemoryRepository<AgentTaskRunQueueItem>(
            new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, DateTimeOffset.UtcNow));
        var attemptRepository = new InMemoryRepository<AgentTaskRunAttempt>();
        using var provider = CreateQueueWorkerProvider(
            taskRepository,
            queueRepository,
            attemptRepository,
            new RecordingAgentTaskRuntime(attemptRepository));
        var worker = new AgentTaskRunQueueWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskRunQueueWorker>.Instance);

        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        processed.Should().BeTrue();
        var queueItem = queueRepository.Items.Single();
        queueItem.Status.Should().Be(AgentTaskRunQueueStatus.Succeeded);
        queueItem.RunAttemptId.Should().NotBeNull();
        attemptRepository.Items.Should().ContainSingle()
            .Which.TriggerType.Should().Be(AgentTaskRunTriggerType.Manual);
    }

    [Fact]
    public async Task AgentApprovalResumeQueue_ShouldEnqueueAfterToolApproval()
    {
        var (task, workspace) = CreateApprovedTask("generate_pdf", requiresApproval: true);
        var now = DateTimeOffset.UtcNow;
        task.Start(now);
        var step = task.Steps.Single();
        task.WaitForToolApproval(now);
        var approval = new ApprovalRequest(task.Id, AgentApprovalType.ToolCall, step.Id.Value.ToString(), task.UserId, now);
        var queueRepository = new InMemoryRepository<AgentTaskRunQueueItem>();
        var handler = new ApproveAgentApprovalCommandHandler(
            new InMemoryRepository<ApprovalRequest>(approval),
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new AgentAuditRecorder(new CapturingAuditLogWriter()),
            new AgentTaskRunQueue(queueRepository),
            new TestCurrentUser(UserId),
            new StubIdentityAccessService([AgentApprovalPermissions.ApproveAgentToolCall]));

        var result = await handler.Handle(new ApproveAgentApprovalCommand(approval.Id.Value, "approved"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        step.Status.Should().Be(AgentStepStatus.Approved);
        task.Status.Should().Be(AgentTaskStatus.Running);
        queueRepository.Items.Should().ContainSingle()
            .Which.TriggerType.Should().Be(AgentTaskRunTriggerType.ApprovalResume);
    }

    [Fact]
    public async Task AgentRunQueueLeaseExpired_ShouldFailStartedLeaseConservatively()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var attempt = new AgentTaskRunAttempt(task.Id, 1, AgentTaskRunTriggerType.Manual, "expired-worker", now, TimeSpan.FromSeconds(1));
        task.BeginRunAttempt(
            attempt.Id,
            attempt.AttemptNo,
            attempt.LeaseId!.Value,
            attempt.LeaseOwner!,
            attempt.LeaseExpiresAt!.Value,
            now);
        var queueItem = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        queueItem.AcquireLease(Guid.NewGuid(), "expired-worker", now, TimeSpan.FromSeconds(1));
        queueItem.MarkStarted(attempt.Id, now);
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var queueRepository = new InMemoryRepository<AgentTaskRunQueueItem>(queueItem);
        var attemptRepository = new InMemoryRepository<AgentTaskRunAttempt>(attempt);
        using var provider = CreateQueueWorkerProvider(
            taskRepository,
            queueRepository,
            attemptRepository,
            new ThrowingRuntime());
        var worker = new AgentTaskRunQueueWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskRunQueueWorker>.Instance);

        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        processed.Should().BeFalse();
        queueItem.Status.Should().Be(AgentTaskRunQueueStatus.Failed);
        queueItem.FailureCode.Should().Be(AppProblemCodes.AgentTaskRunQueueLeaseExpired);
        task.Status.Should().Be(AgentTaskStatus.Failed);
        attempt.Status.Should().Be(AgentTaskRunAttemptStatus.Failed);
        attempt.FailureCode.Should().Be(AppProblemCodes.AgentTaskRunLeaseExpired);
    }

    [Fact]
    public async Task AgentWorkerHeartbeat_ShouldTrackIdleAndActiveState()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var queueItem = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, DateTimeOffset.UtcNow);
        var heartbeatRepository = new InMemoryRepository<AgentWorkerHeartbeat>();
        var heartbeatService = new AgentWorkerHeartbeatService(
            heartbeatRepository,
            new FixedWorkspaceFingerprintProvider("hash-api"));

        await heartbeatService.MarkAsync("worker-1", "data-worker", "1.0.0", null, CancellationToken.None);
        await heartbeatService.MarkAsync("worker-1", "data-worker", "1.0.0", queueItem, CancellationToken.None);
        await heartbeatService.MarkAsync("worker-1", "data-worker", "1.0.0", null, CancellationToken.None);

        heartbeatRepository.Items.Should().ContainSingle();
        var heartbeat = heartbeatRepository.Items.Single();
        heartbeat.WorkerId.Should().Be("worker-1");
        heartbeat.WorkspaceRootHash.Should().Be("hash-api");
        heartbeat.ActiveQueueItemId.Should().BeNull();
        heartbeat.ActiveTaskId.Should().BeNull();
    }

    [Fact]
    public async Task AgentRunQueueSummary_ShouldCountStatusesAndOldestQueued()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        var oldest = now.AddMinutes(-10);
        var queued = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now, oldest);
        var leased = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Retry, task.UserId, now);
        leased.AcquireLease(Guid.NewGuid(), "worker", now, TimeSpan.FromMinutes(5));
        var staleLeased = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Retry, task.UserId, now.AddMinutes(-10));
        staleLeased.AcquireLease(Guid.NewGuid(), "worker", now.AddMinutes(-10), TimeSpan.FromSeconds(1));
        var failed = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        failed.MarkFailed("failed", "safe", now);
        var deadLetter = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        deadLetter.MarkDeadLetter("dead", "safe", now);
        var succeeded = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        succeeded.MarkSucceeded(now);
        var heartbeat = new AgentWorkerHeartbeat("worker-1", "data-worker", now, "hash", "1.0.0");
        var handler = new GetAgentRunQueueSummaryQueryHandler(
            new InMemoryRepository<AgentTaskRunQueueItem>(queued, leased, staleLeased, failed, deadLetter, succeeded),
            new InMemoryRepository<AgentWorkerHeartbeat>(heartbeat));

        var result = await handler.Handle(new GetAgentRunQueueSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.QueuedCount.Should().Be(1);
        result.Value.LeasedCount.Should().Be(2);
        result.Value.FailedCount.Should().Be(1);
        result.Value.DeadLetterCount.Should().Be(1);
        result.Value.SucceededCount.Should().Be(1);
        result.Value.StaleLeasedCount.Should().Be(1);
        result.Value.OldestQueuedAt.Should().Be(oldest);
        result.Value.ActiveWorkerCount.Should().Be(1);
    }

    [Fact]
    public async Task AgentRunQueueGlobalQuery_ShouldFilterByStatusTriggerTaskAndRequester()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var (otherTask, _) = CreateApprovedTask("generate_pdf");
        var now = DateTimeOffset.UtcNow;
        var first = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        first.MarkFailed("failed", "safe", now);
        var second = new AgentTaskRunQueueItem(otherTask.Id, AgentTaskRunTriggerType.Retry, otherTask.UserId, now);
        var handler = new GetAgentRunQueueQueryHandler(new InMemoryRepository<AgentTaskRunQueueItem>(first, second));

        var result = await handler.Handle(
            new GetAgentRunQueueQuery(
                PageIndex: 1,
                PageSize: 10,
                Status: "Failed",
                TriggerType: "Manual",
                TaskId: task.Id.Value,
                RequestedBy: task.UserId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle()
            .Which.Id.Should().Be(first.Id.Value);
    }

    [Fact]
    public async Task AgentRunQueueDeadLetter_ShouldAllowOnlySafeStatesAndWriteAudit()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        var failed = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        failed.MarkFailed("worker_failed", "safe failure", now);
        var activeLeased = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Retry, task.UserId, now);
        activeLeased.AcquireLease(Guid.NewGuid(), "worker", now, TimeSpan.FromMinutes(5));
        var succeeded = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        succeeded.MarkSucceeded(now);
        var repository = new InMemoryRepository<AgentTaskRunQueueItem>(failed, activeLeased, succeeded);
        var audit = new CapturingAuditLogWriter();
        var handler = new DeadLetterAgentRunQueueItemCommandHandler(repository, audit);

        var moved = await handler.Handle(
            new DeadLetterAgentRunQueueItemCommand(failed.Id.Value, "move to dead letter"),
            CancellationToken.None);
        var activeDenied = await handler.Handle(
            new DeadLetterAgentRunQueueItemCommand(activeLeased.Id.Value, "bad"),
            CancellationToken.None);
        var succeededDenied = await handler.Handle(
            new DeadLetterAgentRunQueueItemCommand(succeeded.Id.Value, "bad"),
            CancellationToken.None);

        moved.IsSuccess.Should().BeTrue();
        failed.Status.Should().Be(AgentTaskRunQueueStatus.DeadLetter);
        audit.Requests.Should().ContainSingle()
            .Which.ActionCode.Should().Be("Agent.RunQueueDeadLetter");
        activeDenied.IsSuccess.Should().BeFalse();
        activeDenied.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentRunQueueDeadLetterNotAllowed);
        succeededDenied.IsSuccess.Should().BeFalse();
        succeededDenied.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.AgentRunQueueDeadLetterNotAllowed);
    }

    [Fact]
    public void WorkspaceConfigHealth_ShouldDetectMismatchWithoutExposingPath()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var now = DateTimeOffset.UtcNow;
        var queueItem = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        var heartbeat = new AgentWorkerHeartbeat("worker-1", "data-worker", now, "worker-hash", "1.0.0");
        heartbeat.MarkSeen(now, "data-worker", "worker-hash", "1.0.0", queueItem.Id, queueItem.TaskId);

        var status = AgentWorkerStatusCalculator.Build([queueItem], [heartbeat], "api-hash", now);

        status.StatusCode.Should().Be(AppProblemCodes.AgentWorkerWorkspaceMismatch);
        status.WorkspaceConsistent.Should().BeFalse();
        status.HttpApiWorkspaceRootHash.Should().Be("api-hash");
        status.HttpApiWorkspaceRootHash.Should().NotContain(":\\");
        status.Workers.Should().ContainSingle()
            .Which.WorkspaceMatchesHttpApi.Should().BeFalse();
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldRejectDisabledTool_AndWriteRejectedExecutionRecord()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var taskRepository = new InMemoryRepository<AgentTask>(task);
        var workspaceRepository = new InMemoryRepository<ArtifactWorkspace>(workspace);
        var executionRepository = new InMemoryRepository<ToolExecutionRecord>();
        var runtime = CreateRuntime(
            taskRepository,
            workspaceRepository,
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("generate_chart_data", isEnabled: false)));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        task.Steps.Single().Status.Should().Be(AgentStepStatus.Failed);
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.ToolCode.Should().Be("generate_chart_data");
        record.Status.Should().Be(ToolExecutionStatus.Rejected);
        record.ErrorCode.Should().Be(AppProblemCodes.ToolDisabled);
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldUseRegistryApprovalRequirement_BeforeExecutingStep()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var executionRepository = new InMemoryRepository<ToolExecutionRecord>();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            approvalRepository,
            executionRepository,
            CreateGuard(CreateTool("generate_chart_data", requiresApproval: true)));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.WaitingToolApproval);
        task.Steps.Single().Status.Should().Be(AgentStepStatus.WaitingApproval);
        executionRepository.Items.Should().BeEmpty();
        var approval = approvalRepository.Items.Should().ContainSingle().Which;
        approval.ApprovalType.Should().Be(AgentApprovalType.ToolCall);
        approval.TargetId.Should().Be(task.Steps.Single().Id.Value.ToString());
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldRedactFailedExecutionRecord()
    {
        var (task, workspace) = CreateApprovedTask("generate_chart_data");
        var executionRepository = new InMemoryRepository<ToolExecutionRecord>();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("generate_chart_data")),
            new ThrowingWorkspaceService(throwOnWrite: true));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Failed);
        record.ErrorCode.Should().Be(AppProblemCodes.ArtifactGenerationFailed);
        record.ErrorMessage.Should().Contain("apiKey=******");
        record.ErrorMessage.Should().Contain("[redacted-path]");
        record.ErrorMessage.Should().Contain("Password=******");
        record.ErrorMessage.Should().NotContain("sk-test");
        record.ErrorMessage.Should().NotContain("C:\\");
        record.ErrorMessage.Should().NotContain("super-secret");
    }

    [Fact]
    public async Task ToolExecutionRecordQuery_ShouldPageFilterRedactAndIsolateByTaskOwner()
    {
        var (task, _) = CreateApprovedTask("generate_chart_data");
        var failedRecord = new ToolExecutionRecord(
            task.Id,
            task.Steps.Single().Id,
            "generate_pdf",
            @"apiKey: sk-test C:\secrets\input.txt",
            DateTimeOffset.UtcNow.AddMinutes(-1));
        failedRecord.MarkFailed(
            AppProblemCodes.ArtifactGenerationFailed,
            @"connection string=Host=db;Password=super-secret; C:\secrets\output.txt",
            """{"providerType":"Artifact","targetType":"AgentRuntime","timeoutSeconds":120,"auditLevel":"Standard"}""",
            DateTimeOffset.UtcNow);
        var succeededRecord = new ToolExecutionRecord(
            task.Id,
            task.Steps.Single().Id,
            "generate_chart_data",
            "{}",
            DateTimeOffset.UtcNow.AddMinutes(-2));
        succeededRecord.MarkSucceeded(
            """{"sql":"select * from production_records","table":"production_records"}""",
            null,
            """{"providerType":"BuiltIn"}""",
            DateTimeOffset.UtcNow.AddMinutes(-2).AddSeconds(1));
        var foreignTask = CreateApprovedTask("generate_pdf").Task;
        var foreignRecord = new ToolExecutionRecord(
            foreignTask.Id,
            foreignTask.Steps.Single().Id,
            "generate_pdf",
            "{}",
            DateTimeOffset.UtcNow);

        var handler = new GetAgentTaskToolExecutionsQueryHandler(
            new InMemoryRepository<AgentTask>(task, foreignTask),
            new InMemoryRepository<ToolExecutionRecord>(failedRecord, succeededRecord, foreignRecord),
            new TestCurrentUser(UserId));

        var result = await handler.Handle(
            new GetAgentTaskToolExecutionsQuery(task.Id.Value, 1, 10, "Failed", "generate_pdf"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.TotalCount.Should().Be(1);
        var dto = result.Value.Items.Single();
        dto.ToolCode.Should().Be("generate_pdf");
        dto.Status.Should().Be("Failed");
        dto.ErrorCode.Should().Be(AppProblemCodes.ArtifactGenerationFailed);
        dto.InputSummary.Should().Contain("apiKey=******");
        dto.InputSummary.Should().Contain("[redacted-path]");
        dto.ErrorMessage.Should().Contain("connection string=******");
        dto.ErrorMessage.Should().NotContain("sk-test");
        dto.ErrorMessage.Should().NotContain("super-secret");
        dto.ErrorMessage.Should().NotContain("C:\\");

        var allRecords = await handler.Handle(
            new GetAgentTaskToolExecutionsQuery(task.Id.Value, 1, 10),
            CancellationToken.None);
        allRecords.Value!.Items.Should().HaveCount(2);
        allRecords.Value.Items.Should().NotContain(item => item.TaskId == foreignTask.Id.Value);
        allRecords.Value.Items.Single(item => item.ToolCode == "generate_chart_data")
            .OutputSummary!.ToLowerInvariant().Should().NotContain("select");

        var unauthorizedHandler = new GetAgentTaskToolExecutionsQueryHandler(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ToolExecutionRecord>(failedRecord),
            new TestCurrentUser(Guid.Parse("22222222-2222-4222-8222-222222222222")));
        var unauthorized = await unauthorizedHandler.Handle(
            new GetAgentTaskToolExecutionsQuery(task.Id.Value, 1, 10),
            CancellationToken.None);
        unauthorized.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task AgentAuditSummary_ShouldMergeToolRecordsAndFailureSummary()
    {
        var (task, workspace) = CreateApprovedTask("generate_pdf");
        var now = DateTimeOffset.UtcNow;
        task.Start(now);
        var step = task.Steps.Single();
        step.Start(now);
        step.Fail(@"apiKey: sk-test C:\secrets\report.txt Host=db;Password=super-secret;", now);
        task.Fail("report generation failed", now);

        var record = new ToolExecutionRecord(
            task.Id,
            step.Id,
            "generate_pdf",
            "{}",
            now.AddSeconds(-1));
        record.MarkFailed(
            AppProblemCodes.ArtifactGenerationFailed,
            @"apiKey: sk-test C:\secrets\report.txt Host=db;Password=super-secret;",
            """{"providerType":"Artifact","targetType":"AgentRuntime","timeoutSeconds":120,"auditLevel":"Standard","workspaceCode":"ws_test"}""",
            now);

        var auditLog = new AuditLogSummaryDto(
            Guid.NewGuid(),
            AuditActionGroups.AiGateway,
            "Agent.Plan",
            "AgentTask",
            task.Id.Value.ToString(),
            task.TaskCode,
            "test-user",
            "User",
            AuditResults.Succeeded,
            "Plan created.",
            [],
            new Dictionary<string, string> { ["taskId"] = task.Id.Value.ToString() },
            now.UtcDateTime.AddMinutes(-5));
        var handler = new GetAgentTaskAuditSummaryQueryHandler(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ToolExecutionRecord>(record),
            new FixedAuditLogQueryService(auditLog),
            new TestCurrentUser(UserId));

        var result = await handler.Handle(new GetAgentTaskAuditSummaryQuery(task.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(item => item.ActionCode == "Agent.Plan");
        result.Value.Should().Contain(item => item.ActionCode == "Agent.ToolExecutionRecord" &&
                                             item.Metadata["providerType"] == "Artifact" &&
                                             item.Metadata["targetType"] == "AgentRuntime" &&
                                             item.Metadata["auditLevel"] == "Standard");
        var failure = result.Value.Should().ContainSingle(item => item.ActionCode == "Agent.FailureSummary").Subject;
        failure.Metadata["errorCode"].Should().Be(AppProblemCodes.ArtifactGenerationFailed);
        failure.Summary.Should().NotContain("sk-test");
        failure.Summary.Should().NotContain("super-secret");
        failure.Summary.Should().NotContain("C:\\");
    }

    [Fact]
    public async Task ArtifactGenerationFailure_ShouldNotCreatePlaceholderArtifact()
    {
        var (task, workspace) = CreateApprovedTask("generate_pdf");
        var executionRepository = new InMemoryRepository<ToolExecutionRecord>();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("generate_pdf", ToolProviderType.Artifact)));

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
    public async Task AgentTaskRuntime_ShouldExecuteCloudReadonlyTool_AndUseRowsInMarkdownReport()
    {
        var (task, workspace) = CreateCloudApprovedTask();
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.ToolCall,
            task.Steps.First().Id.Value.ToString(),
            task.UserId,
            DateTimeOffset.UtcNow);
        approval.Approve(UserId, "approved", DateTimeOffset.UtcNow);
        approvalRepository.Add(approval);

        var executionRepository = new InMemoryRepository<ToolExecutionRecord>();
        var workspaceService = new CapturingWorkspaceService(workspace);
        var semanticPlan = CreateDeviceSemanticPlan();
        var cloudClient = new RecordingCloudAiReadClient(new CloudAiReadResult<object>(
            "/api/v1/ai/read/devices",
            "Cloud AiRead devices",
            DateTimeOffset.UtcNow,
            20,
            false,
            [],
            [
                new Dictionary<string, object?>
                {
                    ["deviceCode"] = "DEV-001",
                    ["deviceName"] = "Cutter A",
                    ["status"] = "Running"
                }
            ]));
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            approvalRepository,
            executionRepository,
            CreateGuard(
                CreateTool(
                    "query_cloud_data_readonly",
                    ToolProviderType.CloudReadonly,
                    isEnabled: true,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval),
                CreateTool("generate_markdown_report", ToolProviderType.Artifact)),
            workspaceService,
            CreateRealCloudReadonlyExecutor(new FixedSemanticQueryPlanner(semanticPlan), cloudClient));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Steps.Should().OnlyContain(step => step.Status == AgentStepStatus.Completed);
        cloudClient.RequestedPlans.Should().ContainSingle()
            .Which.Intent.Should().Be("Analysis.Device.List");
        workspaceService.TextArtifacts.Should().ContainKey("draft/report.md");
        workspaceService.TextArtifacts["draft/report.md"].Should().Contain("DEV-001");
        executionRepository.Items.Should().HaveCount(2);
        executionRepository.Items.First().Status.Should().Be(ToolExecutionStatus.Succeeded);
        executionRepository.Items.First().OutputSummary.Should().Contain("Cloud AiRead");
        executionRepository.Items.First().OutputSummary!.ToLowerInvariant().Should().NotContain("select ");
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldStopCloudReadonlyFlow_WhenCloudReadFails()
    {
        var (task, workspace) = CreateCloudApprovedTask();
        var approvalRepository = new InMemoryRepository<ApprovalRequest>();
        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.ToolCall,
            task.Steps.First().Id.Value.ToString(),
            task.UserId,
            DateTimeOffset.UtcNow);
        approval.Approve(UserId, "approved", DateTimeOffset.UtcNow);
        approvalRepository.Add(approval);

        var executionRepository = new InMemoryRepository<ToolExecutionRecord>();
        var workspaceService = new CapturingWorkspaceService(workspace);
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            approvalRepository,
            executionRepository,
            CreateGuard(
                CreateTool(
                    "query_cloud_data_readonly",
                    ToolProviderType.CloudReadonly,
                    isEnabled: true,
                    requiresApproval: true,
                    riskLevel: AiToolRiskLevel.RequiresApproval),
                CreateTool("generate_markdown_report", ToolProviderType.Artifact)),
            workspaceService,
            CreateRealCloudReadonlyExecutor(
                new FixedSemanticQueryPlanner(CreateDeviceSemanticPlan()),
                new FailingCloudAiReadClient(new CloudAiReadException(
                    CloudAiReadProblemCodes.MissingRequiredParameter,
                    @"apiKey: sk-test C:\cloud\secret.txt Host=db;Password=super-secret; missing device/time"))));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        task.Steps.First().Status.Should().Be(AgentStepStatus.Failed);
        task.Steps.Skip(1).Should().OnlyContain(step => step.Status != AgentStepStatus.Completed);
        workspaceService.TextArtifacts.Should().NotContainKey("draft/report.md");
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Failed);
        record.ErrorCode.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        record.ErrorMessage.Should().Contain("apiKey=******");
        record.ErrorMessage.Should().Contain("[redacted-path]");
        record.ErrorMessage.Should().NotContain("sk-test");
        record.ErrorMessage.Should().NotContain("C:\\");
        record.ErrorMessage.Should().NotContain("super-secret");
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
        loader.RegisterAgentPlugin(new GenericBridgePlugin
        {
            Name = pluginName,
            Description = "MCP test bridge",
            ChatExposureMode = ChatExposureMode.Advisory,
            Tools = [mcpTool]
        });

        var approvalRequirementResolver = new ApprovalRequirementResolver(new InMemoryRepository<ApprovalPolicy>());
        var hiddenResolver = new ApprovalToolResolver(
            loader,
            approvalRequirementResolver,
            CreateGuard(),
            new TestCurrentUser(UserId));
        var hiddenTools = await hiddenResolver.GetToolsForPluginsAsync([pluginName], CancellationToken.None);

        hiddenTools.Should().BeEmpty();

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

        var exposed = allowedTools.Should().ContainSingle().Which;
        exposed.Name.Should().Be(mcpTool.Name);
        exposed.RequiresApproval.Should().BeTrue();
    }

    [Fact]
    public void AgentToolExecutorResolver_ShouldResolveBuiltInCloudReadonlyAndMcpExecutors()
    {
        var builtInExecutor = new TestAgentToolExecutor(tool => tool.ProviderType == ToolProviderType.BuiltIn);
        var cloudExecutor = new TestAgentToolExecutor(tool => tool.ProviderType == ToolProviderType.CloudReadonly);
        var mcpExecutor = new TestAgentToolExecutor(tool => tool.ProviderType == ToolProviderType.Mcp);
        var resolver = new AgentToolExecutorResolver([builtInExecutor, cloudExecutor, mcpExecutor]);
        var step = CreateApprovedTask("read_uploaded_file").Task.Steps.Single();

        resolver.Resolve(CreateTool("read_uploaded_file"), step).Should().BeSameAs(builtInExecutor);
        resolver.Resolve(CreateTool(
            "query_cloud_data_readonly",
            ToolProviderType.CloudReadonly,
            isEnabled: true,
            requiresApproval: true,
            riskLevel: AiToolRiskLevel.RequiresApproval), step).Should().BeSameAs(cloudExecutor);
        resolver.Resolve(CreateTool(
            "mcp_runtime_mcp_read",
            ToolProviderType.Mcp,
            ToolRegistrationTargetType.McpServer,
            "runtime-mcp"), step).Should().BeSameAs(mcpExecutor);
    }

    [Fact]
    public async Task McpToolRegistrySynchronizer_ShouldUpsertDisabledTools_AndPreserveAdminSettings()
    {
        var repository = new InMemoryRepository<ToolRegistration>();
        var synchronizer = new McpToolRegistrySynchronizer(repository);

        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [
                new McpDiscoveredToolRegistration(
                    "mcp__runtime_mcp__read",
                    "read",
                    "Read MCP data.",
                    """{"type":"object"}""",
                    """{"type":"object"}""",
                    AiToolRiskLevel.Low)
            ],
            CancellationToken.None);

        var tool = repository.Items.Should().ContainSingle().Which;
        tool.ProviderType.Should().Be(ToolProviderType.Mcp);
        tool.TargetType.Should().Be(ToolRegistrationTargetType.McpServer);
        tool.TargetName.Should().Be("runtime-mcp");
        tool.IsEnabled.Should().BeFalse();
        tool.RequiresApproval.Should().BeTrue();

        tool.Update(
            tool.DisplayName,
            tool.Description,
            tool.ProviderType,
            tool.TargetType,
            tool.TargetName,
            tool.InputSchemaJson,
            tool.OutputSchemaJson,
            AiToolRiskLevel.Low,
            "AiGateway.ToolRegistry.Manage",
            requiresApproval: false,
            isEnabled: true,
            tool.TimeoutSeconds,
            ToolAuditLevel.Verbose,
            DateTimeOffset.UtcNow);

        await synchronizer.UpsertDiscoveredToolsAsync(
            "runtime-mcp",
            [
                new McpDiscoveredToolRegistration(
                    "mcp__runtime_mcp__read",
                    "read",
                    "Read MCP data after rediscovery.",
                    """{"type":"object","properties":{"input":{"type":"string"}}}""",
                    """{"type":"object"}""",
                    AiToolRiskLevel.RequiresApproval)
            ],
            CancellationToken.None);

        tool.IsEnabled.Should().BeTrue();
        tool.RequiresApproval.Should().BeFalse();
        tool.RequiredPermission.Should().Be("AiGateway.ToolRegistry.Manage");
        tool.AuditLevel.Should().Be(ToolAuditLevel.Verbose);
        tool.InputSchemaJson.Should().Contain("\"input\"");
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldExecuteApprovedMcpTool_AndWriteRedactedExecutionRecord()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var loader = new AgentPluginLoader([], provider);
        var toolCode = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "runtime-mcp", "read_status");
        loader.RegisterAgentPlugin(new GenericBridgePlugin
        {
            Name = "runtime-mcp",
            Description = "MCP runtime bridge",
            ChatExposureMode = ChatExposureMode.Advisory,
            Tools =
            [
                new AiToolDefinition
                {
                    Name = toolCode,
                    ToolName = "read_status",
                    Kind = AiToolCallKind.Mcp,
                    TargetType = AiToolTargetType.McpServer,
                    TargetName = "runtime-mcp",
                    ServerName = "runtime-mcp",
                    ExternalSystemType = AiToolExternalSystemType.NonCloud,
                    CapabilityKind = AiToolCapabilityKind.ReadOnlyQuery,
                    RiskLevel = AiToolRiskLevel.RequiresApproval,
                    RequiresApproval = true,
                    JsonSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
                    ReturnJsonSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
                    InvokeAsync = (_, _) => ValueTask.FromResult<object?>(new
                    {
                        status = "ok",
                        token = "sk-test",
                        path = @"C:\server\secret.txt"
                    })
                }
            ]
        });
        var (task, workspace) = CreateApprovedTask(toolCode);
        var approval = new ApprovalRequest(task.Id, AgentApprovalType.ToolCall, task.Steps.Single().Id.Value.ToString(), UserId, DateTimeOffset.UtcNow);
        approval.Approve(UserId, "approved", DateTimeOffset.UtcNow);
        var executionRepository = new InMemoryRepository<ToolExecutionRecord>();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(approval),
            executionRepository,
            CreateGuard(CreateTool(
                toolCode,
                ToolProviderType.Mcp,
                ToolRegistrationTargetType.McpServer,
                "runtime-mcp",
                requiresApproval: true,
                riskLevel: AiToolRiskLevel.RequiresApproval)),
            toolExecutors: [new McpAgentToolExecutor(loader, provider)]);

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Succeeded);
        record.OutputSummary.Should().Contain("runtime-mcp");
        record.OutputSummary.Should().Contain("token=******");
        record.OutputSummary.Should().Contain("[redacted-path]");
        record.OutputSummary.Should().NotContain("sk-test");
        record.OutputSummary.Should().NotContain("C:\\");
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldEvaluateRagAdminAccessFromTaskOwner()
    {
        var knowledgeBaseId = Guid.NewGuid();
        var (task, workspace) = CreateRagApprovedTask(knowledgeBaseId);
        var executionRepository = new InMemoryRepository<ToolExecutionRecord>();
        var accessChecker = new RecordingKnowledgeBaseAccessChecker();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(),
            executionRepository,
            CreateGuard(CreateTool("rag_search", ToolProviderType.BuiltIn)),
            knowledgeBaseAccessCheckers: [accessChecker],
            identityAccessService: new StubIdentityAccessService([], roleName: "Admin"));

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        task.Status.Should().Be(AgentTaskStatus.WaitingFinalApproval);
        accessChecker.ObservedUserId.Should().Be(UserId);
        accessChecker.ObservedIsAdmin.Should().BeTrue();
        executionRepository.Items.Should().ContainSingle().Which.Status.Should().Be(ToolExecutionStatus.Succeeded);
    }

    [Fact]
    public async Task AgentTaskRuntime_ShouldFailMcpTool_WhenRegistryInputSchemaDoesNotMatch()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var loader = new AgentPluginLoader([], provider);
        var toolCode = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "runtime-mcp", "read_status");
        var invoked = false;
        loader.RegisterAgentPlugin(new GenericBridgePlugin
        {
            Name = "runtime-mcp",
            Description = "MCP runtime bridge",
            ChatExposureMode = ChatExposureMode.Advisory,
            Tools =
            [
                new AiToolDefinition
                {
                    Name = toolCode,
                    ToolName = "read_status",
                    Kind = AiToolCallKind.Mcp,
                    TargetType = AiToolTargetType.McpServer,
                    TargetName = "runtime-mcp",
                    ServerName = "runtime-mcp",
                    ExternalSystemType = AiToolExternalSystemType.NonCloud,
                    CapabilityKind = AiToolCapabilityKind.ReadOnlyQuery,
                    RiskLevel = AiToolRiskLevel.RequiresApproval,
                    RequiresApproval = true,
                    JsonSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
                    ReturnJsonSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
                    InvokeAsync = (_, _) =>
                    {
                        invoked = true;
                        return ValueTask.FromResult<object?>("unexpected");
                    }
                }
            ]
        });
        var (task, workspace) = CreateApprovedTask(toolCode);
        var approval = new ApprovalRequest(task.Id, AgentApprovalType.ToolCall, task.Steps.Single().Id.Value.ToString(), UserId, DateTimeOffset.UtcNow);
        approval.Approve(UserId, "approved", DateTimeOffset.UtcNow);
        var executionRepository = new InMemoryRepository<ToolExecutionRecord>();
        var runtime = CreateRuntime(
            new InMemoryRepository<AgentTask>(task),
            new InMemoryRepository<ArtifactWorkspace>(workspace),
            new InMemoryRepository<ApprovalRequest>(approval),
            executionRepository,
            CreateGuard(CreateTool(
                toolCode,
                ToolProviderType.Mcp,
                ToolRegistrationTargetType.McpServer,
                "runtime-mcp",
                requiresApproval: true,
                riskLevel: AiToolRiskLevel.RequiresApproval,
                inputSchemaJson: """{"type":"object","required":["input"],"properties":{"input":{"type":"string"}}}""")),
            toolExecutors: [new McpAgentToolExecutor(loader, provider)]);

        var result = await runtime.RunAsync(task, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoked.Should().BeFalse();
        task.Status.Should().Be(AgentTaskStatus.Failed);
        var record = executionRepository.Items.Should().ContainSingle().Which;
        record.Status.Should().Be(ToolExecutionStatus.Failed);
        record.ErrorCode.Should().Be(AppProblemCodes.AgentPlanSchemaInvalid);
    }

    private static AgentTaskRuntime CreateRuntime(
        IRepository<AgentTask> taskRepository,
        IRepository<ArtifactWorkspace> workspaceRepository,
        IRepository<ApprovalRequest> approvalRepository,
        IRepository<ToolExecutionRecord> executionRepository,
        ToolRegistryGuard guard,
        IAgentArtifactWorkspaceService? workspaceService = null,
        ICloudReadonlyAgentToolExecutor? cloudReadonlyToolExecutor = null,
        IEnumerable<IAgentToolExecutor>? toolExecutors = null,
        IRepository<AgentTaskRunAttempt>? runAttemptRepository = null,
        IEnumerable<IKnowledgeBaseAccessChecker>? knowledgeBaseAccessCheckers = null,
        IKnowledgeRetrievalService? knowledgeRetrievalService = null,
        IIdentityAccessService? identityAccessService = null)
    {
        return new AgentTaskRuntime(
            taskRepository,
            runAttemptRepository ?? new InMemoryRepository<AgentTaskRunAttempt>(),
            workspaceRepository,
            approvalRepository,
            executionRepository,
            new InMemoryRepository<UploadRecord>(),
            workspaceService ?? new ThrowingWorkspaceService(),
            new NoopFileStorageService(),
            new NoopTableFileParser(),
            new NoopDocumentGenerator(),
            knowledgeRetrievalService ?? new NoopKnowledgeRetrievalService(),
            knowledgeBaseAccessCheckers ?? [],
            cloudReadonlyToolExecutor ?? new ThrowingCloudReadonlyAgentToolExecutor(),
            identityAccessService ?? new StubIdentityAccessService([]),
            guard,
            new AgentAuditRecorder(new CapturingAuditLogWriter()),
            toolExecutors ?? []);
    }

    private static (AgentTask Task, ArtifactWorkspace Workspace) CreateApprovedTask(string toolCode, bool requiresApproval = false)
    {
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "生成报告",
            "生成报告",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            CreatePlanJson(toolCode),
            now);
        task.AddStep("生成图表数据", "生成图表数据。", AgentStepType.ChartGeneration, toolCode, requiresApproval, now);
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            @"C:\aicopilot-workspaces\test",
            "/api/aigateway/workspaces/test",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.ApprovePlan(now);
        return (task, workspace);
    }

    private static (AgentTask Task, ArtifactWorkspace Workspace) CreateRagApprovedTask(Guid knowledgeBaseId)
    {
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "RAG admin-only search",
            "RAG admin-only search",
            AgentTaskType.DataAnalysis,
            AgentTaskRiskLevel.Low,
            null,
            CreateRagPlanJson(knowledgeBaseId),
            now);
        task.AddStep("Search RAG", "Search admin-visible knowledge base.", AgentStepType.DataQuery, "rag_search", false, now);
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            @"C:\aicopilot-workspaces\test",
            "/api/aigateway/workspaces/test",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.ApprovePlan(now);
        return (task, workspace);
    }

    private static (AgentTask Task, ArtifactWorkspace Workspace) CreateCloudApprovedTask()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "Cloud readonly report",
            "Cloud readonly report",
            AgentTaskType.CloudDataReport,
            AgentTaskRiskLevel.Medium,
            null,
            CreateCloudPlanJson(),
            now);
        task.AddStep("Read Cloud", "Read Cloud readonly data.", AgentStepType.DataQuery, "query_cloud_data_readonly", requiresApproval: true, now);
        task.AddStep("Generate Markdown", "Generate markdown report.", AgentStepType.ArtifactGeneration, "generate_markdown_report", requiresApproval: false, now);
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            @"C:\aicopilot-workspaces\test",
            "/api/aigateway/workspaces/test",
            now);
        task.AttachWorkspace(workspace.Id, now);
        task.ApprovePlan(now);
        return (task, workspace);
    }

    private static string CreatePlanJson(string toolCode)
    {
        return $$"""
        {
          "version": 1,
          "plannerTemplateCode": "agent_planner",
          "goal": "生成报告",
          "taskType": "ReportGeneration",
          "riskLevel": "Low",
          "uploadIds": [],
          "knowledgeBaseIds": [],
          "steps": [
            {
              "title": "生成图表数据",
              "description": "生成图表数据。",
              "stepType": "ChartGeneration",
              "toolCode": "{{toolCode}}",
              "requiresApproval": false
            }
          ],
          "runtimeSettings": {
            "agentPlanningHistoryCount": 30,
            "contextTokenLimit": 12000
          }
        }
        """;
    }

    private static string CreateRagPlanJson(Guid knowledgeBaseId)
    {
        var plan = new
        {
            version = 1,
            plannerTemplateCode = "agent_planner",
            goal = "RAG admin-only search",
            taskType = "DataAnalysis",
            riskLevel = "Low",
            uploadIds = Array.Empty<Guid>(),
            knowledgeBaseIds = new[] { knowledgeBaseId },
            steps = new[]
            {
                new
                {
                    title = "Search RAG",
                    description = "Search admin-visible knowledge base.",
                    stepType = "DataQuery",
                    toolCode = "rag_search",
                    requiresApproval = false
                }
            },
            runtimeSettings = new
            {
                agentPlanningHistoryCount = 30,
                contextTokenLimit = 12000
            }
        };
        return JsonSerializer.Serialize(plan, JsonSerializerOptions.Web);
    }

    private static string CreateCloudPlanJson()
    {
        var plan = new
        {
            version = 1,
            plannerTemplateCode = "agent_planner",
            goal = "Cloud readonly report",
            taskType = "CloudDataReport",
            riskLevel = "Medium",
            uploadIds = Array.Empty<Guid>(),
            knowledgeBaseIds = Array.Empty<Guid>(),
            cloudReadonlyIntent = new
            {
                intent = "Analysis.Device.List",
                query = """{"filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"limit":20}""",
                confidence = 0.95,
                target = "Device",
                kind = "List",
                summary = "target=Device; kind=List; filters=1; hasTimeRange=False; limit=20"
            },
            steps = new[]
            {
                new
                {
                    title = "Read Cloud",
                    description = "Read Cloud readonly data.",
                    stepType = "DataQuery",
                    toolCode = "query_cloud_data_readonly",
                    requiresApproval = true
                },
                new
                {
                    title = "Generate Markdown",
                    description = "Generate markdown report.",
                    stepType = "ArtifactGeneration",
                    toolCode = "generate_markdown_report",
                    requiresApproval = false
                }
            },
            runtimeSettings = new
            {
                agentPlanningHistoryCount = 30,
                contextTokenLimit = 12000
            }
        };
        return JsonSerializer.Serialize(plan, JsonSerializerOptions.Web);
    }

    private static SemanticQueryPlan CreateDeviceSemanticPlan()
    {
        return new SemanticQueryPlan(
            "Analysis.Device.List",
            SemanticQueryTarget.Device,
            SemanticQueryKind.List,
            null,
            new SemanticProjection(["deviceCode", "deviceName", "status"]),
            [new SemanticFilter("deviceCode", SemanticFilterOperator.Equal, "DEV-001")],
            null,
            new SemanticSort("deviceCode", SemanticSortDirection.Asc),
            20);
    }

    private static ToolRegistryGuard CreateAgentRuntimeGuardWithCloudEnabled()
    {
        return CreateGuard(
            CreateTool(
                "query_cloud_data_readonly",
                ToolProviderType.CloudReadonly,
                isEnabled: true,
                requiresApproval: true,
                riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("generate_chart_data", ToolProviderType.Artifact),
            CreateTool("generate_markdown_report", ToolProviderType.Artifact),
            CreateTool("generate_html_report", ToolProviderType.Artifact),
            CreateTool("generate_pdf", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("generate_pptx", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("generate_xlsx", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval),
            CreateTool("finalize_artifacts", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval));
    }

    private static AgentPlanToolGuard CreatePlanToolGuard(ToolRegistryGuard guard, params AiToolDefinition[] runtimeTools)
    {
        return new AgentPlanToolGuard(guard, new StubAgentPluginCatalog(runtimeTools));
    }

    private static Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> ValidateSingleAsync(
        AgentPlanToolGuard guard,
        string toolCode,
        string? inputJson = null)
    {
        return guard.ValidateStepsAsync(
            [new AgentStepPlanDto("Step", "Step", AgentStepType.Analysis, toolCode, false, inputJson)],
            AgentTaskType.ReportGeneration,
            UserId,
            CancellationToken.None);
    }

    private static PlanAgentTaskCommandHandler CreatePlanHandler(
        Session session,
        ToolRegistryGuard guard,
        IAgentDynamicPlanner? dynamicPlanner = null,
        IReadOnlyCollection<LanguageModel>? models = null,
        ICloudReadonlyAgentPlanService? cloudReadonlyPlanService = null,
        IReadOnlyCollection<AiToolDefinition>? runtimeTools = null,
        InMemoryRepository<AgentTask>? taskRepository = null)
    {
        return new PlanAgentTaskCommandHandler(
            taskRepository ?? new InMemoryRepository<AgentTask>(),
            new InMemoryRepository<ApprovalRequest>(),
            new InMemoryRepository<Session>(session),
            new InMemoryRepository<UploadRecord>(),
            new InMemoryRepository<ConversationTemplate>(),
            new InMemoryRepository<LanguageModel>((models ?? []).ToArray()),
            new FixedRuntimeSettingsProvider(),
            new ThrowingWorkspaceService(),
            new AgentAuditRecorder(new CapturingAuditLogWriter()),
            [],
            CreatePlanToolGuard(guard, (runtimeTools ?? []).ToArray()),
            dynamicPlanner ?? new ThrowingDynamicPlanner(),
            cloudReadonlyPlanService ?? new FixedCloudReadonlyAgentPlanService(),
            new TestCurrentUser(UserId));
    }

    private static LanguageModel CreatePlannerModel()
    {
        return new LanguageModel(
            "FakeEval",
            "planner",
            "http://localhost/fake",
            "fake-key",
            new ModelParameters { MaxTokens = 4096, MaxOutputTokens = 1024, Temperature = 0.2f },
            "FakeEval",
            LanguageModelUsage.Chat | LanguageModelUsage.Planner,
            true);
    }

    private static ToolRegistryGuard CreateGuard(params ToolRegistration[] tools)
    {
        return CreateGuard((IReadOnlyCollection<ToolRegistration>)tools, []);
    }

    private static ToolRegistryGuard CreateGuard(ToolRegistration tool, params string[] permissions)
    {
        return CreateGuard([tool], permissions);
    }

    private static ToolRegistryGuard CreateGuard(IReadOnlyCollection<ToolRegistration> tools, IReadOnlyCollection<string> permissions)
    {
        return new ToolRegistryGuard(
            new InMemoryRepository<ToolRegistration>(tools.ToArray()),
            new StubIdentityAccessService(permissions));
    }

    private static ToolRegistration CreateTool(
        string toolCode,
        ToolProviderType providerType = ToolProviderType.BuiltIn,
        ToolRegistrationTargetType targetType = ToolRegistrationTargetType.AgentRuntime,
        string targetName = "AgentTaskRuntime",
        bool isEnabled = true,
        bool requiresApproval = false,
        AiToolRiskLevel riskLevel = AiToolRiskLevel.Low,
        string? requiredPermission = null,
        string inputSchemaJson = """{"type":"object"}""",
        string outputSchemaJson = """{"type":"object"}""")
    {
        return new ToolRegistration(
            toolCode,
            toolCode,
            "test tool",
            providerType,
            targetType,
            targetName,
            inputSchemaJson,
            outputSchemaJson,
            riskLevel,
            requiredPermission,
            requiresApproval,
            isEnabled,
            120,
            ToolAuditLevel.Standard,
            DateTimeOffset.UtcNow);
    }

    private static ServiceProvider CreateQueueWorkerProvider(
        InMemoryRepository<AgentTask> taskRepository,
        InMemoryRepository<AgentTaskRunQueueItem> queueRepository,
        InMemoryRepository<AgentTaskRunAttempt> attemptRepository,
        IAgentTaskRuntime runtime)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRepository<AgentTask>>(taskRepository);
        services.AddSingleton<IRepository<AgentTaskRunQueueItem>>(queueRepository);
        services.AddSingleton<IRepository<AgentTaskRunAttempt>>(attemptRepository);
        services.AddSingleton<IAgentTaskRunQueue>(new AgentTaskRunQueue(queueRepository));
        services.AddSingleton(runtime);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingAgentTaskRuntime(IRepository<AgentTaskRunAttempt> attemptRepository) : IAgentTaskRuntime
    {
        public Task<Result<AgentTask>> RunAsync(AgentTask task, CancellationToken cancellationToken = default)
        {
            return RunAsync(task, AgentTaskRunTriggerType.Manual, cancellationToken);
        }

        public async Task<Result<AgentTask>> RunAsync(
            AgentTask task,
            AgentTaskRunTriggerType triggerType = AgentTaskRunTriggerType.Manual,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var attempt = new AgentTaskRunAttempt(
                task.Id,
                task.RunAttemptCount + 1,
                triggerType,
                "test-data-worker",
                now,
                TimeSpan.FromMinutes(5));
            attemptRepository.Add(attempt);
            task.BeginRunAttempt(
                attempt.Id,
                attempt.AttemptNo,
                attempt.LeaseId!.Value,
                attempt.LeaseOwner!,
                attempt.LeaseExpiresAt!.Value,
                now);
            attempt.WaitForApproval(now, "Waiting for final output approval.");
            task.ReleaseRunLease(now, clearActiveAttempt: false);
            await attemptRepository.SaveChangesAsync(cancellationToken);
            return Result.Success(task);
        }
    }

    private sealed class ThrowingRuntime : IAgentTaskRuntime
    {
        public Task<Result<AgentTask>> RunAsync(AgentTask task, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Runtime should not be called by this test.");
        }

        public Task<Result<AgentTask>> RunAsync(
            AgentTask task,
            AgentTaskRunTriggerType triggerType = AgentTaskRunTriggerType.Manual,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Runtime should not be called by this test.");
        }
    }

    private sealed class InMemoryRepository<T>(params T[] initialItems) : IRepository<T>
        where T : class, IEntity, IAggregateRoot
    {
        public List<T> Items { get; } = [..initialItems];

        public T Add(T entity)
        {
            Items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
        {
            Items.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<List<T>> ListAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).ToList());
        }

        public Task<T?> FirstOrDefaultAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).FirstOrDefault());
        }

        public Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Count());
        }

        public Task<bool> AnyAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Any());
        }

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            return Task.FromResult(Items.FirstOrDefault(item => Equals(GetId(item), id)));
        }

        public Task<List<T>> GetListAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Where(expression).ToList());
        }

        public Task<int> GetCountAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Count(expression));
        }

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().FirstOrDefault(expression));
        }

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Where(expression).ToList());
        }

        private IQueryable<T> Apply(ISpecification<T>? specification)
        {
            var query = Items.AsQueryable();
            return specification?.FilterCondition is null
                ? query
                : query.Where(specification.FilterCondition);
        }

        private static object? GetId(T item)
        {
            return typeof(T).GetProperty("Id")?.GetValue(item);
        }
    }

    private sealed class FixedCloudReadonlyAgentPlanService : ICloudReadonlyAgentPlanService
    {
        private readonly Result<CloudReadonlyAgentPlanIntent> result;

        public FixedCloudReadonlyAgentPlanService(Result<CloudReadonlyAgentPlanIntent>? result = null)
        {
            this.result = result ?? Result.Success(new CloudReadonlyAgentPlanIntent(
                "Analysis.Device.List",
                """{"filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"limit":20}""",
                0.95,
                "Device",
                "List",
                "target=Device; kind=List; filters=1; hasTimeRange=False; limit=20"));
        }

        public Task<Result<CloudReadonlyAgentPlanIntent>> CreateIntentAsync(
            Guid sessionId,
            string goal,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingDynamicPlanner : IAgentDynamicPlanner
    {
        public Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> CreatePlanAsync(
            AgentDynamicPlannerRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Dynamic planner should not be called by this test.");
        }
    }

    private sealed class FixedDynamicPlanner(params AgentStepPlanDto[] steps) : IAgentDynamicPlanner
    {
        public AgentDynamicPlannerRequest? LastRequest { get; private set; }

        public Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> CreatePlanAsync(
            AgentDynamicPlannerRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Result.Success<IReadOnlyCollection<AgentStepPlanDto>>(steps));
        }
    }

    private sealed class StubAgentPluginCatalog(params AiToolDefinition[] tools) : IAgentPluginCatalog
    {
        public AiToolDefinition[] GetTools(params string[] names)
        {
            return tools
                .Where(tool => names.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }

        public AiToolDefinition[] GetPluginTools(string name)
        {
            return [];
        }

        public AiToolDefinition[] GetAllTools()
        {
            return tools;
        }

        public IAgentPlugin? GetPlugin(string name)
        {
            return null;
        }

        public IAgentPlugin[] GetAllPlugin()
        {
            return [];
        }
    }

    private sealed class ThrowingCloudReadonlyAgentToolExecutor : ICloudReadonlyAgentToolExecutor
    {
        public Task<CloudReadonlyAgentToolResult> ExecuteAsync(
            CloudReadonlyAgentToolRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud readonly tool executor should not be called by this test.");
        }
    }

    private sealed class FixedSemanticQueryPlanner(SemanticQueryPlan plan) : ISemanticQueryPlanner
    {
        public SemanticPlanningResult Plan(string intent, string? query)
        {
            return SemanticPlanningResult.Success(plan);
        }
    }

    private static CloudReadonlyAgentToolExecutor CreateRealCloudReadonlyExecutor(
        ISemanticQueryPlanner planner,
        ICloudAiReadClient cloudClient)
    {
        var options = Options.Create(new CloudReadonlyOptions
        {
            Mode = CloudReadonlyDataSourceMode.Real,
            Real = new CloudReadonlyRealOptions
            {
                Enabled = true,
                AllowProductionRead = true
            }
        });
        return new CloudReadonlyAgentToolExecutor(new FixedCloudReadonlyDataProviderResolver(
            new RealCloudReadonlyDataProvider(planner, cloudClient, options)));
    }

    private sealed class FixedCloudReadonlyDataProviderResolver(ICloudReadonlyDataProvider provider)
        : ICloudReadonlyDataProviderResolver
    {
        public ICloudReadonlyDataProvider Resolve()
        {
            return provider;
        }
    }

    private sealed class FixedRuntimeSettingsProvider : IChatRuntimeSettingsProvider
    {
        public Task<ChatRuntimeSettingsDto> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatRuntimeSettingsDto(6, 12, 4, 30, 40, 12000));
        }
    }

    private sealed class StubIdentityAccessService(
        IReadOnlyCollection<string> permissions,
        string? roleName = "User") : IIdentityAccessService
    {
        public Task<CurrentUserAccess?> GetCurrentUserAccessAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<CurrentUserAccess?>(new CurrentUserAccess(userId, "test-user", roleName, permissions));
        }

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(string roleName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(permissions);
        }

        public Task SyncRolePermissionsAsync(
            string roleName,
            IEnumerable<string> permissionCodes,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingKnowledgeBaseAccessChecker : IKnowledgeBaseAccessChecker
    {
        public Guid? ObservedUserId { get; private set; }

        public bool? ObservedIsAdmin { get; private set; }

        public Task<bool> CanReadAsync(
            Guid knowledgeBaseId,
            Guid userId,
            bool isAdmin,
            CancellationToken cancellationToken = default)
        {
            ObservedUserId = userId;
            ObservedIsAdmin = isAdmin;
            return Task.FromResult(isAdmin);
        }

        public Task<bool> CanWriteAsync(
            Guid knowledgeBaseId,
            Guid userId,
            bool isAdmin,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(isAdmin);
        }
    }

    private sealed class TestAgentToolExecutor(Func<ToolRegistration, bool> canExecute) : IAgentToolExecutor
    {
        public bool CanExecute(ToolRegistration tool, AgentStep step)
        {
            return canExecute(tool);
        }

        public Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context)
        {
            return Task.FromResult(AgentToolExecutionResult.From(new { ok = true }));
        }
    }

    private sealed class CapturingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditLogWriteRequest> Requests { get; } = [];

        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Requests.Count);
        }
    }

    private sealed class FixedAuditLogQueryService(params AuditLogSummaryDto[] logs) : IAuditLogQueryService
    {
        public Task<AuditLogListDto> GetListAsync(
            int page,
            int pageSize,
            string? actionGroup,
            string? actionCode,
            string? targetType,
            string? targetName,
            string? operatorUserName,
            string? result,
            DateTime? from,
            DateTime? to,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AuditLogListDto(page, pageSize, logs.Length, logs));
        }
    }

    private sealed class FixedWorkspaceFingerprintProvider(string hash) : IAgentWorkspaceFingerprintProvider
    {
        public string GetWorkspaceRootHash()
        {
            return hash;
        }
    }

    private sealed class ThrowingWorkspaceService(bool throwOnWrite = false) : IAgentArtifactWorkspaceService
    {
        public Task<ArtifactWorkspace> CreateForTaskAsync(
            AgentTask task,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ArtifactWorkspace(
                task.Id,
                $"ws_{Guid.NewGuid():N}",
                @"C:\aicopilot-workspaces\test",
                "/api/aigateway/workspaces/test",
                nowUtc));
        }

        public Task<Artifact> WriteDraftTextArtifactAsync(
            ArtifactWorkspace workspace,
            ArtifactType artifactType,
            string name,
            string relativePath,
            string content,
            string mimeType,
            AgentStepId? stepId,
            CancellationToken cancellationToken)
        {
            if (throwOnWrite)
            {
                throw new InvalidOperationException(@"apiKey: sk-test C:\secrets\report.txt Host=db;Password=super-secret;");
            }

            return Task.FromResult(workspace.AddDraftArtifact(
                artifactType,
                name,
                relativePath,
                content.Length,
                mimeType,
                stepId,
                DateTimeOffset.UtcNow));
        }

        public Task<Artifact> WriteDraftBinaryArtifactAsync(
            ArtifactWorkspace workspace,
            ArtifactType artifactType,
            string name,
            string relativePath,
            byte[] content,
            string mimeType,
            AgentStepId? stepId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(workspace.AddDraftArtifact(
                artifactType,
                name,
                relativePath,
                content.Length,
                mimeType,
                stepId,
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class CapturingWorkspaceService(ArtifactWorkspace workspace) : IAgentArtifactWorkspaceService
    {
        public Dictionary<string, string> TextArtifacts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<ArtifactWorkspace> CreateForTaskAsync(
            AgentTask task,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(workspace);
        }

        public Task<Artifact> WriteDraftTextArtifactAsync(
            ArtifactWorkspace artifactWorkspace,
            ArtifactType artifactType,
            string name,
            string relativePath,
            string content,
            string mimeType,
            AgentStepId? stepId,
            CancellationToken cancellationToken)
        {
            TextArtifacts[relativePath] = content;
            return Task.FromResult(artifactWorkspace.AddDraftArtifact(
                artifactType,
                name,
                relativePath,
                content.Length,
                mimeType,
                stepId,
                DateTimeOffset.UtcNow));
        }

        public Task<Artifact> WriteDraftBinaryArtifactAsync(
            ArtifactWorkspace artifactWorkspace,
            ArtifactType artifactType,
            string name,
            string relativePath,
            byte[] content,
            string mimeType,
            AgentStepId? stepId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(artifactWorkspace.AddDraftArtifact(
                artifactType,
                name,
                relativePath,
                content.Length,
                mimeType,
                stepId,
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class NoopFileStorageService : IFileStorageService
    {
        public Task<string> SaveAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(fileName);
        }

        public Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream?>(null);
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoopTableFileParser : IAgentTableFileParser
    {
        public Task<AgentReportTable?> ParseAsync(
            AgentTableFileParseRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AgentReportTable?>(null);
        }
    }

    private sealed class NoopDocumentGenerator : IAgentArtifactDocumentGenerator
    {
        public Task<byte[]> GeneratePdfAsync(AgentReportDocument document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Array.Empty<byte>());
        }

        public Task<byte[]> GeneratePptxAsync(AgentReportDocument document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Array.Empty<byte>());
        }

        public Task<byte[]> GenerateXlsxAsync(AgentReportDocument document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Array.Empty<byte>());
        }
    }

    private sealed class NoopKnowledgeRetrievalService : IKnowledgeRetrievalService
    {
        public Task<IReadOnlyList<KnowledgeRetrievalResult>> SearchAsync(
            Guid knowledgeBaseId,
            string queryText,
            int topK,
            double minScore,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<KnowledgeRetrievalResult>>([]);
        }
    }

    private sealed class DisabledCloudAiReadClient : ICloudAiReadClient
    {
        public bool IsEnabled => false;

        public Task<JsonDocument> SendJsonAsync(
            HttpMethod method,
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadPassStationRecordDto>> GetPassStationRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingCloudAiReadClient(CloudAiReadResult<object> result) : ICloudAiReadClient
    {
        public List<SemanticQueryPlan> RequestedPlans { get; } = [];

        public bool IsEnabled => true;

        public Task<JsonDocument> SendJsonAsync(
            HttpMethod method,
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<CloudAiReadPassStationRecordDto>> GetPassStationRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            RequestedPlans.Add(plan);
            return Task.FromResult(result);
        }
    }

    private sealed class FailingCloudAiReadClient(CloudAiReadException exception) : ICloudAiReadClient
    {
        public bool IsEnabled => true;

        public Task<JsonDocument> SendJsonAsync(
            HttpMethod method,
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<CloudAiReadPassStationRecordDto>> GetPassStationRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }
    }
}
