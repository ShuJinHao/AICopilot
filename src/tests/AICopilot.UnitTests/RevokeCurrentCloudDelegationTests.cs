using AICopilot.IdentityService.Commands;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.UnitTests;

public sealed class RevokeCurrentCloudDelegationTests
{
    [Fact]
    public async Task Handle_ShouldUseOnlyCurrentUserAndJwtGrantThenWriteStructuredAudit()
    {
        var userId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var store = new RecordingGrantStore(
            new CloudDelegationRevocationResult(true, false));
        var audit = new RecordingAuditWriter();
        var handler = CreateHandler(userId, grantId, store, audit);

        var result = await handler.Handle(
            new RevokeCurrentCloudDelegationCommand(),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        store.RevokeCalls.Should().Be(1);
        store.LastGrantId.Should().Be(grantId);
        store.LastAiUserId.Should().Be(userId);
        audit.Requests.Should().ContainSingle();
        audit.Requests[0].ActionCode.Should().Be("Identity.CloudDelegationRevokeCurrent");
        audit.Requests[0].ChangedFields.Should().BeEquivalentTo(
            ["revokedAtUtc", "protectedToken"]);
        audit.Requests[0].Summary.Contains("token-", StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldBeIdempotentForAlreadyRevokedGrant()
    {
        var store = new RecordingGrantStore(
            new CloudDelegationRevocationResult(true, true));
        var audit = new RecordingAuditWriter();
        var handler = CreateHandler(Guid.NewGuid(), Guid.NewGuid(), store, audit);

        var result = await handler.Handle(
            new RevokeCurrentCloudDelegationCommand(),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        audit.Requests.Should().ContainSingle();
        audit.Requests[0].Metadata!["alreadyRevoked"].Should().Be("true");
        audit.Requests[0].ChangedFields.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldRejectGrantNotOwnedByCurrentUserWithoutAuditSuccess()
    {
        var store = new RecordingGrantStore(
            new CloudDelegationRevocationResult(false, false));
        var audit = new RecordingAuditWriter();
        var handler = CreateHandler(Guid.NewGuid(), Guid.NewGuid(), store, audit);

        var result = await handler.Handle(
            new RevokeCurrentCloudDelegationCommand(),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unauthorized);
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should()
            .Be(AuthProblemCodes.SessionRevoked);
        audit.Requests.Should().BeEmpty();
    }

    private static RevokeCurrentCloudDelegationCommandHandler CreateHandler(
        Guid userId,
        Guid grantId,
        RecordingGrantStore store,
        RecordingAuditWriter audit) =>
        new(
            new FixedCurrentUser(userId, grantId),
            store,
            audit,
            new InlineTransactionService(),
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 9, 1, 0, 0, TimeSpan.Zero)));

    private sealed class FixedCurrentUser(Guid userId, Guid grantId) : ICurrentUser
    {
        public Guid? Id => userId;
        public string? UserName => "current-user";
        public string? Role => "User";
        public string? IdentityProvider => ExternalIdentityProviders.Cloud;
        public string? CloudTenantId => CloudOidcIdentityProfile.DefaultTenantId;
        public string? CloudEmployeeNo => "E0001";
        public string? CloudDepartmentId => null;
        public string? CloudDepartmentName => null;
        public string? CloudStatusVersion => "v1";
        public string? CloudDelegationId => grantId.ToString("D");
        public bool IsAuthenticated => true;
    }

    private sealed class RecordingGrantStore(
        CloudDelegationRevocationResult result) : ICloudDelegationGrantStore
    {
        public int RevokeCalls { get; private set; }
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
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CloudDelegationRevocationResult> RevokeCurrentAsync(
            Guid grantId,
            Guid aiUserId,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            RevokeCalls++;
            LastGrantId = grantId;
            LastAiUserId = aiUserId;
            return Task.FromResult(result);
        }

        public Task<CloudDelegationPurgeResult> PurgeExpiredAsync(
            DateTime utcNow,
            int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAuditWriter : IIdentityAuditLogWriter
    {
        public List<AuditLogWriteRequest> Requests { get; } = [];

        public Task WriteAsync(
            AuditLogWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class InlineTransactionService : ITransactionalExecutionService
    {
        public Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);

        public Task<Result<TValue>> ExecuteResultAsync<TValue>(
            Func<CancellationToken, Task<Result<TValue>>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);

        public Task<Result> ExecuteResultAsync(
            Func<CancellationToken, Task<Result>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
