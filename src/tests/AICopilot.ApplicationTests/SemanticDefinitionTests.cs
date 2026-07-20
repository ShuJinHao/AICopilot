using AICopilot.DataAnalysisService.Semantics;

namespace AICopilot.ApplicationTests;

public sealed class SemanticDefinitionTests
{
    [Fact]
    public void Definitions_ShouldExposeOnlyWhitelistedBusinessFields()
    {
        var catalog = new SemanticDefinitionCatalog();

        var device = catalog.Get(SemanticQueryTarget.Device);
        device.AllowedProjectionFields.Should().BeEquivalentTo(
            ["deviceId", "deviceCode", "deviceName", "processId", "clientCode", "softwareStatus", "runtimeStatus", "runtimeStartedAtUtc", "lastRuntimeHeartbeatAtUtc"]);
        device.AllowedFilterFields.Should().BeEquivalentTo(
            ["deviceId", "deviceCode", "deviceName", "processId", "clientCode"]);
        device.AllowedSortFields.Should().BeEquivalentTo(
            ["deviceCode", "deviceName"]);

        var deviceLog = catalog.Get(SemanticQueryTarget.DeviceLog);
        deviceLog.AllowedProjectionFields.Should().BeEquivalentTo(
            ["logId", "deviceId", "deviceName", "level", "message", "occurredAt", "receivedAt"]);
        deviceLog.AllowedFilterFields.Should().BeEquivalentTo(
            ["deviceId", "deviceCode", "deviceName", "level", "message"]);
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
            ["shiftDate", "outputQty", "qualifiedQty", "totalCount", "okCount", "ngCount", "occurredAt"]);
        capacity.AllowedFilterFields.Should().BeEquivalentTo(
            ["deviceId", "deviceCode", "plcName", "shiftDate"]);
        capacity.AllowedSortFields.Should().BeEquivalentTo(
            ["shiftDate", "occurredAt", "outputQty", "qualifiedQty"]);

        var productionData = catalog.Get(SemanticQueryTarget.ProductionData);
        productionData.AllowedProjectionFields.Should().BeEquivalentTo(
            ["recordId", "typeKey", "typeName", "deviceId", "deviceName", "barcode", "result", "completedAt", "receivedAt", "fields", "fieldSchema"]);
        productionData.AllowedFilterFields.Should().BeEquivalentTo(
            ["typeKey", "processId", "deviceId", "deviceCode", "barcode", "result"]);
        productionData.AllowedSortFields.Should().BeEquivalentTo(
            ["completedAt", "typeKey", "result"]);

        var process = catalog.Get(SemanticQueryTarget.Process);
        process.AllowedProjectionFields.Should().BeEquivalentTo(
            ["processId", "processCode", "processName"]);
        process.AllowedFilterFields.Should().BeEquivalentTo(
            ["processId", "processCode", "processName"]);
        process.AllowedSortFields.Should().BeEmpty();

        var clientRelease = catalog.Get(SemanticQueryTarget.ClientRelease);
        clientRelease.AllowedProjectionFields.Should().BeEquivalentTo(
            ["releaseId", "componentKind", "componentKey", "displayName", "channel", "targetRuntime", "version", "status", "releaseNotes", "createdAtUtc", "publishedAtUtc", "deletedAtUtc"]);
        clientRelease.AllowedFilterFields.Should().BeEquivalentTo(
            ["channel", "targetRuntime", "status", "includeArchived"]);
        clientRelease.AllowedSortFields.Should().BeEmpty();

        CloudAiReadSemanticSchemaRegistry.GetOperationSchemas().Should().HaveCount(8);
        new[] { device, deviceLog, capacity, productionData, process, clientRelease }
            .Should().OnlyContain(definition =>
                definition.MaxLimit == CloudAiReadRowLimitPolicy.MaxRows);
    }
}
