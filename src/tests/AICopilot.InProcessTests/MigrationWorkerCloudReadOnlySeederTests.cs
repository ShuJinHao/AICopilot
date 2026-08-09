using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.MigrationWorkApp;
using Microsoft.Extensions.Configuration;

namespace AICopilot.InProcessTests;

public sealed class MigrationWorkerCloudReadOnlySeederTests
{
    private const string TestReadOnlyConnectionString =
        "Host=cloud-postgres.internal.example;Database=cloud;Username=readonly;Password=fake-test-only";

    [Fact]
    public void ResolveOptions_ShouldStayDisabledByDefault()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = MigrationWorkerCloudReadOnlySeeder.ResolveOptions(configuration);

        options.Enabled.Should().BeFalse();
        options.DatabaseName.Should().Be(MigrationWorkerCloudReadOnlySeeder.DefaultDatabaseName);
        options.ConnectionString.Should().BeNull();
    }

    [Fact]
    public void ValidateOptions_ShouldRejectEnabledModeBeforeConnectionValidation()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DataAnalysis:CloudReadOnly:Enabled"] = "true",
            ["DataAnalysis:CloudReadOnly:ReadOnlyCredentialVerified"] = "true"
        });
        var options = MigrationWorkerCloudReadOnlySeeder.ResolveOptions(configuration);

        var act = () => MigrationWorkerCloudReadOnlySeeder.ValidateOptions(configuration, options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*temporarily closed*");
    }

    [Fact]
    public void ValidateOptions_ShouldRejectEnabledModeBeforeCredentialValidation()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DataAnalysis:CloudReadOnly:Enabled"] = "true",
            ["DataAnalysis:CloudReadOnly:ConnectionString"] = TestReadOnlyConnectionString
        });
        var options = MigrationWorkerCloudReadOnlySeeder.ResolveOptions(configuration);

        var act = () => MigrationWorkerCloudReadOnlySeeder.ValidateOptions(configuration, options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*temporarily closed*");
    }

    [Fact]
    public void ValidateOptions_ShouldRejectEnabledModeEvenWhenSimulationIsConfigured()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DataAnalysis:CloudReadOnly:Enabled"] = "true",
            ["DataAnalysis:CloudReadOnly:ConnectionString"] = TestReadOnlyConnectionString,
            ["DataAnalysis:CloudReadOnly:ReadOnlyCredentialVerified"] = "true",
            ["CloudReadonly:Mode"] = "Simulation",
            ["CloudReadonly:Simulation:Enabled"] = "true"
        });
        var options = MigrationWorkerCloudReadOnlySeeder.ResolveOptions(configuration);

        var act = () => MigrationWorkerCloudReadOnlySeeder.ValidateOptions(configuration, options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*temporarily closed*");
    }

    [Fact]
    public void FullyConfiguredRealCloudSource_ShouldStillFailBeforeRegistration()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DataAnalysis:CloudReadOnly:Enabled"] = "true",
            ["DataAnalysis:CloudReadOnly:ConnectionString"] = TestReadOnlyConnectionString,
            ["DataAnalysis:CloudReadOnly:ReadOnlyCredentialVerified"] = "true",
            ["DataAnalysis:CloudReadOnly:DefaultQueryLimit"] = "100",
            ["DataAnalysis:CloudReadOnly:MaxQueryLimit"] = "500"
        });
        var options = MigrationWorkerCloudReadOnlySeeder.ResolveOptions(configuration);

        var act = () => MigrationWorkerCloudReadOnlySeeder.ValidateOptions(
            configuration,
            options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*temporarily closed*");
    }

    [Fact]
    public void RetireCloudSource_ShouldClearConnectionAndEveryRuntimeSelectionFlag()
    {
        var database = new BusinessDatabase(
            "legacy-cloud",
            "legacy Cloud source",
            TestReadOnlyConnectionString,
            DbProviderType.PostgreSql,
            isReadOnly: true,
            externalSystemType: BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: true,
            isEnabled: true,
            isSelectableInChat: true,
            isSelectableInAgent: true);

        database.RetireAndClearConnectionMaterial();

        database.ConnectionString.Should().BeEmpty();
        database.IsEnabled.Should().BeFalse();
        database.ReadOnlyCredentialVerified.Should().BeFalse();
        database.IsSelectableInChat.Should().BeFalse();
        database.IsSelectableInAgent.Should().BeFalse();
        database.ExternalSystemType.Should().Be(BusinessDataExternalSystemType.CloudReadOnly);
        database.IsReadOnly.Should().BeTrue();
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
