using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.BackendTests;

[Trait("Suite", "DynamicPlannerContract")]
public sealed class DynamicPlannerContractTests
{
    [Theory]
    [InlineData("", "Planner returned invalid JSON: Planner response was empty.")]
    [InlineData("```json\n{\"steps\":[]}\n```", "Planner returned invalid JSON: Planner response must be raw JSON and must not be wrapped in Markdown.")]
    [InlineData("{\"steps\":[],\"extra\":true}", "Planner output contains unknown root field 'extra'.")]
    [InlineData("{\"steps\":[{\"title\":\"x\",\"description\":\"x\",\"stepType\":\"Analysis\",\"toolCode\":\"generate_markdown_report\",\"unknown\":true}]}", "Planner step contains unknown field 'unknown'.")]
    [InlineData("{\"steps\":[{\"title\":\"x\",\"description\":\"x\",\"stepType\":\"Analysis\",\"toolCode\":\"generate_markdown_report\",\"inputJson\":[]}]}", "Planner step inputJson must be omitted, a JSON object, or a JSON object string.")]
    public async Task DefaultDynamicPlanner_ShouldRejectInvalidOutputs(string responseText, string expectedDetail)
    {
        var runtimeFactory = new FakeRuntimeAgentFactory();
        runtimeFactory.EnqueueText(responseText);
        var (planner, request) = CreatePlanner(runtimeFactory);

        var result = await planner.CreatePlanAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                expectedDetail));
    }

    [Fact]
    public async Task DefaultDynamicPlanner_ShouldRejectOversizedStepInputJson()
    {
        var oversized = new string('x', 8100);
        var responseText = JsonSerializer.Serialize(new
        {
            steps = new[]
            {
                new
                {
                    title = "x",
                    description = "x",
                    stepType = "Analysis",
                    toolCode = "generate_markdown_report",
                    inputJson = new { payload = oversized }
                }
            }
        });
        var runtimeFactory = new FakeRuntimeAgentFactory();
        runtimeFactory.EnqueueText(responseText);
        var (planner, request) = CreatePlanner(runtimeFactory);

        var result = await planner.CreatePlanAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Planner step inputJson exceeds the allowed length."));
    }

    [Fact]
    public async Task DefaultDynamicPlanner_ShouldRedactSecretsPathsAndSqlFromPlannerInput()
    {
        var runtimeFactory = new FakeRuntimeAgentFactory();
        runtimeFactory.EnqueueText(
            """{"steps":[{"title":"x","description":"x","stepType":"Analysis","toolCode":"generate_markdown_report"}]}""");
        var (planner, request) = CreatePlanner(
            runtimeFactory,
            "Build report with apiKey: sk-goal C:\\secrets\\goal.txt SELECT * FROM device_master_cloud_sim_view",
            [
                new AgentPlannerToolSummary(
                    "generate_markdown_report",
                    "Markdown",
                    "token: sk-tool C:\\secrets\\tool.txt SELECT * FROM device_log_cloud_sim_view",
                    "Artifact",
                    "AgentRuntime",
                    "AgentTaskRuntime",
                    """{"type":"object","description":"Password=fake-test-only; table device_master_cloud_sim_view"}""",
                    false,
                    "Low")
            ]);

        var result = await planner.CreatePlanAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var input = runtimeFactory.LastRun!.InputText;
        input.Should().NotContain("sk-goal");
        input.Should().NotContain("sk-tool");
        input.Should().NotContain("C:\\secrets");
        input.Should().NotContain("Password=fake-test-only");
        input.Should().NotContain("SELECT * FROM");
        input.Should().NotContain("device_master_cloud_sim_view");
        input.Should().NotContain("device_log_cloud_sim_view");
    }

    private static (DefaultAgentDynamicPlanner Planner, AgentDynamicPlannerRequest Request) CreatePlanner(
        FakeRuntimeAgentFactory runtimeFactory,
        string goal = "Build report",
        IReadOnlyCollection<AgentPlannerToolSummary>? tools = null)
    {
        var model = new LanguageModel(
            FakeRuntimeAgentFactory.ProviderName,
            "planner",
            "http://localhost/fake",
            "fake-key",
            new ModelParameters { MaxTokens = 4096, MaxOutputTokens = 1024, Temperature = 0.2f },
            FakeRuntimeAgentFactory.ProviderName,
            LanguageModelUsage.Chat | LanguageModelUsage.Planner,
            true);
        var template = new ConversationTemplate(
            "agent_planner",
            "planner",
            "Return backend controlled JSON.",
            model.Id,
            new TemplateSpecification { MaxTokens = 512, Temperature = 0.1f });
        var factory = new ConfiguredAgentRuntimeFactory(
            new InMemoryReadRepository<ConversationTemplate>([template]),
            new InMemoryReadRepository<LanguageModel>([model]),
            runtimeFactory);
        var request = new AgentDynamicPlannerRequest(
            goal,
            AgentTaskType.ReportGeneration,
            [],
            [],
            tools ??
            [
                new AgentPlannerToolSummary(
                    "generate_markdown_report",
                    "Markdown",
                    "Generate Markdown report.",
                    "Artifact",
                    "AgentRuntime",
                    "AgentTaskRuntime",
                    """{"type":"object"}""",
                    false,
                    "Low")
            ],
            model,
            new ChatRuntimeSettingsDto(6, 12, 4, 30, 12000));

        return (new DefaultAgentDynamicPlanner(factory), request);
    }
}
