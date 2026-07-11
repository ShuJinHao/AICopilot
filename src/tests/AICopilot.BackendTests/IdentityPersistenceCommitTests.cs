using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Commands;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Collection(PostgresPersistenceTestCollection.Name)]
[Trait("Suite", "PersistenceCommit")]
[Trait("Runtime", "DockerRequired")]
public sealed class IdentityPersistenceCommitTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task IdentityTransaction_ShouldRollbackAuditRows_WhenOperationFails()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var service = CreateService(database.ConnectionString, dbContext);
        var auditWriter = new IdentityAuditLogWriter(dbContext);
        var actionCode = $"Identity.RollbackProbe.{Guid.NewGuid():N}";

        Func<Task> action = () => service.ExecuteAsync<int>(async cancellationToken =>
        {
            await auditWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    actionCode,
                    "RollbackProbe",
                    null,
                    "identity-rollback-probe",
                    AuditResults.Succeeded,
                    "identity rollback probe"),
                cancellationToken);
            throw new InvalidOperationException("identity rollback probe failure");
        });

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("identity rollback probe failure");

        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.AuditLogs.AnyAsync(entry => entry.ActionCode == actionCode))
            .Should().BeFalse();
        await AssertMarkerCountAsync(database.ConnectionString, 0);
    }

    [Fact]
    public async Task IdentityTransaction_ShouldNotWriteMarker_WhenOperationHasNoChanges()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var service = CreateService(database.ConnectionString, dbContext);

        (await service.ExecuteAsync(_ => Task.FromResult(42))).Should().Be(42);

        await AssertMarkerCountAsync(database.ConnectionString, 0);
    }

    [Fact]
    public async Task IdentityTransaction_ShouldRejectResultThroughGenericExecutionEntry()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var service = CreateService(database.ConnectionString, dbContext);

        Func<Task> action = () => service.ExecuteAsync(
            _ => Task.FromResult(Result.Failure("wrong execution entry")));

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must use ExecuteResultAsync*");
        await AssertMarkerCountAsync(database.ConnectionString, 0);
    }

    [Fact]
    public async Task IdentityTransaction_ShouldRollbackUserManagerAutoSave_WhenLaterResultIsNotSuccessful()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var service = CreateService(database.ConnectionString, dbContext);
        var userName = $"identity-rejected-{Guid.NewGuid():N}";
        using var managers = IdentityManagerScope.Create(dbContext);
        var user = new ApplicationUser { UserName = userName };

        var result = await service.ExecuteResultAsync<Guid>(async _ =>
        {
            var createResult = await managers.UserManager.CreateAsync(user, "ValidPassword123!");
            createResult.Succeeded.Should().BeTrue();
            return Result.Failure("simulated later identity step failure");
        });

        result.IsSuccess.Should().BeFalse();
        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.Users.AnyAsync(item => item.UserName == userName)).Should().BeFalse();
        await AssertMarkerCountAsync(database.ConnectionString, 0);
    }

    [Fact]
    public async Task CreateUserHandler_ShouldCommitUserRoleAuditAndMarkerTogether()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var managers = IdentityManagerScope.Create(dbContext);
        var roleName = $"identity-role-{Guid.NewGuid():N}";
        (await managers.RoleManager.CreateAsync(new IdentityRole<Guid>(roleName)))
            .Succeeded.Should().BeTrue();
        var service = CreateService(database.ConnectionString, dbContext);
        var handler = new CreateUserCommandHandler(
            managers.UserManager,
            managers.RoleManager,
            new IdentityAuditLogWriter(dbContext),
            service);
        var userName = $"identity-handler-{Guid.NewGuid():N}";

        var result = await handler.Handle(
            new CreateUserCommand(userName, "ValidPassword123!", roleName),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var user = await verification.Users.SingleAsync(item => item.UserName == userName);
        (await verification.UserRoles.AnyAsync(item => item.UserId == user.Id))
            .Should().BeTrue();
        (await verification.AuditLogs.AnyAsync(entry =>
            entry.ActionCode == "Identity.CreateUser" && entry.TargetId == user.Id.ToString()))
            .Should().BeTrue();
        await AssertMarkerCountAsync(database.ConnectionString, 1);
    }

    [Fact]
    public async Task IdentityTransaction_ShouldVerifyMarkerWithoutReplaying_WhenCommitAcknowledgementIsLost()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var commitFault = new CommitAcknowledgementLostInterceptor();
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString, commitFault));
        var service = CreateService(database.ConnectionString, dbContext);
        var userId = Guid.NewGuid();
        var userName = $"identity-ack-{Guid.NewGuid():N}";
        var operationCount = 0;

        var result = await service.ExecuteAsync(async cancellationToken =>
        {
            operationCount++;
            dbContext.Users.Add(CreateUser(userId, userName));
            await dbContext.SaveChangesAsync(cancellationToken);
            return userId;
        });

        result.Should().Be(userId);
        operationCount.Should().Be(1);
        commitFault.ThrowCount.Should().Be(1);
        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.Users.CountAsync(user => user.Id == userId)).Should().Be(1);
        await AssertMarkerCountAsync(database.ConnectionString, 1);
    }

    [Fact]
    public async Task IdentityTransaction_ShouldClearFailedAttemptBeforePreCommitRetry()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var saveFault = new FailFirstBusinessSaveInterceptor();
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString, saveFault));
        var service = CreateService(database.ConnectionString, dbContext);
        var userId = Guid.NewGuid();
        var userName = $"identity-retry-{Guid.NewGuid():N}";
        var operationCount = 0;

        await service.ExecuteAsync(async cancellationToken =>
        {
            operationCount++;
            dbContext.Users.Add(CreateUser(userId, userName));
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        });

        operationCount.Should().Be(2);
        saveFault.SaveAttemptCount.Should().Be(2);
        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.Users.CountAsync(user => user.Id == userId)).Should().Be(1);
        await AssertMarkerCountAsync(database.ConnectionString, 1);
    }

    private async Task<PostgresScratchDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_identity_commit");
        try
        {
            await using var root = new AiCopilotDbContext(
                PostgresPersistenceTestOptions.Create<AiCopilotDbContext>(
                    database.ConnectionString,
                    MigrationHistoryTables.AiCopilot));
            await root.Database.MigrateAsync();
            await using var identity = new IdentityStoreDbContext(
                CreateIdentityOptions(database.ConnectionString));
            await identity.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static IdentityTransactionalExecutionService CreateService(
        string connectionString,
        IdentityStoreDbContext dbContext)
    {
        return new IdentityTransactionalExecutionService(
            dbContext,
            new PersistenceCommitEngine(
                PostgresPersistenceTestOptions.CreateMarker(connectionString)));
    }

    private static DbContextOptions<IdentityStoreDbContext> CreateIdentityOptions(
        string connectionString,
        params IInterceptor[] interceptors)
    {
        var history = MigrationHistoryTables.IdentityStore;
        var builder = new DbContextOptionsBuilder<IdentityStoreDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable(history.TableName, history.Schema);
                    npgsql.EnableRetryOnFailure(2, TimeSpan.Zero, null);
                });
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return builder.Options;
    }

    private static ApplicationUser CreateUser(Guid id, string userName)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
    }

    private static async Task AssertMarkerCountAsync(string connectionString, int expected)
    {
        await using var markers = new PersistenceCommitMarkerDbContext(
            PostgresPersistenceTestOptions.CreateMarker(connectionString));
        (await markers.CommitMarkers.CountAsync()).Should().Be(expected);
    }

    private sealed class IdentityManagerScope : IDisposable
    {
        private readonly ServiceProvider serviceProvider;

        private IdentityManagerScope(
            ServiceProvider serviceProvider,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager)
        {
            this.serviceProvider = serviceProvider;
            UserManager = userManager;
            RoleManager = roleManager;
        }

        public UserManager<ApplicationUser> UserManager { get; }

        public RoleManager<IdentityRole<Guid>> RoleManager { get; }

        public static IdentityManagerScope Create(IdentityStoreDbContext dbContext)
        {
            var options = Options.Create(new IdentityOptions
            {
                Password =
                {
                    RequireDigit = true,
                    RequireLowercase = true,
                    RequireUppercase = true,
                    RequireNonAlphanumeric = false,
                    RequiredLength = 8
                }
            });
            var userStore = new UserStore<
                ApplicationUser,
                IdentityRole<Guid>,
                IdentityStoreDbContext,
                Guid>(dbContext);
            var roleStore = new RoleStore<
                IdentityRole<Guid>,
                IdentityStoreDbContext,
                Guid>(dbContext);
            var serviceProvider = new ServiceCollection().BuildServiceProvider();
            var userManager = new UserManager<ApplicationUser>(
                userStore,
                options,
                new PasswordHasher<ApplicationUser>(),
                [new UserValidator<ApplicationUser>()],
                [new PasswordValidator<ApplicationUser>()],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                serviceProvider,
                NullLogger<UserManager<ApplicationUser>>.Instance);
            var roleManager = new RoleManager<IdentityRole<Guid>>(
                roleStore,
                [new RoleValidator<IdentityRole<Guid>>()],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                NullLogger<RoleManager<IdentityRole<Guid>>>.Instance);
            return new IdentityManagerScope(serviceProvider, userManager, roleManager);
        }

        public void Dispose()
        {
            UserManager.Dispose();
            RoleManager.Dispose();
            serviceProvider.Dispose();
        }
    }

}
