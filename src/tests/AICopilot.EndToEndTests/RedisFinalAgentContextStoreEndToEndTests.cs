using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Infrastructure.AiGateway;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.EndToEndTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class RedisFinalAgentContextStoreEndToEndTests(
    CoreAICopilotAppFixture fixture)
{
    [Fact]
    public async Task SeparateStores_ShouldShareAndRemoveFinalAgentContext()
    {
        var redisConnectionString = await fixture.GetConnectionStringAsync(
            "final-agent-context-redis");
        using var serviceProviderA = CreateRedisServiceProvider(redisConnectionString);
        using var serviceProviderB = CreateRedisServiceProvider(redisConnectionString);

        var storeA = new RedisFinalAgentContextStore(
            serviceProviderA.GetRequiredService<IDistributedCache>());
        var storeB = new RedisFinalAgentContextStore(
            serviceProviderB.GetRequiredService<IDistributedCache>());

        var sessionId = Guid.NewGuid();
        var storedContext = new StoredFinalAgentContext(
            sessionId,
            "prepare diagnostic checklist for device DEV-001",
            128,
            64,
            new ChatTokenTelemetryContext(
                sessionId,
                "fake-model",
                "fake-template",
                4096,
                512),
            512,
            0.3f,
            ["GenerateDiagnosticChecklist"],
            """{"threadId":"redis-context-test"}""",
            [
                new StoredToolApprovalRequest(
                    "request-1",
                    "call-1",
                    "Function",
                    "GenerateDiagnosticChecklist",
                    null,
                    new Dictionary<string, object?>
                    {
                        ["deviceCode"] = "DEV-001"
                    })
            ]);

        await storeA.SetAsync(sessionId, storedContext);

        var restoredContext = await storeB.GetAsync(sessionId);
        restoredContext.Should().NotBeNull();
        restoredContext!.SessionId.Should().Be(sessionId);
        restoredContext.InputText.Should().Be(storedContext.InputText);
        restoredContext.ToolNames.Should().Equal(storedContext.ToolNames);
        restoredContext.PendingApprovals.Should().ContainSingle();
        restoredContext.PendingApprovals[0].CallId.Should().Be("call-1");
        restoredContext.PendingApprovals[0].ToolName.Should().Be(
            "GenerateDiagnosticChecklist");
        restoredContext.PendingApprovals[0].Arguments.Should().ContainKey("deviceCode");
        restoredContext.PendingApprovals[0].Arguments["deviceCode"]?.ToString()
            .Should()
            .Be("DEV-001");

        await storeB.RemoveAsync(sessionId);

        (await storeA.GetAsync(sessionId)).Should().BeNull();
    }

    private static ServiceProvider CreateRedisServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
            options.InstanceName = "tests:";
        });

        return services.BuildServiceProvider();
    }
}
