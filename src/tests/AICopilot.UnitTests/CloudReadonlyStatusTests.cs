using AICopilot.AiGatewayService.Runtime;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.UnitTests;

public sealed class CloudReadonlyStatusTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_ShouldSeparateConfiguredTransportFromMissingCurrentDelegation()
    {
        var status = CloudReadonlyStatusEvaluator.Evaluate(
            CreateReadonlyOptions(),
            CreateAiReadOptions(),
            delegationAvailable: false);

        status.Status.Should().Be(CloudReadonlyRuntimeStatuses.RealMissingDelegation);
        status.TransportConfigured.Should().BeTrue();
        status.DelegationAvailable.Should().BeFalse();
        status.Message.Should().Contain("当前用户");
    }

    [Fact]
    public async Task Handle_ShouldAdvertiseReadyOnlyForGrantOwnedByCurrentAiUser()
    {
        var aiUserId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var grantStore = new FixedGrantStore(new CloudDelegationAccessToken(
            grantId,
            aiUserId,
            Guid.NewGuid(),
            "protected-runtime-token",
            Now.AddMinutes(5).UtcDateTime));
        var handler = new GetCloudReadonlyStatusQueryHandler(
            Options.Create(CreateReadonlyOptions()),
            Options.Create(CreateAiReadOptions()),
            new FixedCurrentUser(aiUserId, grantId),
            grantStore,
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new GetCloudReadonlyStatusQuery(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(CloudReadonlyRuntimeStatuses.RealReady);
        result.Value.TransportConfigured.Should().BeTrue();
        result.Value.DelegationAvailable.Should().BeTrue();
        grantStore.LastGrantId.Should().Be(grantId);
        grantStore.LastAiUserId.Should().Be(aiUserId);
    }

    [Fact]
    public async Task Handle_ShouldFailClosedWhenGrantCannotBeResolved()
    {
        var handler = new GetCloudReadonlyStatusQueryHandler(
            Options.Create(CreateReadonlyOptions()),
            Options.Create(CreateAiReadOptions()),
            new FixedCurrentUser(Guid.NewGuid(), Guid.NewGuid()),
            new FixedGrantStore(null),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new GetCloudReadonlyStatusQuery(),
            CancellationToken.None);

        result.Value!.Status.Should().Be(CloudReadonlyRuntimeStatuses.RealMissingDelegation);
        result.Value.DelegationAvailable.Should().BeFalse();
    }

    private static CloudReadonlyOptions CreateReadonlyOptions() => new()
    {
        Mode = CloudReadonlyDataSourceMode.Real,
        Real = new CloudReadonlyRealOptions
        {
            Enabled = true,
            AllowProductionRead = true
        }
    };

    private static CloudAiReadOptions CreateAiReadOptions() => new()
    {
        Enabled = true,
        BaseUrl = "https://cloud.example.com"
    };

    private sealed class FixedCurrentUser(Guid aiUserId, Guid grantId) : ICurrentUser
    {
        public Guid? Id => aiUserId;
        public string? UserName => "E0001";
        public string? Role => "User";
        public string? IdentityProvider => ExternalIdentityProviders.Cloud;
        public string? CloudTenantId => CloudOidcIdentityProfile.DefaultTenantId;
        public string? CloudEmployeeNo => "E0001";
        public string? CloudDepartmentId => "D001";
        public string? CloudDepartmentName => "制造一部";
        public string? CloudStatusVersion => "v1";
        public string? CloudDelegationId => grantId.ToString("D");
        public bool IsAuthenticated => true;
    }

    private sealed class FixedGrantStore(CloudDelegationAccessToken? token)
        : ICloudDelegationGrantStore
    {
        public Guid? LastGrantId { get; private set; }
        public Guid? LastAiUserId { get; private set; }

        public Task<CloudDelegationAccessToken?> ResolveAsync(
            Guid grantId,
            Guid aiUserId,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            LastGrantId = grantId;
            LastAiUserId = aiUserId;
            return Task.FromResult(token is not null &&
                                   token.GrantId == grantId &&
                                   token.AiUserId == aiUserId &&
                                   token.ExpiresAtUtc > utcNow
                ? token
                : null);
        }

        public Task<CloudDelegationGrantSnapshot> CreateAsync(
            CreateCloudDelegationGrantRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
