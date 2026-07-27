using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.ExternalIdentities;
using AICopilot.EntityFrameworkCore.Locking;
using AICopilot.IdentityService.Commands;
using AICopilot.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;
using static AICopilot.PersistenceTests.IdentityPersistenceTestSupport;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class CloudOidcBindingConcurrencyTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task ConfirmExistingCloudOidcAccount_ShouldSerializeRealPostgresBindingConflict()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        const string firstUserName = "E1001";
        const string secondUserName = "E1002";
        const string password = "ValidPassword123!";

        await using (var seedContext = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            using var seedManagers = IdentityManagerTestScope.Create(seedContext);
            (await seedManagers.UserManager.CreateAsync(
                new ApplicationUser { Id = firstUserId, UserName = firstUserName },
                password)).Succeeded.Should().BeTrue();
            (await seedManagers.UserManager.CreateAsync(
                new ApplicationUser { Id = secondUserId, UserName = secondUserName },
                password)).Succeeded.Should().BeTrue();
        }

        await using var firstContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        await using var secondContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var firstManagers = IdentityManagerTestScope.Create(firstContext);
        using var secondManagers = IdentityManagerTestScope.Create(secondContext);

        var firstLockAcquired = NewSignal();
        var releaseFirstLock = NewSignal();
        var secondLockAttempted = NewSignal();
        var firstHandler = CreateHandler(
            database.ConnectionString,
            firstContext,
            firstManagers,
            new HoldingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(firstContext),
                firstLockAcquired,
                releaseFirstLock));
        var secondHandler = CreateHandler(
            database.ConnectionString,
            secondContext,
            secondManagers,
            new SignalingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(secondContext),
                secondLockAttempted));

        var firstTask = firstHandler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(firstUserName),
                password),
            CancellationToken.None);
        await firstLockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondTask = secondHandler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(secondUserName),
                password),
            CancellationToken.None);

        try
        {
            await secondLockAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromMilliseconds(250))))
                .Should().NotBeSameAs(secondTask);
        }
        finally
        {
            releaseFirstLock.TrySetResult(true);
        }

        var results = await Task.WhenAll(firstTask, secondTask);

        results.Should().ContainSingle(result => result.Status == ResultStatus.Ok);
        results.Should().ContainSingle(result =>
            result.Status == ResultStatus.Unauthorized &&
            result.Errors!.OfType<ApiProblemDescriptor>().Single().Code ==
            AuthProblemCodes.ExternalIdentityConflict);

        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var binding = await verification.ExternalIdentityBindings.SingleAsync();
        binding.UserId.Should().Be(firstUserId);
        binding.ExternalUserId.Should().Be("shared-cloud-subject");
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcExistingAccountConfirmed" &&
            entry.Result == AuditResults.Succeeded)).Should().Be(1);
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcExistingAccountBindingConflict" &&
            entry.Result == AuditResults.Rejected)).Should().Be(1);
    }

    private static ConfirmExistingCloudOidcAccountCommandHandler CreateHandler(
        string connectionString,
        IdentityStoreDbContext dbContext,
        IdentityManagerTestScope managers,
        IExternalIdentityBindingInvariantGuard invariantGuard)
    {
        return new ConfirmExistingCloudOidcAccountCommandHandler(
            managers.UserManager,
            new ExternalIdentityBindingStore(dbContext),
            invariantGuard,
            new IdentityAuditLogWriter(dbContext),
            new StubJwtTokenGenerator(),
            CreateService(connectionString, dbContext));
    }

    private static CloudOidcIdentityProfile CreateProfile(string employeeNo)
    {
        return new CloudOidcIdentityProfile(
            "https://cloud.example.com",
            "shared-cloud-subject",
            CloudOidcIdentityProfile.DefaultTenantId,
            employeeNo,
            employeeNo,
            $"employee-{employeeNo}",
            employeeNo,
            "D001",
            "制造一部",
            "v1",
            AccountEnabled: true,
            EmployeeActive: true);
    }

    private static TaskCompletionSource<bool> NewSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class StubJwtTokenGenerator : IJwtTokenGenerator
    {
        public Task<string> GenerateTokenAsync(
            JwtTokenUser user,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult($"token-{user.UserName}");
        }
    }

    private sealed class HoldingInvariantGuard(
        IExternalIdentityBindingInvariantGuard inner,
        TaskCompletionSource<bool> acquired,
        TaskCompletionSource<bool> release) : IExternalIdentityBindingInvariantGuard
    {
        public async Task AcquireAsync(
            string provider,
            string tenantId,
            string externalUserId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            await inner.AcquireAsync(
                provider,
                tenantId,
                externalUserId,
                userId,
                cancellationToken);
            acquired.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class SignalingInvariantGuard(
        IExternalIdentityBindingInvariantGuard inner,
        TaskCompletionSource<bool> attempted) : IExternalIdentityBindingInvariantGuard
    {
        public Task AcquireAsync(
            string provider,
            string tenantId,
            string externalUserId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            attempted.TrySetResult(true);
            return inner.AcquireAsync(
                provider,
                tenantId,
                externalUserId,
                userId,
                cancellationToken);
        }
    }
}
