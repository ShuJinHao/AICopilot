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
            RoutingNote = "test"
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

    [Fact]
    public void CreateIntentsFromRouted_ShouldFreezeEveryDistinctSupportedCloudIntent()
    {
        var routed = new[]
        {
            new IntentResult
            {
                Intent = "Analysis.Device.Status",
                Query = "{\"filters\":[{\"field\":\"deviceCode\",\"operator\":\"eq\",\"value\":\"DEV-01\"}]}",
                Confidence = 0.95
            },
            new IntentResult
            {
                Intent = "Analysis.Capacity.ByDevice",
                Query = "{\"filters\":[{\"field\":\"deviceId\",\"operator\":\"eq\",\"value\":\"22222222-2222-4222-8222-222222222222\"}]}",
                Confidence = 0.9
            }
        };
        var service = CreateService(routed);

        var result = service.CreateIntentsFromRouted("只读查看设备状态和产能", routed);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(intent => intent.Intent).Should().Equal(
            "Analysis.Capacity.ByDevice",
            "Analysis.Device.Status");
        result.Value.Should().OnlyContain(intent =>
            intent.SemanticPlanDigest.Length == 64 && intent.SemanticPlanDigest.All(character =>
                character >= '0' && character <= '9' || character >= 'a' && character <= 'f'));
    }

    private static CloudReadonlyAgentPlanService CreateService(params IntentResult[] intents)
    {
        var definitions = new SemanticDefinitionCatalog();
        var planner = new SemanticQueryPlanner(new SemanticQuerySchemaRegistry(definitions), definitions);
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
