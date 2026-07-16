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
using AICopilot.AiGatewayService.Skills;
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
using AICopilot.Core.AiGateway.Aggregates.Skills;
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
        var handler = new PlanAgentTaskCommandHandler(
            new PlanAgentTaskCoordinator(
                taskRepository,
                approvalRepository,
                new InMemoryRepository<Session>(session),
                new InMemoryRepository<UploadRecord>(),
                new AgentAuditRecorder(new CapturingAuditLogWriter()),
                [],
                new TestCurrentUser(UserId)));

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

        var approvalAudit = new CapturingAuditLogWriter();
        var approveHandler = new ApproveAgentTaskPlanCommandHandler(
            taskRepository,
            approvalRepository,
            CreateAgentTaskDtoQueryService(
                new InMemoryRepository<ArtifactWorkspace>(),
                approvalRepository,
                new InMemoryAgentTaskRunQueueStore()),
            new AgentAuditRecorder(approvalAudit),
            new TestCurrentUser(UserId),
            new AgentPlanDraftConfirmationService(CreatePlanToolGuard(CreateGuard(CreateTool(
                "query_cloud_data_readonly",
                ToolProviderType.CloudReadonly,
                isEnabled: false,
                requiresApproval: true,
                riskLevel: AiToolRiskLevel.RequiresApproval))),
                new FixedCloudReadonlyAgentPlanService()));

        var confirmation = await approveHandler.Handle(
            new ApproveAgentTaskPlanCommand(task.Id.Value),
            CancellationToken.None);

        confirmation.IsSuccess.Should().BeFalse();
        confirmation.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
            AppProblemCodes.CloudReadonlyToolDisabled,
            "Tool 'query_cloud_data_readonly' is disabled."));
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
                new InMemoryRepository<ApprovalRequest>(),
                new InMemoryRepository<Session>(session),
                new InMemoryRepository<UploadRecord>(),
                new AgentAuditRecorder(new CapturingAuditLogWriter()),
                [],
                new TestCurrentUser(UserId)));

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "read recipe version details", AgentTaskType.CloudDataReport, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        using var plan = JsonDocument.Parse(result.Value!.PlanJson);
        plan.RootElement.GetProperty("planKind").GetString().Should().Be("PlanDraft");
        plan.RootElement.GetProperty("cloudReadonlyIntent").ValueKind.Should().Be(JsonValueKind.Null);

        var task = taskRepository.Items.Should().ContainSingle().Which;
        var confirmation = await new AgentPlanDraftConfirmationService(
                CreatePlanToolGuard(CreateAgentRuntimeGuardWithCloudEnabled()),
                cloudReadonlyPlanService)
            .ConfirmAsync(task, DateTimeOffset.UtcNow, CancellationToken.None);

        confirmation.IsSuccess.Should().BeFalse();
        confirmation.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.CloudReadonlyIntentUnsupported,
                "Recipe data is outside the Cloud readonly agent boundary."));
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
                new FixedCloudReadonlyAgentPlanService())
            .ConfirmAsync(task, DateTimeOffset.UtcNow, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Agent task plan JSON is invalid and cannot be confirmed."));
        task.Status.Should().Be(AgentTaskStatus.Draft);
    }
    [Fact]
    public async Task PlanDraft_ShouldUseUnifiedStaticDraft_WhenNoPlannerModelIsAvailable()
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
        plan.RootElement.GetProperty("plannerMode").GetString().Should().Be("PlanDraft");
        plan.RootElement.GetProperty("plannerFallbackReason").ValueKind.Should().Be(JsonValueKind.Null);
        plan.RootElement.GetProperty("plannerValidationVersion").GetInt32().Should().Be(1);
        plan.RootElement.GetProperty("plannerAvailableToolCount").GetInt32().Should().Be(0);
        plan.RootElement.GetProperty("steps").EnumerateArray()
            .Select(step => step.GetProperty("toolCode").GetString())
            .Should().Contain("generate_markdown_report");
        var stepTitles = plan.RootElement.GetProperty("steps").EnumerateArray()
            .Select(step => step.GetProperty("title").GetString())
            .ToArray();
        stepTitles.Should().Contain("生成 Markdown 报告");
        stepTitles.Should().NotContain("Generate Markdown report");
    }
    [Fact]
    public async Task PlanDraft_ShouldNotCallDynamicPlannerOrToolCatalog_WhenPlannerModelIsAvailable()
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
        result.Value!.ModelId.Should().BeNull();
        dynamicPlanner.LastRequest.Should().BeNull();
        using var plan = JsonDocument.Parse(result.Value.PlanJson);
        plan.RootElement.GetProperty("plannerMode").GetString().Should().Be("PlanDraft");
        plan.RootElement.GetProperty("plannerModelId").ValueKind.Should().Be(JsonValueKind.Null);
        plan.RootElement.GetProperty("plannerValidationVersion").GetInt32().Should().Be(1);
        plan.RootElement.GetProperty("plannerToolCatalogVersion").GetInt32().Should().Be(PlannerToolCatalog.CurrentVersion);
        plan.RootElement.GetProperty("plannerAvailableToolCount").GetInt32().Should().Be(0);
        var markdownStep = plan.RootElement.GetProperty("steps").EnumerateArray()
            .Single(step => step.GetProperty("toolCode").GetString() == "generate_markdown_report");
        markdownStep.GetProperty("inputJson").ValueKind.Should().Be(JsonValueKind.Null);
    }
    [Fact]
    public async Task PlanDraft_ShouldIgnoreDynamicPlannerFailure_UntilUserConfirmsExecution()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var plannerModel = CreatePlannerModel();
        var taskRepository = new InMemoryRepository<AgentTask>();
        var handler = CreatePlanHandler(
            session,
            CreateAgentRuntimeGuardWithCloudEnabled(),
            new FailingDynamicPlanner(AppProblemCodes.AgentPlanInvalid, "Planner returned invalid JSON: HTTP 400"),
            [plannerModel],
            taskRepository: taskRepository);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "generate a report", AgentTaskType.ReportGeneration, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var task = taskRepository.Items.Should().ContainSingle().Which;
        using var plan = JsonDocument.Parse(task.PlanJson);
        plan.RootElement.GetProperty("plannerMode").GetString().Should().Be("PlanDraft");
        plan.RootElement.GetProperty("plannerFallbackReason").ValueKind.Should().Be(JsonValueKind.Null);
    }
    [Fact]
    public async Task PreferredToolCodes_ShouldNotNarrowPlannerCatalog_BeforePlanDraftConfirmation()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var plannerModel = CreatePlannerModel();
        var dynamicPlanner = new FixedDynamicPlanner(
            new AgentStepPlanDto(
                "Build markdown",
                "Generate markdown report.",
                AgentStepType.ArtifactGeneration,
                "generate_markdown_report",
                false));
        var handler = CreatePlanHandler(
            session,
            CreateAgentRuntimeGuardWithCloudEnabled(),
            dynamicPlanner,
            [plannerModel]);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(
                session.Id.Value,
                "generate a markdown report",
                AgentTaskType.ReportGeneration,
                null,
                PreferredToolCodes: ["generate_markdown_report"]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dynamicPlanner.LastRequest.Should().BeNull();
        using var plan = JsonDocument.Parse(result.Value!.PlanJson);
        plan.RootElement.GetProperty("plannerAvailableToolCount").GetInt32().Should().Be(0);
        plan.RootElement.GetProperty("steps").EnumerateArray()
            .Select(step => step.GetProperty("toolCode").GetString())
            .Should().Contain("finalize_artifacts");
    }
    [Fact]
    public async Task PreferredToolCodes_ShouldNotBlockPlanDraft_WhenToolIsOutsideCurrentCatalog()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var plannerModel = CreatePlannerModel();
        var handler = CreatePlanHandler(
            session,
            CreateGuard(
                CreateTool("generate_markdown_report", ToolProviderType.Artifact),
                CreateTool("finalize_artifacts", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval)),
            new FixedDynamicPlanner(),
            [plannerModel]);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(
                session.Id.Value,
                "generate a report",
                AgentTaskType.ReportGeneration,
                null,
                PreferredToolCodes: ["generate_pdf"]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        using var plan = JsonDocument.Parse(result.Value!.PlanJson);
        plan.RootElement.GetProperty("planKind").GetString().Should().Be("PlanDraft");
        plan.RootElement.GetProperty("plannerAvailableToolCount").GetInt32().Should().Be(0);
        plan.RootElement.GetProperty("capabilityGaps").EnumerateArray()
            .Select(item => item.GetString())
            .Should().NotContain(item => item!.Contains(AppProblemCodes.AgentPlanToolDenied, StringComparison.Ordinal));
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
    public async Task SkillDefinition_ShouldNarrowPlannerCatalog_AfterToolRegistryGuard()
    {
        var guard = new AgentPlanToolGuard(
            CreateGuard(
                CreateTool("generate_markdown_report", ToolProviderType.Artifact),
                CreateTool("generate_pdf", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval),
                CreateTool("disabled_tool", ToolProviderType.Artifact, isEnabled: false)),
            new StubAgentPluginCatalog(),
            CreateSkillGuard(CreateSkill("restricted_skill", ["generate_markdown_report", "disabled_tool"])));

        var catalog = await guard.GetAvailableToolCatalogAsync(
            UserId,
            simulationOnly: false,
            businessDomains: null,
            CancellationToken.None,
            "restricted_skill");

        catalog.IsSuccess.Should().BeTrue();
        catalog.Value!.Tools.Select(tool => tool.ToolCode)
            .Should().BeEquivalentTo(["generate_markdown_report"]);
    }
    [Fact]
    public async Task SkillDefinition_ShouldRejectPlannerStepOutsideSelectedSkill()
    {
        var guard = new AgentPlanToolGuard(
            CreateGuard(
                CreateTool("generate_markdown_report", ToolProviderType.Artifact),
                CreateTool("generate_pdf", ToolProviderType.Artifact, requiresApproval: true, riskLevel: AiToolRiskLevel.RequiresApproval)),
            new StubAgentPluginCatalog(),
            CreateSkillGuard(CreateSkill("restricted_skill", ["generate_markdown_report"])));

        var result = await guard.ValidateStepsAsync(
            [new AgentStepPlanDto("PDF", "Generate PDF.", AgentStepType.ArtifactGeneration, "generate_pdf", true)],
            AgentTaskType.ReportGeneration,
            UserId,
            simulationOnly: false,
            businessDomains: null,
            CancellationToken.None,
            "restricted_skill");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanToolDenied,
                "Tool 'generate_pdf' is outside skill 'restricted_skill'."));
    }
    [Fact]
    public async Task PlanAgentTask_ShouldUseAutoSelectedCloudReadonlySkill()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var taskRepository = new InMemoryRepository<AgentTask>();
        var skillGuard = CreateSkillGuard(CreateSkill(
            "cloud_readonly",
            [
                "query_cloud_data_readonly",
                "generate_chart_data",
                "generate_markdown_report",
                "generate_html_report",
                "finalize_artifacts"
            ]));
        var handler = CreatePlanHandler(
            session,
            CreateAgentRuntimeGuardWithCloudEnabled(),
            taskRepository: taskRepository,
            skillDefinitionGuard: skillGuard,
            skillAutoSelector: new FixedSkillAutoSelector("cloud_readonly"));

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "查看 DEV-001 最近设备日志", AgentTaskType.ReportGeneration, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var task = taskRepository.Items.Should().ContainSingle().Which;
        task.TaskType.Should().Be(AgentTaskType.CloudDataReport);
        task.Steps.Should().Contain(step => step.ToolCode == "query_cloud_data_readonly");
        using var plan = JsonDocument.Parse(task.PlanJson);
        plan.RootElement.GetProperty("skillCode").GetString().Should().Be("cloud_readonly");
        plan.RootElement.GetProperty("skillRoutingReason").GetString().Should().Be("test selector");
        plan.RootElement.GetProperty("taskType").GetString().Should().Be("CloudDataReport");
        plan.RootElement.GetProperty("plannerSafetySummary").GetProperty("planSource").GetString()
            .Should().Be("Skill.cloud_readonly");
    }
    [Fact]
    public async Task PlanAgentTask_ShouldCreateDraftWhenAutoSkillSelectorCannotMatch()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var taskRepository = new InMemoryRepository<AgentTask>();
        var handler = CreatePlanHandler(
            session,
            CreateAgentRuntimeGuardWithCloudEnabled(),
            taskRepository: taskRepository,
            skillDefinitionGuard: CreateSkillGuard(),
            skillAutoSelector: new FixedSkillAutoSelector(null, "目标不明确，需要用户补充。"));

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "帮我看看", AgentTaskType.ReportGeneration, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var task = taskRepository.Items.Should().ContainSingle().Which;
        using var plan = JsonDocument.Parse(task.PlanJson);
        plan.RootElement.GetProperty("planKind").GetString().Should().Be("PlanDraft");
        plan.RootElement.GetProperty("skillCode").ValueKind.Should().Be(JsonValueKind.Null);
        plan.RootElement.GetProperty("capabilityGaps").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("Skill 自动识别未命中：目标不明确，需要用户补充。");
    }
    [Fact]
    public async Task PlanDraft_ShouldNotCreateToolCatalogGap_WhenCatalogWouldBeEmpty()
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

        result.IsSuccess.Should().BeTrue();
        using var plan = JsonDocument.Parse(result.Value!.PlanJson);
        plan.RootElement.GetProperty("planKind").GetString().Should().Be("PlanDraft");
        plan.RootElement.GetProperty("isExecutable").GetBoolean().Should().BeFalse();
        plan.RootElement.GetProperty("plannerAvailableToolCount").GetInt32().Should().Be(0);
        plan.RootElement.GetProperty("capabilityGaps").EnumerateArray()
            .Select(item => item.GetString())
            .Should().NotContain("No enabled and authorized tools are currently available for this PlanDraft. Tool validation is deferred until confirmation.");
    }
    [Fact]
    public async Task PlanDraft_ShouldNotExposeRuntimeMcpToolAsExecutableStepBeforeConfirmation()
    {
        var session = new Session(UserId, ConversationTemplateId.New());
        var plannerModel = CreatePlannerModel();
        var mcpToolCode = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "runtime-mcp", "read_status");
        var runtimeTools = new[]
        {
            new AiToolDefinition
            {
                Name = mcpToolCode,
                TargetType = AiToolTargetType.McpServer,
                TargetName = "runtime-mcp"
            }
        };
        var toolGuard = CreateGuard(
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
                riskLevel: AiToolRiskLevel.RequiresApproval));
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
            toolGuard,
            dynamicPlanner,
            [plannerModel],
            runtimeTools: runtimeTools,
            taskRepository: taskRepository);

        var result = await handler.Handle(
            new PlanAgentTaskCommand(session.Id.Value, "read mcp status", AgentTaskType.ReportGeneration, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dynamicPlanner.LastRequest.Should().BeNull();
        var task = taskRepository.Items.Should().ContainSingle().Which;
        task.Steps.Should().NotContain(step => string.Equals(step.ToolCode, mcpToolCode, StringComparison.OrdinalIgnoreCase));
        using var plan = JsonDocument.Parse(task.PlanJson);
        plan.RootElement.GetProperty("plannerAvailableToolCount").GetInt32().Should().Be(0);
    }
    [Fact]
    public async Task ConfirmPlanDraft_ShouldRejectInputJson_WhenSchemaDoesNotMatch()
    {
        var toolGuard = CreateGuard(
            CreateTool(
                "generate_markdown_report",
                ToolProviderType.Artifact,
                inputSchemaJson: """{"type":"object","properties":{"format":{"type":"string","enum":["markdown"]}},"required":["format"]}"""),
            CreateTool(
                "finalize_artifacts",
                ToolProviderType.Artifact,
                requiresApproval: true,
                riskLevel: AiToolRiskLevel.RequiresApproval));
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "generate a markdown report",
            "generate a markdown report",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            CreatePlanJson("generate_markdown_report", inputJson: """{"format":"html"}"""),
            now);
        task.AddStep(
            "Build markdown",
            "Generate markdown report.",
            AgentStepType.ArtifactGeneration,
            "generate_markdown_report",
            false,
            now,
            """{"format":"html"}""");

        var confirmation = await new AgentPlanDraftConfirmationService(
                CreatePlanToolGuard(toolGuard),
                new FixedCloudReadonlyAgentPlanService())
            .ConfirmAsync(task, DateTimeOffset.UtcNow, CancellationToken.None);

        confirmation.IsSuccess.Should().BeFalse();
        confirmation.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanSchemaInvalid,
                "Tool input field '$.format' is not one of the allowed values."));
    }
    [Fact]
    public async Task PlanDraft_ShouldNotRejectExplicitModelBeforeExecutableConfirmation()
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

        result.IsSuccess.Should().BeTrue();
        result.Value!.ModelId.Should().Be(chatOnlyModel.Id.Value);
        using var plan = JsonDocument.Parse(result.Value.PlanJson);
        plan.RootElement.GetProperty("planKind").GetString().Should().Be("PlanDraft");
        plan.RootElement.GetProperty("plannerMode").GetString().Should().Be("PlanDraft");
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
            task.Steps.Single().Id.Value.ToString(),
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
        var (task, workspace) = CreateApprovedTask("generate_pdf");
        var executionRepository = new InMemoryToolExecutionAuditStore();
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
