using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.Services.Contracts;

namespace AICopilot.UnitTests;

public sealed class DataAnalysisReadOnlyGuardrailTests
{
    [Fact]
    public void CloudReadOnlyGovernance_ShouldExposeSchemaOnlyForVerifiedReadOnlySource()
    {
        var database = new BusinessDatabase(
            "CloudPlatformReadonly",
            "Cloud Platform readonly business data",
            "Host=localhost;Database=cloud;Username=readonly;Password=fake-test-only",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: true);
        var profileRegistry = CreateProfileRegistry();

        BusinessDataSourceGovernancePolicy.ResolveGovernanceStatus(database, profileRegistry)
            .Should().Be("GovernedSchemaReady");
        BusinessDataSourceGovernancePolicy.HasExecutableGovernedSchema(database, profileRegistry)
            .Should().BeTrue();
        BusinessDataSourceGovernancePolicy.IsSelectableForMode(database, DataSourceSelectionMode.TextToSql, profileRegistry)
            .Should().BeFalse("Cloud Text-to-SQL is reachable only through the unified plugin-first fallback policy");
        BusinessDataSourceGovernancePolicy.IsSelectableForMode(database, DataSourceSelectionMode.Chat, profileRegistry)
            .Should().BeFalse("Chat never exposes a database-name route that can bypass the typed plugin pipeline");
        BusinessDataSourceGovernancePolicy.IsSelectableForMode(database, DataSourceSelectionMode.Agent, profileRegistry)
            .Should().BeTrue("Agent execution now routes CloudReadOnly through the governed fallback runner and SQL guard");

        var schema = BusinessDataSourceGovernancePolicy.ResolveSafetySchema(database, profileRegistry);
        schema.Should().NotBeNull();
        schema!.AllowedTables.Should().Contain(["devices", "mfg_processes", "device_logs", "hourly_capacity", "pass_station_records"]);
        schema.AllowedTables.Should().BeEquivalentTo(CloudReadOnlyGovernedSchema.AllowedTables);
        schema.AllowedColumnFragments.Should().NotBeNull();
        schema.AllowedColumnFragments!.Should().Contain(["client_code", "process_name", "log_time", "total_count", "completed_time"]);
        schema.SensitiveColumnFragments.Should().NotBeNull();
        schema.SensitiveColumnFragments!.Should().Contain("bootstrap_secret");
    }

    [Fact]
    public void RegisteredNonCloudProfile_ShouldBecomeGovernedWithoutCentralSwitch()
    {
        var database = new BusinessDatabase(
            "mes-line-a",
            "MES readonly source",
            "Host=localhost;Database=mes;Username=readonly;Password=fake-test-only",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.NonCloud,
            readOnlyCredentialVerified: true);
        var profile = new BusinessDataSourceProfile(
            database.Name,
            DataSourceExternalSystemType.NonCloud,
            DatabaseProviderType.PostgreSql,
            IsRealExternalSource: true,
            RequiresExplicitSelection: true,
            SupportsTextToSqlFallback: true,
            new HashSet<BusinessDataCapability> { BusinessDataCapability.ProductionRecord },
            BusinessQuerySecurityProfile.TableOnly(
                new HashSet<string>(["public"], StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(["mes_records"], StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["mes_records"] = new HashSet<string>(["id"], StringComparer.OrdinalIgnoreCase)
                },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        var registry = new BusinessDataSourceProfileRegistry(
            [new FixedProfileProvider(profile)]);

        BusinessDataSourceGovernancePolicy.HasExecutableGovernedSchema(database, registry)
            .Should().BeTrue();
        BusinessDataSourceGovernancePolicy.ResolveSafetySchema(database, registry)!
            .AllowedTables.Should().Contain("mes_records");
    }

    private static IBusinessDataSourceProfileRegistry CreateProfileRegistry()
    {
        return new BusinessDataSourceProfileRegistry(
            [new CloudReadOnlyBusinessDataSourceProfileProvider()]);
    }

    private sealed class FixedProfileProvider(BusinessDataSourceProfile profile)
        : IBusinessDataSourceProfileProvider
    {
        public BusinessDataSourceProfile Profile { get; } = profile;
    }
}
