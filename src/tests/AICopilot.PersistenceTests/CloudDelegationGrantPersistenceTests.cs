using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.CloudDelegations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static AICopilot.PersistenceTests.IdentityPersistenceTestSupport;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class CloudDelegationGrantPersistenceTests(
    PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task GrantStore_ShouldEncryptTokenAndFailClosedForWrongUserExpiryAndRevocation()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var aiUserId = await CreateUserAsync(database.ConnectionString);
        var grantId = Guid.NewGuid();
        var cloudUserId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var expiresAt = createdAt.AddMinutes(30);
        const string rawToken = "cloud-delegation-secret-token";

        await using (var context = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            var store = new CloudDelegationGrantStore(
                context,
                dataProtectionProvider);
            await store.CreateAsync(new CreateCloudDelegationGrantRequest(
                grantId,
                aiUserId,
                cloudUserId,
                "https://cloud.example.com",
                CloudOidcIdentityProfile.DefaultTenantId,
                rawToken,
                expiresAt,
                "status-v1",
                createdAt));
            await context.SaveChangesAsync();
        }

        await using (var context = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            var persisted = await context.CloudDelegationGrants
                .AsNoTracking()
                .SingleAsync(grant => grant.GrantId == grantId);
            persisted.ProtectedToken.Should().NotBe(rawToken);
            persisted.ProtectedToken.Should().NotContain(rawToken);

            var store = new CloudDelegationGrantStore(
                context,
                dataProtectionProvider);
            var resolved = await store.ResolveAsync(
                grantId,
                aiUserId,
                createdAt.AddMinutes(1));
            resolved.Should().NotBeNull();
            resolved!.AccessToken.Should().Be(rawToken);
            (await store.ResolveAsync(
                    grantId,
                    Guid.NewGuid(),
                    createdAt.AddMinutes(1)))
                .Should().BeNull();
            (await store.ResolveAsync(
                    grantId,
                    aiUserId,
                    expiresAt))
                .Should().BeNull();

            var grant = await context.CloudDelegationGrants.SingleAsync(
                candidate => candidate.GrantId == grantId);
            grant.RevokedAtUtc = createdAt.AddMinutes(2);
            await context.SaveChangesAsync();
            (await store.ResolveAsync(
                    grantId,
                    aiUserId,
                    createdAt.AddMinutes(3)))
                .Should().BeNull();
        }
    }

    [Fact]
    public async Task GrantStore_ShouldFailClosedWhenTokenCannotBeDecrypted()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var aiUserId = await CreateUserAsync(database.ConnectionString);
        var grantId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        await using (var context = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            var writer = new CloudDelegationGrantStore(
                context,
                new EphemeralDataProtectionProvider());
            await writer.CreateAsync(new CreateCloudDelegationGrantRequest(
                grantId,
                aiUserId,
                Guid.NewGuid(),
                "https://cloud.example.com",
                CloudOidcIdentityProfile.DefaultTenantId,
                "token-encrypted-with-another-key-ring",
                createdAt.AddMinutes(30),
                "status-v1",
                createdAt));
            await context.SaveChangesAsync();
        }

        await using var readContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var reader = new CloudDelegationGrantStore(
            readContext,
            new EphemeralDataProtectionProvider());

        (await reader.ResolveAsync(
                grantId,
                aiUserId,
                createdAt.AddMinutes(1)))
            .Should().BeNull();
    }

    [Fact]
    public async Task RevokeCurrent_ShouldBeOwnerBoundIdempotentAndClearProtectedToken()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var protector = new EphemeralDataProtectionProvider();
        var aiUserId = await CreateUserAsync(database.ConnectionString);
        var grantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var context = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            var store = new CloudDelegationGrantStore(context, protector);
            await store.CreateAsync(CreateGrantRequest(grantId, aiUserId, now));
            await context.SaveChangesAsync();
        }

        await using (var context = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            var store = new CloudDelegationGrantStore(context, protector);
            var service = CreateService(database.ConnectionString, context);

            var wrongUser = await service.ExecuteAsync(ct => store.RevokeCurrentAsync(
                grantId,
                Guid.NewGuid(),
                now.AddMinutes(1),
                ct));
            wrongUser.Found.Should().BeFalse();

            var first = await service.ExecuteAsync(ct => store.RevokeCurrentAsync(
                grantId,
                aiUserId,
                now.AddMinutes(2),
                ct));
            first.Should().Be(new CloudDelegationRevocationResult(true, false));

            var second = await service.ExecuteAsync(ct => store.RevokeCurrentAsync(
                grantId,
                aiUserId,
                now.AddMinutes(3),
                ct));
            second.Should().Be(new CloudDelegationRevocationResult(true, true));
        }

        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var persisted = await verification.CloudDelegationGrants
            .AsNoTracking()
            .SingleAsync(grant => grant.GrantId == grantId);
        persisted.ProtectedToken.Should().BeNull();
        var expectedRevokedAtUtc = now.AddMinutes(2);
        expectedRevokedAtUtc = expectedRevokedAtUtc.AddTicks(
            -(expectedRevokedAtUtc.Ticks % TimeSpan.TicksPerMicrosecond));
        persisted.RevokedAtUtc.Should().Be(expectedRevokedAtUtc);
        var resolver = new CloudDelegationGrantStore(verification, protector);
        (await resolver.ResolveAsync(grantId, aiUserId, now.AddMinutes(2)))
            .Should().BeNull();
    }

    [Fact]
    public async Task PurgeExpired_ShouldClearNewExpiryDeleteOldMetadataAndHonorBatchLimit()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var protector = new EphemeralDataProtectionProvider();
        var aiUserId = await CreateUserAsync(database.ConnectionString);
        var now = DateTime.UtcNow;
        var recentExpiredId = Guid.NewGuid();
        var oldExpiredId = Guid.NewGuid();
        var oldRevokedId = Guid.NewGuid();
        var activeId = Guid.NewGuid();

        await using (var context = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            var store = new CloudDelegationGrantStore(context, protector);
            await store.CreateAsync(CreateGrantRequest(
                recentExpiredId,
                aiUserId,
                now.AddHours(-2),
                now.AddHours(-1)));
            await store.CreateAsync(CreateGrantRequest(
                oldExpiredId,
                aiUserId,
                now.AddHours(-27),
                now.AddHours(-26)));
            await store.CreateAsync(CreateGrantRequest(activeId, aiUserId, now));
            await store.CreateAsync(CreateGrantRequest(oldRevokedId, aiUserId, now));
            context.CloudDelegationGrants.Local.Single(
                    grant => grant.GrantId == oldRevokedId)
                .RevokedAtUtc = now.AddHours(-25);
            for (var index = 0; index < 501; index++)
            {
                await store.CreateAsync(CreateGrantRequest(
                    Guid.NewGuid(),
                    aiUserId,
                    now.AddHours(-2),
                    now.AddHours(-1)));
            }

            await context.SaveChangesAsync();
        }

        CloudDelegationPurgeResult result;
        await using (var context = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            var store = new CloudDelegationGrantStore(context, protector);
            result = await CreateService(database.ConnectionString, context)
                .ExecuteAsync(ct => store.PurgeExpiredAsync(now, 500, ct));
        }

        result.LockAcquired.Should().BeTrue();
        (result.ClearedTokenCount + result.DeletedMetadataCount).Should().Be(500);

        CloudDelegationPurgeResult secondResult;
        await using (var context = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            var store = new CloudDelegationGrantStore(context, protector);
            secondResult = await CreateService(database.ConnectionString, context)
                .ExecuteAsync(ct => store.PurgeExpiredAsync(now, 500, ct));
        }

        secondResult.LockAcquired.Should().BeTrue();
        secondResult.DeletedMetadataCount.Should().BeGreaterThanOrEqualTo(1);
        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.CloudDelegationGrants.AnyAsync(
                grant => grant.GrantId == oldExpiredId))
            .Should().BeFalse();
        (await verification.CloudDelegationGrants.AnyAsync(
                grant => grant.GrantId == oldRevokedId))
            .Should().BeFalse();
        (await verification.CloudDelegationGrants.SingleAsync(
                grant => grant.GrantId == activeId))
            .ProtectedToken.Should().NotBeNull();
        (await verification.CloudDelegationGrants.CountAsync(grant =>
                grant.ExpiresAtUtc <= now && grant.ProtectedToken != null))
            .Should().Be(0);
    }

    [Fact]
    public async Task PurgeExpired_ShouldAllowOnlyOneConcurrentDatabaseLeaseOwner()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var protector = new EphemeralDataProtectionProvider();
        var now = DateTime.UtcNow;
        await using var firstContext = new IdentityStoreDbContext(
            CreateNonRetryIdentityOptions(database.ConnectionString));
        await using var secondContext = new IdentityStoreDbContext(
            CreateNonRetryIdentityOptions(database.ConnectionString));
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync();
        await using var secondTransaction = await secondContext.Database.BeginTransactionAsync();
        var firstStore = new CloudDelegationGrantStore(firstContext, protector);
        var secondStore = new CloudDelegationGrantStore(secondContext, protector);

        var first = await firstStore.PurgeExpiredAsync(now, 500);
        var second = await secondStore.PurgeExpiredAsync(now, 500);

        first.LockAcquired.Should().BeTrue();
        second.Should().Be(new CloudDelegationPurgeResult(false, 0, 0));
        await firstTransaction.RollbackAsync();
        await secondTransaction.RollbackAsync();
    }

    private static DbContextOptions<IdentityStoreDbContext> CreateNonRetryIdentityOptions(
        string connectionString)
    {
        var history = MigrationHistoryTables.IdentityStore;
        return new DbContextOptionsBuilder<IdentityStoreDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    history.TableName,
                    history.Schema))
            .Options;
    }

    private static CreateCloudDelegationGrantRequest CreateGrantRequest(
        Guid grantId,
        Guid aiUserId,
        DateTime createdAtUtc,
        DateTime? expiresAtUtc = null) =>
        new(
            grantId,
            aiUserId,
            Guid.NewGuid(),
            "https://cloud.example.com",
            CloudOidcIdentityProfile.DefaultTenantId,
            $"cloud-token-{grantId:N}",
            expiresAtUtc ?? createdAtUtc.AddMinutes(30),
            "status-v1",
            createdAtUtc);

    private static async Task<Guid> CreateUserAsync(string connectionString)
    {
        await using var context = new IdentityStoreDbContext(
            CreateIdentityOptions(connectionString));
        using var managers = IdentityManagerTestScope.Create(context);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"delegation-{Guid.NewGuid():N}",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        (await managers.UserManager.CreateAsync(user)).Succeeded.Should().BeTrue();
        return user.Id;
    }
}
