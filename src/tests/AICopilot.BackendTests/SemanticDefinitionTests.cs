using AICopilot.DataAnalysisService.Semantics;

namespace AICopilot.BackendTests;

public sealed class SemanticDefinitionTests
{
    [Fact]
    public void Definitions_ShouldExposeOnlyWhitelistedBusinessFields()
    {
        var catalog = new SemanticDefinitionCatalog();

        var device = catalog.Get(SemanticQueryTarget.Device);
        device.AllowedProjectionFields.Should().BeEquivalentTo(
            ["deviceId", "deviceCode", "deviceName", "status", "lineName", "updatedAt"]);
        device.AllowedFilterFields.Should().BeEquivalentTo(
            ["deviceId", "deviceCode", "deviceName", "status", "lineName"]);
        device.AllowedSortFields.Should().BeEquivalentTo(
            ["deviceCode", "deviceName", "updatedAt"]);

        var deviceLog = catalog.Get(SemanticQueryTarget.DeviceLog);
        deviceLog.AllowedProjectionFields.Should().BeEquivalentTo(
            ["logId", "deviceId", "deviceCode", "level", "message", "source", "occurredAt"]);
        deviceLog.AllowedFilterFields.Should().BeEquivalentTo(
            ["deviceId", "deviceCode", "level", "source"]);
        deviceLog.AllowedSortFields.Should().BeEquivalentTo(
            ["occurredAt", "level"]);

        var recipe = catalog.Get(SemanticQueryTarget.Recipe);
        recipe.AllowedProjectionFields.Should().BeEquivalentTo(
            ["recipeId", "recipeName", "deviceId", "deviceCode", "processName", "version", "isActive", "updatedAt"]);
        recipe.AllowedFilterFields.Should().BeEquivalentTo(
            ["recipeId", "recipeName", "deviceId", "deviceCode", "processName", "version", "isActive"]);
        recipe.AllowedSortFields.Should().BeEquivalentTo(
            ["recipeName", "version", "updatedAt", "processName"]);

        var capacity = catalog.Get(SemanticQueryTarget.Capacity);
        capacity.AllowedProjectionFields.Should().BeEquivalentTo(
            ["recordId", "deviceId", "deviceCode", "processName", "shiftDate", "outputQty", "qualifiedQty", "occurredAt"]);
        capacity.AllowedFilterFields.Should().BeEquivalentTo(
            ["recordId", "deviceId", "deviceCode", "processName", "shiftDate"]);
        capacity.AllowedSortFields.Should().BeEquivalentTo(
            ["shiftDate", "occurredAt", "outputQty", "qualifiedQty", "deviceCode", "processName"]);

        var productionData = catalog.Get(SemanticQueryTarget.ProductionData);
        productionData.AllowedProjectionFields.Should().BeEquivalentTo(
            ["recordId", "deviceId", "deviceCode", "processName", "barcode", "stationName", "result", "occurredAt"]);
        productionData.AllowedFilterFields.Should().BeEquivalentTo(
            ["recordId", "deviceId", "deviceCode", "processName", "barcode", "stationName", "result"]);
        productionData.AllowedSortFields.Should().BeEquivalentTo(
            ["occurredAt", "deviceCode", "processName", "stationName", "result"]);
    }
}
