using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Services.Contracts.Data;
using Microsoft.Extensions.Configuration;

namespace AICopilot.ApplicationTests;

public sealed class CloudReadOnlyBusinessDatabaseSeedPolicyTests
{
    private const string TestReadOnlyConnectionString =
        "Host=cloud-postgres.internal.example;Database=cloud;Username=readonly;Password=fake-test-only";

    [Fact]
    public void ResolveOptions_ShouldStayDisabledByDefault()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = CloudReadOnlyBusinessDatabaseSeedPolicy.ResolveOptions(configuration);

        options.Enabled.Should().BeFalse();
        options.DatabaseName.Should().Be(CloudReadOnlyBusinessDatabaseSeedPolicy.DefaultDatabaseName);
        options.ConnectionString.Should().BeNull();
    }

    [Fact]
    public void ValidateOptions_ShouldRequireConnectionString_WhenEnabled()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DataAnalysis:CloudReadOnly:Enabled"] = "true",
            ["DataAnalysis:CloudReadOnly:ReadOnlyCredentialVerified"] = "true"
        });
        var options = CloudReadOnlyBusinessDatabaseSeedPolicy.ResolveOptions(configuration);

        var act = () => CloudReadOnlyBusinessDatabaseSeedPolicy.ValidateOptions(configuration, options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionString is required*");
    }

    [Fact]
    public void ValidateOptions_ShouldRequireVerifiedReadOnlyCredential_WhenEnabled()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DataAnalysis:CloudReadOnly:Enabled"] = "true",
            ["DataAnalysis:CloudReadOnly:ConnectionString"] = TestReadOnlyConnectionString
        });
        var options = CloudReadOnlyBusinessDatabaseSeedPolicy.ResolveOptions(configuration);

        var act = () => CloudReadOnlyBusinessDatabaseSeedPolicy.ValidateOptions(configuration, options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ReadOnlyCredentialVerified must be true*");
    }

    [Fact]
    public void ValidateOptions_ShouldRejectSimulationSeedConflict()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DataAnalysis:CloudReadOnly:Enabled"] = "true",
            ["DataAnalysis:CloudReadOnly:ConnectionString"] = TestReadOnlyConnectionString,
            ["DataAnalysis:CloudReadOnly:ReadOnlyCredentialVerified"] = "true",
            ["CloudReadonly:Mode"] = "Simulation",
            ["CloudReadonly:Simulation:Enabled"] = "true"
        });
        var options = CloudReadOnlyBusinessDatabaseSeedPolicy.ResolveOptions(configuration);

        var act = () => CloudReadOnlyBusinessDatabaseSeedPolicy.ValidateOptions(configuration, options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be enabled while CloudReadonly Simulation seeding is enabled*");
    }

    [Fact]
    public void CreateBusinessDatabase_ShouldCreateVerifiedCloudReadOnlySource()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["DataAnalysis:CloudReadOnly:Enabled"] = "true",
            ["DataAnalysis:CloudReadOnly:ConnectionString"] = TestReadOnlyConnectionString,
            ["DataAnalysis:CloudReadOnly:ReadOnlyCredentialVerified"] = "true",
            ["DataAnalysis:CloudReadOnly:DefaultQueryLimit"] = "100",
            ["DataAnalysis:CloudReadOnly:MaxQueryLimit"] = "500"
        });
        var options = CloudReadOnlyBusinessDatabaseSeedPolicy.ResolveOptions(configuration);

        CloudReadOnlyBusinessDatabaseSeedPolicy.ValidateOptions(configuration, options);
        var database = CloudReadOnlyBusinessDatabaseSeedPolicy.CreateBusinessDatabase(options);

        database.Name.Should().Be(CloudReadOnlyBusinessDatabaseSeedPolicy.DefaultDatabaseName);
        database.Provider.Should().Be(DbProviderType.PostgreSql);
        database.IsEnabled.Should().BeTrue();
        database.IsReadOnly.Should().BeTrue();
        database.ExternalSystemType.Should().Be(BusinessDataExternalSystemType.CloudReadOnly);
        database.ReadOnlyCredentialVerified.Should().BeTrue();
        database.DefaultQueryLimit.Should().Be(100);
        database.MaxQueryLimit.Should().Be(500);
        database.Tags.Should().Contain("direct-db");
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
