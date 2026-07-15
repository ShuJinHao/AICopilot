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

namespace AICopilot.UnitTests;

public sealed class ToolRegistryUnitTests : ToolRegistryGovernanceTestBase
{
    [Fact]
    public void BuiltInToolCatalog_ShouldExposeChineseDisplayNames()
    {
        BuiltInToolRegistrations.CurrentCatalogVersion.Should().BeGreaterThanOrEqualTo(12);
        var tools = BuiltInToolRegistrations.AgentRuntimeTools
            .ToDictionary(tool => tool.ToolCode, StringComparer.Ordinal);

        tools["read_uploaded_file"].DisplayName.Should().Be("读取上传文件");
        tools["parse_csv_json"].DisplayName.Should().Be("解析 CSV/JSON");
        tools["query_cloud_data_readonly"].DisplayName.Should().Be("查询 Cloud 只读数据");
        tools["generate_business_chart"].DisplayName.Should().Be("生成业务图表");
        tools["finalize_artifacts"].DisplayName.Should().Be("最终产物确认");
        var displayNames = tools.Values.Select(tool => tool.DisplayName).ToArray();
        displayNames.Should().NotContain("Finalize artifacts");
        displayNames.Should().NotContain("Generate business chart");
        displayNames.Should().NotContain("Parse CSV/JSON");
    }
    [Fact]
    public void AgentSkillRouterAutoSelector_ParseSelection_ShouldParseSimplifiedSkillObject()
    {
        var selected = AgentSkillRouterAutoSelector.ParseSelection(
            """
            ```json
            {"skillCode":"cloud_readonly","reason":"用户要求查看云端设备日志并生成报告。"}
            ```
            """);

        selected.Should().Be(new AgentSkillSelection(
            "cloud_readonly",
            "用户要求查看云端设备日志并生成报告。"));
    }
    [Fact]
    public void AgentSkillRouterAutoSelector_ParseSelection_ShouldKeepNoMatchReason()
    {
        var selected = AgentSkillRouterAutoSelector.ParseSelection(
            """{"skillCode":null,"reason":"目标不明确，需要用户补充。"}""");

        selected.Should().Be(new AgentSkillSelection(null, "目标不明确，需要用户补充。"));
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
    public async Task MockMcpAgentToolExecutor_ShouldReturnMockMarkers_AndResultHash()
    {
        var now = DateTimeOffset.UtcNow;
        var tool = CreateTool(
            "mock_mcp_kpi_formula_lookup",
            ToolProviderType.MockMcp,
            targetName: "MockMcpProvider",
            inputSchemaJson: """{"type":"object","properties":{"domain":{"type":"string"}}}""");
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "Mock MCP KPI",
            "Mock MCP KPI",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            "{}",
            now);
        var step = task.AddStep(
            "Lookup KPI formula",
            "Lookup mock KPI formula.",
            AgentStepType.Analysis,
            tool.ToolCode,
            false,
            now,
            """{"domain":"Production"}""");
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            @"C:\aicopilot-workspaces\test",
            "/api/aigateway/workspaces/test",
            now);
        var plan = new AgentTaskPlanDocument(
            1,
            "agent_planner",
            "Mock MCP KPI",
            AgentTaskType.ReportGeneration.ToString(),
            AgentTaskRiskLevel.Low.ToString(),
            [],
            [],
            null,
            [],
            new AgentTaskPlanRuntimeSettingsDocument(30, 12000),
            ToolCatalogVersion: BuiltInToolRegistrations.CurrentCatalogVersion,
            VisibleToolCount: 1,
            ToolRiskSummary: new Dictionary<string, int> { [AiToolRiskLevel.Low.ToString()] = 1 },
            MockMcpOnly: false);

        var executor = new MockMcpAgentToolExecutor();
        var result = await executor.ExecuteAsync(new AgentToolExecutionContext(
            task,
            workspace,
            plan,
            step,
            new AgentTaskRunState(),
            tool,
            CancellationToken.None));

        var json = JsonSerializer.Serialize(result.Output, JsonSerializerOptions.Web);
        json.Should().Contain("\"isMock\":true");
        json.Should().Contain("\"providerKind\":\"MockMcp\"");
        json.Should().Contain("\"toolRunId\"");
        json.Should().Contain("\"toolCatalogVersion\"");
        json.Should().Contain("\"resultHash\"");
        json.Should().Contain("capacityUtilization");
    }
}
