using AICopilot.AiRuntime;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.DataAnalysisService.BusinessDatabases;
using Microsoft.Extensions.Options;

namespace AICopilot.ApplicationTests;

public sealed class EnterpriseDataGovernanceP1Tests
{
    [Theory]
    [InlineData("capacity report by production line", "production_records")]
    [InlineData("quality defect rate by product", "quality_inspections")]
    [InlineData("device downtime and alarm summary", "device_events")]
    [InlineData("inventory turnover by material", "inventory_movements")]
    [InlineData("sales delivery backlog by customer", "sales_orders")]
    [InlineData("employee leave and attendance overview", "employees")]
    public void BusinessTextToSqlGenerator_ShouldCreateGuardedSimulationQueries(
        string question,
        string expectedTable)
    {
        var generated = BusinessTextToSqlRuleBasedGenerator.Generate(question, limit: 25);

        generated.Sql.Should().StartWith("SELECT");
        generated.Sql.Should().Contain(expectedTable);
        generated.Sql.Should().Contain("LIMIT 25");
        generated.Explanation.Should().NotBeNullOrWhiteSpace();
        BusinessReadonlyQuerySafetyPolicy
            .Validate(generated.Sql, SimulationBusinessQuerySchema.SafetySchema)
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData("SELECT password_hash FROM employees")]
    [InlineData("SELECT * FROM pg_catalog.pg_user")]
    [InlineData("SELECT * FROM employees; SELECT * FROM sales_orders")]
    [InlineData("SELECT * FROM unknown_real_business_table")]
    [InlineData("DELETE FROM employees")]
    public void BusinessTextToSqlGuardrail_ShouldRejectDangerousOrUnauthorizedSql(string sql)
    {
        BusinessReadonlyQuerySafetyPolicy
            .Validate(sql, SimulationBusinessQuerySchema.SafetySchema)
            .Should()
            .NotBeNull();
    }

    [Fact]
    public void KnowledgeSupplement_ShouldOnlyApplyWhenEnabledAndWithinEffectiveWindow()
    {
        var now = DateTime.UtcNow;
        var supplement = new KnowledgeSupplement(
            "Policy override",
            "Use the newest policy supplement first.",
            KnowledgeSupplementPriority.CriticalOverride,
            effectiveAt: now.AddMinutes(-5),
            expiredAt: now.AddMinutes(5));

        supplement.CanApply(now).Should().BeTrue();

        supplement.Update(
            supplement.Title,
            supplement.Content,
            KnowledgeSupplementPriority.CriticalOverride,
            now.AddMinutes(-10),
            now.AddMinutes(-1),
            categoryId: null,
            documentId: null,
            isEnabled: true);

        supplement.CanApply(now).Should().BeFalse();

        supplement.Update(
            supplement.Title,
            supplement.Content,
            KnowledgeSupplementPriority.CriticalOverride,
            now.AddMinutes(-10),
            now.AddMinutes(10),
            categoryId: null,
            documentId: null,
            isEnabled: false);

        supplement.CanApply(now).Should().BeFalse();
    }

    [Fact]
    public void ModelEndpointPoolScheduler_ShouldHandleMockLoadAndCircuitStatistics()
    {
        var scheduler = new InMemoryModelEndpointPoolScheduler(
            Options.Create(new ModelProviderReliabilityOptions
            {
                CircuitBreakerFailureThreshold = 2,
                CircuitBreakerOpenSeconds = 60,
                EndpointPools =
                {
                    ["TextToSqlPool"] = new ModelEndpointPoolOptions
                    {
                        Usage = "TextToSqlPool",
                        Strategy = "LeastInFlight",
                        Endpoints =
                        [
                            CreateEndpoint("mock-a"),
                            CreateEndpoint("mock-b"),
                            CreateEndpoint("mock-c")
                        ]
                    }
                }
            }),
            new EndpointPoolSecretProtector());

        var selected = new List<ModelEndpointSelection>();
        for (var i = 0; i < 6; i++)
        {
            var endpoint = scheduler.SelectEndpoint("TextToSqlPool");
            selected.Add(endpoint);
            scheduler.RecordStarted(endpoint.EndpointId);
        }

        selected
            .GroupBy(item => item.EndpointId)
            .Select(group => group.Count())
            .Should()
            .OnlyContain(count => count == 2);

        Action selectOverflow = () => scheduler.SelectEndpoint("TextToSqlPool");
        selectOverflow.Should().Throw<InvalidOperationException>()
            .WithMessage("*no healthy endpoint with available concurrency*");

        scheduler.RecordRateLimited("mock-a");
        scheduler.RecordFailed("mock-a", TimeSpan.FromMilliseconds(120), new TimeoutException());
        scheduler.RecordFailed("mock-a", TimeSpan.FromMilliseconds(130), new TimeoutException());
        scheduler.RecordStickyStreaming("mock-b");
        scheduler.RecordSucceeded("mock-b", TimeSpan.FromMilliseconds(80));

        var snapshot = scheduler.GetSnapshot();
        var pool = snapshot.Pools.Should().ContainSingle(item => item.PoolName == "TextToSqlPool").Subject;

        pool.Endpoints.Should().OnlyContain(endpoint => endpoint.QueueLimit == 6);
        pool.Endpoints.Single(endpoint => endpoint.EndpointId == "mock-a").IsCircuitOpen.Should().BeTrue();
        pool.Endpoints.Single(endpoint => endpoint.EndpointId == "mock-a").Stats.CircuitState.Should().Be("Open");
        pool.Endpoints.Single(endpoint => endpoint.EndpointId == "mock-a").Stats.LastFailureReason.Should().Be(nameof(TimeoutException));
        pool.Endpoints.Single(endpoint => endpoint.EndpointId == "mock-b").Stats.StickyStreamingCount.Should().Be(1);
    }

    [Fact]
    public void ModelEndpointPoolScheduler_ShouldSupportWeightedRoundRobinWithoutPlaintextKeyExposure()
    {
        var scheduler = new InMemoryModelEndpointPoolScheduler(
            Options.Create(new ModelProviderReliabilityOptions
            {
                EndpointPools =
                {
                    ["AnswerPool"] = new ModelEndpointPoolOptions
                    {
                        Usage = "AnswerPool",
                        Strategy = "WeightedRoundRobin",
                        Endpoints =
                        [
                            CreateEndpoint("answer-a", weight: 2),
                            CreateEndpoint("answer-b", weight: 1)
                        ]
                    }
                }
            }),
            new EndpointPoolSecretProtector());

        var selections = Enumerable.Range(0, 3)
            .Select(_ => scheduler.SelectEndpoint("AnswerPool").EndpointId)
            .ToArray();

        selections.Count(endpointId => endpointId == "answer-a").Should().Be(2);
        selections.Count(endpointId => endpointId == "answer-b").Should().Be(1);

        var snapshot = scheduler.GetSnapshot();
        snapshot.Pools.Single().Endpoints.Should().OnlyContain(endpoint => endpoint.HasApiKey);
        snapshot.Pools.Single().Endpoints.Select(endpoint => endpoint.BaseUrl)
            .Should()
            .OnlyContain(baseUrl => !baseUrl.Contains("protected-test-key", StringComparison.OrdinalIgnoreCase));
    }

    private static ModelEndpointOptions CreateEndpoint(string endpointId, int weight = 1)
    {
        return new ModelEndpointOptions
        {
            EndpointId = endpointId,
            Provider = "Mock",
            BaseUrl = $"https://mock.invalid/{endpointId}",
            ApiKey = "encv2:protected-test-key",
            ConcurrencyLimit = 2,
            QueueLimit = 6,
            TimeoutMs = 5000,
            Weight = weight,
            Priority = 1
        };
    }
}
