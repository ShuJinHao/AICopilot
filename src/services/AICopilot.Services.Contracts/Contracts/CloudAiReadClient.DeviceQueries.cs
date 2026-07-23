namespace AICopilot.Services.Contracts;

public partial interface ICloudAiReadClient
{
    Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);
    Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);
}
