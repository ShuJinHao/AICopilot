using AICopilot.Infrastructure.CloudRead;
using AICopilot.Services.Contracts;
using FluentAssertions;

namespace AICopilot.InProcessTests;

public sealed class CloudDelegationAccessTokenProviderTests
{
    [Theory]
    [InlineData(false, true, "10000000-0000-0000-0000-000000000001")]
    [InlineData(true, false, "10000000-0000-0000-0000-000000000001")]
    [InlineData(true, true, null)]
    [InlineData(true, true, "not-a-guid")]
    public async Task GetCurrentTokenAsync_ShouldRequireAuthenticatedUserOwnedGrant(
        bool isAuthenticated,
        bool hasUserId,
        string? delegationId)
    {
        var currentUser = new StubCurrentUser(
            isAuthenticated,
            hasUserId ? Guid.NewGuid() : null,
            delegationId);
        var store = new StubCloudDelegationGrantStore();
        var provider = new CloudDelegationAccessTokenProvider(currentUser, store);

        var action = () => provider.GetCurrentTokenAsync();

        var exception = await action.Should().ThrowAsync<CloudAiReadException>();
        exception.Which.Code.Should().Be(CloudAiReadProblemCodes.DelegationRequired);
        store.ResolveCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentTokenAsync_ShouldReturnOnlyCurrentUsersResolvedGrant()
    {
        var aiUserId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var store = new StubCloudDelegationGrantStore
        {
            Result = new CloudDelegationAccessToken(
                grantId,
                aiUserId,
                Guid.NewGuid(),
                "runtime-user-delegation-token",
                DateTime.UtcNow.AddMinutes(20))
        };
        var provider = new CloudDelegationAccessTokenProvider(
            new StubCurrentUser(true, aiUserId, grantId.ToString("D")),
            store);

        var token = await provider.GetCurrentTokenAsync();

        token.Should().Be("runtime-user-delegation-token");
        store.LastGrantId.Should().Be(grantId);
        store.LastAiUserId.Should().Be(aiUserId);
        store.ResolveCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetCurrentTokenAsync_ShouldFailClosedForMissingRevokedExpiredCrossUserOrDecryptFailure()
    {
        var aiUserId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var store = new StubCloudDelegationGrantStore { Result = null };
        var provider = new CloudDelegationAccessTokenProvider(
            new StubCurrentUser(true, aiUserId, grantId.ToString("D")),
            store);

        var missing = () => provider.GetCurrentTokenAsync();
        (await missing.Should().ThrowAsync<CloudAiReadException>())
            .Which.Code.Should().Be(CloudAiReadProblemCodes.DelegationRequired);

        store.Exception = new InvalidOperationException("decrypt failed");
        var decryptFailure = () => provider.GetCurrentTokenAsync();
        (await decryptFailure.Should().ThrowAsync<CloudAiReadException>())
            .Which.Code.Should().Be(CloudAiReadProblemCodes.DelegationRequired);
    }

    private sealed class StubCurrentUser(
        bool isAuthenticated,
        Guid? id,
        string? cloudDelegationId) : ICurrentUser
    {
        public Guid? Id => id;
        public string? UserName => null;
        public string? Role => null;
        public string? IdentityProvider => null;
        public string? CloudTenantId => null;
        public string? CloudEmployeeNo => null;
        public string? CloudDepartmentId => null;
        public string? CloudDepartmentName => null;
        public string? CloudStatusVersion => null;
        public string? CloudDelegationId => cloudDelegationId;
        public bool IsAuthenticated => isAuthenticated;
    }

    private sealed class StubCloudDelegationGrantStore : ICloudDelegationGrantStore
    {
        public CloudDelegationAccessToken? Result { get; set; }
        public Exception? Exception { get; set; }
        public int ResolveCalls { get; private set; }
        public Guid? LastGrantId { get; private set; }
        public Guid? LastAiUserId { get; private set; }

        public Task<CloudDelegationGrantSnapshot> CreateAsync(
            CreateCloudDelegationGrantRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CloudDelegationAccessToken?> ResolveAsync(
            Guid grantId,
            Guid aiUserId,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            LastGrantId = grantId;
            LastAiUserId = aiUserId;
            return Exception is null
                ? Task.FromResult(Result)
                : Task.FromException<CloudDelegationAccessToken?>(Exception);
        }

        public Task<CloudDelegationRevocationResult> RevokeCurrentAsync(
            Guid grantId,
            Guid aiUserId,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CloudDelegationPurgeResult> PurgeExpiredAsync(
            DateTime utcNow,
            int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
