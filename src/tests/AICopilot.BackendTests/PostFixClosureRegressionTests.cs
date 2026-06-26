using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Safety;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.BackendTests;

public sealed class PostFixClosureRegressionTests
{
    [Fact]
    public void RuntimeSettings_ShouldNotExposeSummaryThresholdCapability()
    {
        var solutionRoot = FindSolutionRoot();
        var runtimeSettingsSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Runtime",
            "ChatRuntimeSettingsManagement.cs"));
        var domainSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "core",
            "AICopilot.Core.AiGateway",
            "Aggregates",
            "RuntimeSettings",
            "ChatRuntimeSettings.cs"));
        var efConfigSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Configuration",
            "AiGateway",
            "ChatRuntimeSettingsConfiguration.cs"));

        runtimeSettingsSource.Should().NotContain("SummaryThresholdMessages");
        runtimeSettingsSource.Should().NotContain("ReservedSummaryThresholdMessages");
        domainSource.Should().NotContain("SummaryThresholdMessages");
        domainSource.Should().NotContain("ReservedSummaryThresholdMessages");
        efConfigSource.Should().NotContain("summary_threshold_messages");
    }

    [Fact]
    public void AgentTaskPlanDocument_ShouldDefaultMockMcpOnlyToFalse()
    {
        var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(
            """
            {
              "version": 1,
              "plannerTemplateCode": "agent_task_planner",
              "goal": "只生成计划",
              "taskType": "General",
              "riskLevel": "Low",
              "uploadIds": [],
              "knowledgeBaseIds": [],
              "steps": [],
              "runtimeSettings": {
                "agentPlanningHistoryCount": 6,
                "contextTokenLimit": 24000
              }
            }
            """,
            AgentRuntimeJson.Options);

        plan.Should().NotBeNull();
        plan!.MockMcpOnly.Should().BeFalse();
        plan.PlannerSafetySummary.Should().BeNull();
    }

    [Fact]
    public void AgentTaskPlanSafetySummary_ShouldDefaultMockMcpOnlyToFalse()
    {
        var summary = JsonSerializer.Deserialize<AgentTaskPlanSafetySummaryDocument>(
            """
            {
              "planSource": "PlanDraft",
              "plannerMode": "PlanDraft",
              "availableToolCount": 0,
              "isSimulationOnly": false,
              "requiresDataApproval": false,
              "plannerToolCatalogVersion": 1
            }
            """,
            AgentRuntimeJson.Options);

        summary.Should().NotBeNull();
        summary!.MockMcpOnly.Should().BeFalse();
    }

    [Fact]
    public void WorkflowExceptionDetail_ShouldNotExposeModelProviderOrQueryText()
    {
        var chunk = AgentStreamRuntime.CreateErrorChunk(
            new AgentWorkflowException(
                AppProblemCodes.ChatConfigurationMissing,
                "Language model 'secret-model' on provider 'internal-provider' failed SELECT * FROM prod.table WHERE token=abc",
                "当前模型不可用，请联系管理员检查配置。"),
            "test",
            AppProblemCodes.ChatStreamFailed,
            "对话执行失败，请稍后重试。");

        var payload = JsonSerializer.Deserialize<ChatErrorPayload>(chunk.Content, JsonSerializerOptions.Web);

        payload.Should().NotBeNull();
        payload!.Code.Should().Be(AppProblemCodes.ChatConfigurationMissing);
        payload.Detail.Should().Contain("对话运行配置不可用");
        chunk.Content.Should().NotContain("secret-model");
        chunk.Content.Should().NotContain("internal-provider");
        chunk.Content.Should().NotContain("prod.table");
        chunk.Content.Should().NotContain("token=abc");
    }

    [Fact]
    public void AgentStreamRuntime_ShouldStripThinkTagsBeforeSseTextChunks()
    {
        var runtime = new AgentStreamRuntime(new ApprovalRequirementResolver(null!));
        var update = new RuntimeAgentUpdate(
            [new AiTextContent("<mm:think>内部推理</mm:think>最终回答")]);
        var chunks = runtime.CreateUpdateChunksAsync(
                update,
                "test",
                session: null,
                assistantText: new System.Text.StringBuilder(),
                appendAssistantText: true,
                CancellationToken.None)
            .ToBlockingEnumerable()
            .ToArray();

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Be("最终回答");
        chunks[0].Content.Should().NotContain("think");
        chunks[0].Content.Should().NotContain("内部推理");
    }

    [Fact]
    public void AgentStreamRuntime_ShouldStripThinkTagsAcrossRuntimeUpdates()
    {
        var runtime = new AgentStreamRuntime(new ApprovalRequirementResolver(null!));
        var assistantText = new System.Text.StringBuilder();
        var thinkTagFilter = new StreamingThinkTagFilter();

        var firstChunks = runtime.CreateUpdateChunksAsync(
                new RuntimeAgentUpdate([new AiTextContent("<mm:think>内部")]),
                "test",
                session: null,
                assistantText,
                appendAssistantText: true,
                CancellationToken.None,
                thinkTagFilter)
            .ToBlockingEnumerable()
            .ToArray();
        var secondChunks = runtime.CreateUpdateChunksAsync(
                new RuntimeAgentUpdate([new AiTextContent("推理</mm:think>最终回答")]),
                "test",
                session: null,
                assistantText,
                appendAssistantText: true,
                CancellationToken.None,
                thinkTagFilter)
            .ToBlockingEnumerable()
            .ToArray();

        firstChunks.Should().BeEmpty();
        secondChunks.Should().ContainSingle();
        secondChunks[0].Content.Should().Be("最终回答");
        assistantText.ToString().Should().Be("最终回答");
    }

    [Fact]
    public void ProductionCompositionRoot_ShouldUsePostgreSqlSessionExecutionLock()
    {
        var solutionRoot = FindSolutionRoot();
        var infrastructureSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.Infrastructure",
            "DependencyInjection.cs"));
        var serviceSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        infrastructureSource.Should().Contain("AddSingleton<ISessionExecutionLock>");
        infrastructureSource.Should().Contain("PostgreSqlSessionExecutionLock");
        infrastructureSource.Should().Contain("ConnectionStrings:ai-copilot");
        infrastructureSource.Should().NotContain("InMemorySessionExecutionLock");
        serviceSource.Should().Contain("TryAddSingleton<ISessionExecutionLock, InMemorySessionExecutionLock>");
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AICopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Cannot locate solution root.");
    }

    private sealed record ChatErrorPayload(string? Code, string? Detail, string? UserFacingMessage);
}
