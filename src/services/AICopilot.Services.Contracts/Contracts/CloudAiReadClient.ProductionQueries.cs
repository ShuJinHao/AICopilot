namespace AICopilot.Services.Contracts;

public partial interface ICloudAiReadClient
{
    Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);
    Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);
    Task<CloudAiReadResult<CloudAiReadDevicePlcDto>> GetDevicePlcsAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);
    Task<CloudAiReadResult<CloudAiReadDataSchemaDto>> GetDataSchemasAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);
    Task<SemanticQueryPlan> SealProductionScopeAsync(
        SemanticQueryPlan plan,
        CancellationToken cancellationToken = default);
}
