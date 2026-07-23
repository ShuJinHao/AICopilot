namespace AICopilot.Services.Contracts;

public partial interface ICloudAiReadClient
{
    Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);
    Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
        CloudAiReadQuery query,
        CancellationToken cancellationToken = default);
}
