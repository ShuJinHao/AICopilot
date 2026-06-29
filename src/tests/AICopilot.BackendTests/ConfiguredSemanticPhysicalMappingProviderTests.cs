using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;

namespace AICopilot.BackendTests;

public sealed class ConfiguredSemanticPhysicalMappingProviderTests
{
    [Fact]
    public void Provider_ShouldLoadExplicitRealSourceTemplateMappings()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(GetRepositoryFilePath(Path.Combine(
                "src",
                "hosts",
                "AICopilot.HttpApi",
                "appsettings.RealSource.template.json")))
            .Build();

        var provider = new ConfiguredSemanticPhysicalMappingProvider(configuration);

        provider.TryGetMapping(SemanticQueryTarget.Device, out var deviceMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.DeviceLog, out var deviceLogMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.Recipe, out _).Should().BeFalse();
        provider.TryGetMapping(SemanticQueryTarget.Capacity, out var capacityMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.ProductionData, out var productionMapping).Should().BeTrue();

        deviceMapping.DatabaseName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);
        deviceLogMapping.DatabaseName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);
        capacityMapping.DatabaseName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);
        productionMapping.DatabaseName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);

        deviceMapping.SourceName.Should().Be("devices");
        deviceMapping.FromClause.Should().Contain("devices d LEFT JOIN mfg_processes mp");
        deviceLogMapping.SourceName.Should().Be("device_logs");
        deviceLogMapping.FromClause.Should().Be("device_logs l INNER JOIN devices d ON l.device_id = d.id LEFT JOIN mfg_processes mp ON d.process_id = mp.id");
        deviceLogMapping.FieldMappings["source"].Should().Be("'Cloud'");
        capacityMapping.SourceName.Should().Be("hourly_capacity");
        capacityMapping.FromClause.Should().Be("hourly_capacity h INNER JOIN devices d ON h.device_id = d.id LEFT JOIN mfg_processes mp ON d.process_id = mp.id");
        productionMapping.SourceName.Should().Be("pass_station_records");
        productionMapping.FromClause.Should().Be("pass_station_records p INNER JOIN devices d ON p.device_id = d.id LEFT JOIN mfg_processes mp ON d.process_id = mp.id");

        foreach (var target in Enum.GetValues<SemanticQueryTarget>().Where(target => target != SemanticQueryTarget.Recipe))
        {
            provider.TryGetMapping(target, out var mapping).Should().BeTrue();
            mapping.DatabaseName.Should().NotBeNullOrWhiteSpace();
            mapping.SourceName.Should().NotBeNullOrWhiteSpace();

            foreach (var field in SemanticSourceContractCatalog.GetRequiredFields(target))
            {
                mapping.FieldMappings.Keys.Should().Contain(
                    field,
                    $"the real source template must declare the full readonly contract for {target}");
            }
        }
    }

    [Fact]
    public void Provider_ShouldExposeDefaultBusinessMappings()
    {
        var configuration = new ConfigurationBuilder().Build();
        var provider = new ConfiguredSemanticPhysicalMappingProvider(configuration);

        provider.TryGetMapping(SemanticQueryTarget.Device, out var deviceMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.DeviceLog, out var deviceLogMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.Recipe, out _).Should().BeFalse();
        provider.TryGetMapping(SemanticQueryTarget.Capacity, out var capacityMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.ProductionData, out var productionMapping).Should().BeTrue();

        deviceMapping.DatabaseName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);
        deviceMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDeviceSourceName);
        deviceLogMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDeviceLogSourceName);
        capacityMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultCapacitySourceName);
        productionMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultProductionDataSourceName);

        capacityMapping.IsProjectionFieldAllowed("outputQty").Should().BeTrue();
        capacityMapping.IsFilterFieldAllowed("processName").Should().BeTrue();
        capacityMapping.IsSortFieldAllowed("occurredAt").Should().BeTrue();

        productionMapping.IsProjectionFieldAllowed("barcode").Should().BeTrue();
        productionMapping.IsFilterFieldAllowed("deviceCode").Should().BeTrue();
        productionMapping.IsSortFieldAllowed("occurredAt").Should().BeTrue();
    }

    [Fact]
    public void Provider_ShouldExposeDirectCloudReadOnlyMappings_WhenEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataAnalysis:CloudReadOnly:Enabled"] = "true"
            })
            .Build();
        var provider = new ConfiguredSemanticPhysicalMappingProvider(configuration);

        provider.TryGetMapping(SemanticQueryTarget.Device, out var deviceMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.DeviceLog, out var deviceLogMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.Capacity, out var capacityMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.ProductionData, out var productionMapping).Should().BeTrue();

        deviceMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.RealDeviceSourceName);
        deviceMapping.FromClause.Should().Contain("LEFT JOIN LATERAL");
        deviceMapping.FieldMappings["deviceCode"].Should().Be("d.client_code");
        deviceMapping.FieldMappings["lineName"].Should().Be("mp.process_name");
        deviceLogMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.RealDeviceLogSourceName);
        deviceLogMapping.FieldMappings["source"].Should().Be("'Cloud'");
        deviceLogMapping.IsFilterFieldAllowed("source").Should().BeFalse();
        capacityMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.RealCapacitySourceName);
        capacityMapping.FromClause.Should().Contain("LEFT JOIN mfg_processes mp");
        capacityMapping.FieldMappings["processName"].Should().Be("mp.process_name");
        capacityMapping.FieldMappings["outputQty"].Should().Be("h.total_count");
        productionMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.RealProductionDataSourceName);
        productionMapping.FromClause.Should().Contain("LEFT JOIN mfg_processes mp");
        productionMapping.FieldMappings["processName"].Should().Be("mp.process_name");
        productionMapping.FieldMappings["stationName"].Should().Be("p.type_key");
    }

    [Fact]
    public void Provider_ShouldAllowConfigurationOverrides()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SemanticMappings:DatabaseName"] = "SharedSemanticDb",
                ["SemanticMappings:Device:DatabaseName"] = "DeviceDb",
                ["SemanticMappings:Device:SourceName"] = "vw_device_master",
                ["SemanticMappings:DeviceLog:SourceName"] = "vw_device_log",
                ["SemanticMappings:Recipe:SourceName"] = "vw_recipe",
                ["SemanticMappings:Capacity:SourceName"] = "vw_capacity",
                ["SemanticMappings:ProductionData:SourceName"] = "vw_production"
            })
            .Build();

        var provider = new ConfiguredSemanticPhysicalMappingProvider(configuration);

        provider.TryGetMapping(SemanticQueryTarget.Device, out var deviceMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.DeviceLog, out var deviceLogMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.Recipe, out _).Should().BeFalse();
        provider.TryGetMapping(SemanticQueryTarget.Capacity, out var capacityMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.ProductionData, out var productionMapping).Should().BeTrue();

        deviceMapping.DatabaseName.Should().Be("DeviceDb");
        deviceMapping.SourceName.Should().Be("vw_device_master");
        deviceLogMapping.DatabaseName.Should().Be("SharedSemanticDb");
        deviceLogMapping.SourceName.Should().Be("vw_device_log");
        capacityMapping.SourceName.Should().Be("vw_capacity");
        productionMapping.SourceName.Should().Be("vw_production");
    }

    [Fact]
    public void Provider_ShouldIgnoreRecipeMappingConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SemanticMappings:Recipe:Provider"] = "SqlServer",
                ["SemanticMappings:Recipe:FromClause"] = "recipes r INNER JOIN devices d ON d.id = r.device_id",
                ["SemanticMappings:Recipe:FieldMappings:recipeName"] = "r.recipe_name",
                ["SemanticMappings:Recipe:FieldMappings:deviceCode"] = "d.client_code",
                ["SemanticMappings:Recipe:FieldMappings:version"] = "r.version_no",
                ["SemanticMappings:Recipe:AllowedProjectionFields:0"] = "recipeName",
                ["SemanticMappings:Recipe:AllowedProjectionFields:1"] = "version",
                ["SemanticMappings:Recipe:AllowedFilterFields:0"] = "recipeName",
                ["SemanticMappings:Recipe:AllowedSortFields:0"] = "version",
                ["SemanticMappings:Recipe:DefaultSort:Field"] = "version",
                ["SemanticMappings:Recipe:DefaultSort:Direction"] = "Desc",
                ["SemanticMappings:Recipe:DefaultFilters:0:Field"] = "isActive",
                ["SemanticMappings:Recipe:DefaultFilters:0:Operator"] = "Equal",
                ["SemanticMappings:Recipe:DefaultFilters:0:Value"] = "True"
            })
            .Build();

        var provider = new ConfiguredSemanticPhysicalMappingProvider(configuration);

        provider.TryGetMapping(SemanticQueryTarget.Recipe, out _).Should().BeFalse();
    }

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.");
    }
}
