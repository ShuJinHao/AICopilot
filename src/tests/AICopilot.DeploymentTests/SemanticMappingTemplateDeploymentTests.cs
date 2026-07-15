using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;

namespace AICopilot.DeploymentTests;

public sealed class SemanticMappingTemplateDeploymentTests
{
    [Fact]
    public void RealSourceTemplate_ShouldDeclareCompleteReadonlyMappings()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(
                RepositoryTestSupport.Root,
                "src",
                "hosts",
                "AICopilot.HttpApi",
                "appsettings.RealSource.template.json"))
            .Build();

        var provider = new ConfiguredSemanticPhysicalMappingProvider(configuration);

        provider.TryGetMapping(SemanticQueryTarget.Device, out var deviceMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.DeviceLog, out var deviceLogMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.Recipe, out _).Should().BeFalse();
        provider.TryGetMapping(SemanticQueryTarget.Capacity, out var capacityMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.ProductionData, out var productionMapping).Should().BeTrue();
        provider.TryGetMapping(SemanticQueryTarget.Process, out _).Should().BeFalse();
        provider.TryGetMapping(SemanticQueryTarget.ClientRelease, out _).Should().BeFalse();

        deviceMapping.SourceName.Should().Be("devices");
        deviceLogMapping.SourceName.Should().Be("device_logs");
        capacityMapping.SourceName.Should().Be("hourly_capacity");
        productionMapping.SourceName.Should().Be("pass_station_records");

        foreach (var target in new[]
                 {
                     SemanticQueryTarget.Device,
                     SemanticQueryTarget.DeviceLog,
                     SemanticQueryTarget.Capacity,
                     SemanticQueryTarget.ProductionData
                 })
        {
            provider.TryGetMapping(target, out var mapping).Should().BeTrue();
            mapping.DatabaseName.Should().NotBeNullOrWhiteSpace();
            mapping.SourceName.Should().NotBeNullOrWhiteSpace();
            mapping.FieldMappings.Keys.Should().Contain(
                SemanticSourceContractCatalog.GetRequiredFields(target));
        }
    }
}
