using AICopilot.Services.Contracts;

namespace AICopilot.Infrastructure.CloudRead;

public sealed class CloudDelegationAccessTokenProvider(
    ICurrentUser currentUser,
    ICloudDelegationGrantStore grantStore) : ICloudDelegationAccessTokenProvider
{
    public async Task<string> GetCurrentTokenAsync(
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated ||
            !currentUser.Id.HasValue ||
            !Guid.TryParse(currentUser.CloudDelegationId, out var grantId) ||
            grantId == Guid.Empty)
        {
            throw DelegationRequired();
        }

        CloudDelegationAccessToken? grant;
        try
        {
            grant = await grantStore.ResolveAsync(
                grantId,
                currentUser.Id.Value,
                DateTime.UtcNow,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw DelegationRequired();
        }

        if (grant is null || string.IsNullOrWhiteSpace(grant.AccessToken))
        {
            throw DelegationRequired();
        }

        return grant.AccessToken;
    }

    private static CloudAiReadException DelegationRequired()
    {
        return new CloudAiReadException(
            CloudAiReadProblemCodes.DelegationRequired,
            "A current Cloud user delegation is required for this query.");
    }
}
