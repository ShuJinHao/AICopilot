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
            .AddJsonFile(GetRepositoryFilePath(@"src\hosts\AICopilot.HttpApi\appsettings.RealSource.template.json"))
            .Build();

        var provider = new ConfiguredSemanticPhysicalMappingProvider(configuration);

        provider.TryGetMapping(SemanticQueryTarget.Device, out var deviceMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.DeviceLog, out var deviceLogMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.Recipe, out var recipeMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.Capacity, out var capacityMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.ProductionData, out var productionMapping).Should().BeTrue();

        deviceMapping.DatabaseName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);
        deviceLogMapping.DatabaseName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);
        recipeMapping.DatabaseName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);
        capacityMapping.DatabaseName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);
        productionMapping.DatabaseName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);

        deviceMapping.SourceName.Should().Be("vw_device_readonly");
        deviceLogMapping.SourceName.Should().Be("vw_device_log_readonly");
        recipeMapping.SourceName.Should().Be("vw_recipe_readonly");
        capacityMapping.SourceName.Should().Be("vw_capacity_readonly");
        productionMapping.SourceName.Should().Be("vw_production_data_readonly");

        foreach (var target in Enum.GetValues<SemanticQueryTarget>())
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
        provider.TryGetMapping(SemanticQueryTarget.Recipe, out var recipeMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.Capacity, out var capacityMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.ProductionData, out var productionMapping).Should().BeTrue();

        deviceMapping.DatabaseName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);
        deviceMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDeviceSourceName);
        deviceLogMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultDeviceLogSourceName);
        recipeMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultRecipeSourceName);
        capacityMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultCapacitySourceName);
        productionMapping.SourceName.Should().Be(ConfiguredSemanticPhysicalMappingProvider.DefaultProductionDataSourceName);

        recipeMapping.IsProjectionFieldAllowed("recipeName").Should().BeTrue();
        recipeMapping.IsFilterFieldAllowed("deviceCode").Should().BeTrue();
        recipeMapping.IsSortFieldAllowed("version").Should().BeTrue();

        capacityMapping.IsProjectionFieldAllowed("outputQty").Should().BeTrue();
        capacityMapping.IsFilterFieldAllowed("processName").Should().BeTrue();
        capacityMapping.IsSortFieldAllowed("occurredAt").Should().BeTrue();

        productionMapping.IsProjectionFieldAllowed("barcode").Should().BeTrue();
        productionMapping.IsFilterFieldAllowed("deviceCode").Should().BeTrue();
        productionMapping.IsSortFieldAllowed("occurredAt").Should().BeTrue();
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
        provider.TryGetMapping(SemanticQueryTarget.Recipe, out var recipeMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.Capacity, out var capacityMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.ProductionData, out var productionMapping).Should().BeTrue();

        deviceMapping.DatabaseName.Should().Be("DeviceDb");
        deviceMapping.SourceName.Should().Be("vw_device_master");
        deviceLogMapping.DatabaseName.Should().Be("SharedSemanticDb");
        deviceLogMapping.SourceName.Should().Be("vw_device_log");
        recipeMapping.SourceName.Should().Be("vw_recipe");
        capacityMapping.SourceName.Should().Be("vw_capacity");
        productionMapping.SourceName.Should().Be("vw_production");
    }

    [Fact]
    public void Provider_ShouldExposeConfigurableRealMappingSettings()
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

        provider.TryGetMapping(SemanticQueryTarget.Recipe, out var recipeMapping).Should().BeTrue();
        recipeMapping.Provider.Should().Be(DatabaseProviderType.SqlServer);
        recipeMapping.FromClause.Should().Be("recipes r INNER JOIN devices d ON d.id = r.device_id");
        recipeMapping.FieldMappings["recipeName"].Should().Be("r.recipe_name");
        recipeMapping.FieldMappings["version"].Should().Be("r.version_no");
        recipeMapping.IsProjectionFieldAllowed("recipeName").Should().BeTrue();
        recipeMapping.IsProjectionFieldAllowed("deviceCode").Should().BeFalse();
        recipeMapping.IsFilterFieldAllowed("recipeName").Should().BeTrue();
        recipeMapping.IsFilterFieldAllowed("deviceCode").Should().BeFalse();
        recipeMapping.IsSortFieldAllowed("version").Should().BeTrue();
        recipeMapping.DefaultSort.Should().NotBeNull();
        recipeMapping.DefaultSort!.Field.Should().Be("version");
        recipeMapping.DefaultSort.Direction.Should().Be(SemanticSortDirection.Desc);
        recipeMapping.DefaultFilters.Should().ContainSingle();
        recipeMapping.DefaultFilters[0].Field.Should().Be("isActive");
        recipeMapping.DefaultFilters[0].Operator.Should().Be(SemanticFilterOperator.Equal);
        recipeMapping.DefaultFilters[0].Value.Should().Be("True");
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
