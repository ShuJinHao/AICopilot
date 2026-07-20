using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Models;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.ApplicationTests;

public sealed class CloudReadonlyAgentPlanServiceTests
{
    [Theory]
    [InlineData("Analysis.Process.List", "{\"filters\":[],\"limit\":20}", "Process", "List")]
    [InlineData("Analysis.Process.Detail", "{\"filters\":[{\"field\":\"processCode\",\"operator\":\"eq\",\"value\":\"CUT\"}]}", "Process", "Detail")]
    [InlineData("Analysis.ClientRelease.List", "{\"filters\":[{\"field\":\"channel\",\"operator\":\"eq\",\"value\":\"stable\"}]}", "ClientRelease", "List")]
    [InlineData("Analysis.Device.Status", "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-001\"}]}", "Device", "Status")]
    public async Task CreateIntentAsync_ShouldExposeConfirmedCloudReadonlyCapabilities(
        string intent,
        string query,
        string expectedTarget,
        string expectedKind)
    {
        var service = CreateService(new IntentResult
        {
            Intent = intent,
            Query = query,
            Confidence = 0.95,
            Reasoning = "test"
        });

        var result = await service.CreateIntentAsync(Guid.NewGuid(), "只读查询");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Intent.Should().Be(intent);
        result.Value.SemanticPlan.Target.ToString().Should().Be(expectedTarget);
        result.Value.SemanticPlan.Kind.ToString().Should().Be(expectedKind);
        result.Value.SemanticPlan.QueryText.Should().BeNull();
        result.Value.SemanticPlanDigest.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void PlanService_ShouldNotReceiveCloudClientOrExecuteDataDuringPlanDraftResolution()
    {
        var constructorParameters = typeof(CloudReadonlyAgentPlanService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameters.Should().NotContain(typeof(ICloudAiReadClient));
        constructorParameters.Should().Contain(typeof(ICloudReadonlyAgentIntentRouter));
        constructorParameters.Should().Contain(typeof(ISemanticQueryPlanner));
    }

    private static CloudReadonlyAgentPlanService CreateService(params IntentResult[] intents)
    {
        var definitions = new SemanticDefinitionCatalog();
        var planner = new SemanticQueryPlanner(new SemanticIntentCatalog(definitions), definitions);
        return new CloudReadonlyAgentPlanService(
            new FixedIntentRouter(intents),
            planner,
            Options.Create(new CloudReadonlyOptions { Mode = CloudReadonlyDataSourceMode.Real }),
            new ThrowingSimulationPlanner());
    }

    private sealed class FixedIntentRouter(IReadOnlyCollection<IntentResult> intents)
        : ICloudReadonlyAgentIntentRouter
    {
        public Task<IReadOnlyCollection<IntentResult>> RouteAsync(
            Guid sessionId,
            string goal,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(intents);
        }
    }

    private sealed class ThrowingSimulationPlanner : ICloudReadonlySimulationIntentPlanner
    {
        public Result<CloudReadonlyAgentPlanIntent> CreateIntent(string goal)
        {
            throw new InvalidOperationException("Real Agent planning must not use the Simulation planner.");
        }
    }
}
