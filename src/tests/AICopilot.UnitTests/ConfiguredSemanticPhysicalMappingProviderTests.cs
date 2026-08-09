using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;

namespace AICopilot.UnitTests;

public sealed class ConfiguredSemanticPhysicalMappingProviderTests
{
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
        deviceLogMapping.IsProjectionFieldAllowed("deviceName").Should().BeTrue();
        deviceLogMapping.IsFilterFieldAllowed("processName").Should().BeTrue();
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
    public void Provider_ShouldRejectDirectCloudReadOnlyMappings_WhenEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataAnalysis:CloudReadOnly:Enabled"] = "true"
            })
            .Build();
        var action = () => new ConfiguredSemanticPhysicalMappingProvider(configuration);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*temporarily closed*");
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

}
