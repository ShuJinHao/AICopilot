using AICopilot.AiRuntime;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.DataAnalysisService.SimulationBusiness;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseDataGovernanceP0")]
public sealed class EnterpriseDataGovernanceP0Tests
{
    [Fact]
    public void SimulationBusinessDescriptor_ShouldRemainSeparateFromCloudReadonlySimulation()
    {
        var database = new BusinessDatabase(
            "aicopilot_sim_business",
            "AI independent simulation business database",
            "Host=localhost;Database=aicopilot_sim_business;Username=readonly;Password=readonly;",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.SimulationBusiness,
            readOnlyCredentialVerified: false,
            isEnabled: true,
            category: "Simulation",
            tags: ["production", "quality"],
            ownerDepartment: "AI Platform",
            businessDomain: "生产设备",
            sensitivityLevel: "Internal",
            defaultQueryLimit: 100,
            maxQueryLimit: 500,
            isSelectableInChat: true,
            isSelectableInAgent: true);

        var descriptor = BusinessDatabaseContractMapper.ToDescriptor(database);

        descriptor.ExternalSystemType.Should().Be(DataSourceExternalSystemType.SimulationBusiness);
        descriptor.ExternalSystemType.Should().NotBe(DataSourceExternalSystemType.CloudReadOnly);
        descriptor.Category.Should().Be("Simulation");
        descriptor.Tags.Should().BeEquivalentTo("production", "quality");
        descriptor.BusinessDomain.Should().Be("生产设备");
        descriptor.DefaultQueryLimit.Should().Be(100);
        descriptor.MaxQueryLimit.Should().Be(500);
    }

    [Fact]
    public void BusinessQueryResult_ShouldForceSimulationMarkersAndHashOnly()
    {
        var database = new BusinessDatabase(
            "aicopilot_sim_business",
            "AI independent simulation business database",
            "Host=localhost;Database=aicopilot_sim_business;Username=readonly;Password=readonly;",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.SimulationBusiness);
        const string sql = "SELECT employee_no FROM employees WHERE employee_name = 'secret employee'";
        var result = new DatabaseQueryResult(
            [new Dictionary<string, object?> { ["employee_no"] = "E0001" }],
            ReturnedRowCount: 1,
            IsTruncated: false,
            ElapsedMilliseconds: 7);

        var dto = BusinessQueryResultMapper.Map(
            database,
            sql,
            result,
            SimulationBusinessQuerySchema.SafetySchema,
            DataSourceSelectionMode.TextToSql);

        dto.SourceType.Should().Be("BusinessDatabase");
        dto.SourceMode.Should().Be(DataSourceExternalSystemType.SimulationBusiness);
        dto.IsSimulation.Should().BeTrue();
        dto.SourceLabel.Should().Be("AI 独立模拟业务库");
        dto.QueryHash.Should().HaveLength(64);
        dto.QueryHash.Should().NotContain("secret employee");
        dto.RowCount.Should().Be(1);
        dto.Governance.Should().NotBeNull();
        dto.Governance!.IsSanitizedPreview.Should().BeTrue();
    }

    [Theory]
    [InlineData("SELECT password FROM employees")]
    [InlineData("SELECT * FROM information_schema.tables")]
    [InlineData("SELECT token_value FROM api_tokens")]
    public void BusinessReadonlySafetyPolicy_ShouldBlockSensitiveFieldsAndSystemCatalogs(string sql)
    {
        BusinessReadonlyQuerySafetyPolicy.Validate(sql).Should().NotBeNull();
    }

    [Fact]
    public void SimulationBusinessSeedGenerator_ShouldCreateMediumFiveDomainPlan()
    {
        var plan = new SimulationBusinessSeedGenerator().CreatePlan();

        plan.DatabaseName.Should().Be("aicopilot_sim_business");
        plan.Profile.Should().Be(SimulationBusinessProfile.Medium);
        plan.TableCounts.Should().Contain(item => item.TableName == "employees" && item.RowCount == 300);
        plan.TableCounts.Should().Contain(item => item.TableName == "attendance" && item.RowCount == 18000);
        plan.TableCounts.Should().Contain(item => item.TableName == "production_records" && item.RowCount == 30000);
        plan.TableCounts.Should().Contain(item => item.TableName == "device_events" && item.RowCount == 50000);
        plan.TableCounts.Should().Contain(item => item.TableName == "quality_inspections" && item.RowCount == 20000);
        plan.TableCounts.Should().Contain(item => item.TableName == "inventory_movements" && item.RowCount == 30000);
        plan.TableCounts.Should().Contain(item => item.TableName == "sales_orders" && item.RowCount == 3000);
        plan.TableCounts.Select(item => item.BusinessDomain).Distinct().Should().HaveCountGreaterThanOrEqualTo(5);
        plan.SqlScript.Should().Contain("CREATE TABLE IF NOT EXISTS employees");
    }

    [Fact]
    public void RagDocumentLifecycle_ShouldExcludeSupersededAndSoftDeletedDocuments()
    {
        var kb = new KnowledgeBase("制度库", "制度测试", EmbeddingModelId.New());
        var document = kb.AddDocument(
            new DocumentId(1),
            "婚假制度.md",
            "/safe/marriage.md",
            ".md",
            new string('a', 64));

        document.MarkAsIndexed();
        document.IsSearchable(DateTime.UtcNow).Should().BeTrue();

        document.Activate();
        document.Status.Should().Be(DocumentStatus.Active);
        document.IsSearchable(DateTime.UtcNow).Should().BeTrue();

        document.SoftDelete();
        document.Status.Should().Be(DocumentStatus.SoftDeleted);
        document.IsSearchable(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void KnowledgeSupplement_CriticalOverride_ShouldApplyWithinEffectiveWindow()
    {
        var supplement = new KnowledgeSupplement(
            "婚假补充说明",
            "婚假以最新制度补充说明为准。",
            KnowledgeSupplementPriority.CriticalOverride,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(1));

        supplement.Priority.Should().Be(KnowledgeSupplementPriority.CriticalOverride);
        supplement.CanApply(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void ModelEndpointPoolScheduler_ShouldRespectLeastInFlightAndCircuitBreaker()
    {
        var scheduler = new InMemoryModelEndpointPoolScheduler(
            Options.Create(new ModelProviderReliabilityOptions
            {
                CircuitBreakerFailureThreshold = 2,
                CircuitBreakerOpenSeconds = 60,
                EndpointPools =
                {
                    ["AnswerPool"] = new ModelEndpointPoolOptions
                    {
                        Usage = "AnswerPool",
                        Strategy = "LeastInFlight",
                        Endpoints =
                        [
                            new ModelEndpointOptions
                            {
                                EndpointId = "answer-a",
                                Provider = "OpenAI",
                                BaseUrl = "https://example.invalid/a",
                                ApiKey = "encv2:protected-a",
                                ConcurrencyLimit = 2,
                                Weight = 2,
                                Priority = 1
                            },
                            new ModelEndpointOptions
                            {
                                EndpointId = "answer-b",
                                Provider = "OpenAI",
                                BaseUrl = "https://example.invalid/b",
                                ApiKey = "encv2:protected-b",
                                ConcurrencyLimit = 2,
                                Weight = 1,
                                Priority = 1
                            }
                        ]
                    }
                }
            }),
            new EndpointPoolSecretProtector());

        var first = scheduler.SelectEndpoint("AnswerPool");
        scheduler.RecordStarted(first.EndpointId);
        var second = scheduler.SelectEndpoint("AnswerPool");

        second.EndpointId.Should().NotBe(first.EndpointId);

        scheduler.RecordFailed(first.EndpointId, TimeSpan.FromMilliseconds(10), new InvalidOperationException("rate limit"));
        scheduler.RecordFailed(first.EndpointId, TimeSpan.FromMilliseconds(10), new InvalidOperationException("rate limit"));

        var snapshot = scheduler.GetSnapshot();
        snapshot.Pools.Should().ContainSingle(pool => pool.PoolName == "AnswerPool");
        snapshot.Pools.Single().Endpoints.Single(endpoint => endpoint.EndpointId == first.EndpointId)
            .IsCircuitOpen.Should().BeTrue();
        snapshot.Pools.Single().Endpoints.Should().OnlyContain(endpoint => !endpoint.HasApiKey || endpoint.HasApiKey);
    }
}
