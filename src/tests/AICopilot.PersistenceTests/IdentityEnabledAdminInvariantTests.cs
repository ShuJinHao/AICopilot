using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Locking;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.PersistenceTestKit;
using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Commands;
using AICopilot.IdentityService.Queries;
using AICopilot.MigrationWorkApp;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using static AICopilot.PersistenceTests.IdentityPersistenceTestSupport;
using System.Threading.Channels;

namespace AICopilot.PersistenceTests;

[Collection(IdentityPersistenceTestCollection.Name)]
public sealed class IdentityEnabledAdminInvariantTests(PostgresPersistenceFixture fixture)
{
    [Theory]
    [InlineData(AdminDecreaseAction.Disable, AdminDecreaseAction.Disable)]
    [InlineData(AdminDecreaseAction.Disable, AdminDecreaseAction.Demote)]
    [InlineData(AdminDecreaseAction.Demote, AdminDecreaseAction.Demote)]
    public async Task ConcurrentAdminDecrease_ShouldSerializeAndKeepOneEnabledAdmin(
        AdminDecreaseAction firstAction,
        AdminDecreaseAction secondAction)
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        await SeedUsersAsync(
            database.ConnectionString,
            new TestUser(firstUserId, $"admin-a-{Guid.NewGuid():N}", false, [IdentityRoleNames.Admin]),
            new TestUser(secondUserId, $"admin-b-{Guid.NewGuid():N}", false, [IdentityRoleNames.Admin]));
        var initialSecurityStamps = await GetSecurityStampsAsync(
            database.ConnectionString,
            firstUserId,
            secondUserId);

        await using var controlConnection = new NpgsqlConnection(database.ConnectionString);
        await controlConnection.OpenAsync();
        await using var controlTransaction = await controlConnection.BeginTransactionAsync();
        await PostgreSqlAdvisoryLock.AcquireTransactionAsync(
            controlConnection,
            controlTransaction,
            PostgresIdentityEnabledAdminInvariantGuard.ResourceKey);

        await using var firstScope = IdentityMutationTestScope.Create(database.ConnectionString);
        await using var secondScope = IdentityMutationTestScope.Create(database.ConnectionString);
        var firstTask = firstScope.ExecuteAsync(firstAction, firstUserId);
        var secondTask = secondScope.ExecuteAsync(secondAction, secondUserId);

        try
        {
            await Task.WhenAll(
                firstScope.WaitForAcquireAttemptAsync(),
                secondScope.WaitForAcquireAttemptAsync());
            await controlTransaction.CommitAsync();
        }
        catch
        {
            await controlTransaction.RollbackAsync();
            throw;
        }

        var results = await Task.WhenAll(firstTask, secondTask);
        results.Count(result => result.IsSuccess).Should().Be(1);
        results.Count(result => !result.IsSuccess).Should().Be(1);
        await AssertEnabledAdminCountAsync(database.ConnectionString, 1);

        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var auditEntries = await verification.AuditLogs
            .Where(entry => entry.TargetId == firstUserId.ToString() ||
                            entry.TargetId == secondUserId.ToString())
            .ToListAsync();
        auditEntries.Should().ContainSingle(entry => entry.Result == AuditResults.Succeeded);
        auditEntries.Should().ContainSingle(entry => entry.Result == AuditResults.Rejected);

        var successfulUserId = Guid.Parse(auditEntries.Single(entry =>
            entry.Result == AuditResults.Succeeded).TargetId!);
        var rejectedUserId = successfulUserId == firstUserId ? secondUserId : firstUserId;
        var finalSecurityStamps = await GetSecurityStampsAsync(
            database.ConnectionString,
            firstUserId,
            secondUserId);
        finalSecurityStamps[successfulUserId].Should().NotBe(initialSecurityStamps[successfulUserId]);
        finalSecurityStamps[rejectedUserId].Should().Be(initialSecurityStamps[rejectedUserId]);
        await AssertMarkerCountAsync(database.ConnectionString, 2);
    }

    [Fact]
    public async Task TransientRetry_ShouldReacquireInvariantAndRereadCommittedAdminState()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var retriedUserId = Guid.NewGuid();
        var competingUserId = Guid.NewGuid();
        await SeedUsersAsync(
            database.ConnectionString,
            new TestUser(
                retriedUserId,
                $"retry-admin-{Guid.NewGuid():N}",
                false,
                [IdentityRoleNames.Admin]),
            new TestUser(
                competingUserId,
                $"competing-admin-{Guid.NewGuid():N}",
                false,
                [IdentityRoleNames.Admin]));

        await using var controlConnection = new NpgsqlConnection(database.ConnectionString);
        await controlConnection.OpenAsync();
        await using var controlTransaction = await controlConnection.BeginTransactionAsync();
        await PostgreSqlAdvisoryLock.AcquireTransactionAsync(
            controlConnection,
            controlTransaction,
            PostgresIdentityEnabledAdminInvariantGuard.ResourceKey);

        var retryFault = new FailFirstBusinessSaveInterceptor();
        await using var retriedScope = IdentityMutationTestScope.Create(
            database.ConnectionString,
            retryFault);
        await using var competingScope = IdentityMutationTestScope.Create(database.ConnectionString);

        var retriedTask = retriedScope.ExecuteAsync(
            AdminDecreaseAction.Disable,
            retriedUserId);
        await retriedScope.WaitForAcquireAttemptAsync();

        var competingTask = competingScope.ExecuteAsync(
            AdminDecreaseAction.Disable,
            competingUserId);
        await competingScope.WaitForAcquireAttemptAsync();

        try
        {
            await controlTransaction.CommitAsync();
        }
        catch
        {
            await controlTransaction.RollbackAsync();
            throw;
        }

        var retriedResult = await retriedTask;
        var competingResult = await competingTask;

        retryFault.SaveAttemptCount.Should().Be(2);
        retriedScope.InvariantAcquireCount.Should().Be(2);
        competingScope.InvariantAcquireCount.Should().Be(1);
        retriedResult.IsSuccess.Should().BeFalse(
            "the retry must observe the competing commit and preserve the last enabled Admin");
        competingResult.IsSuccess.Should().BeTrue();
        await AssertEnabledAdminCountAsync(database.ConnectionString, 1);
        await AssertUserDisabledAsync(database.ConnectionString, retriedUserId, expected: false);
        await AssertUserDisabledAsync(database.ConnectionString, competingUserId, expected: true);

        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.AuditLogs.CountAsync(entry =>
            entry.TargetId == retriedUserId.ToString() &&
            entry.Result == AuditResults.Rejected)).Should().Be(1);
        (await verification.AuditLogs.CountAsync(entry =>
            entry.TargetId == competingUserId.ToString() &&
            entry.Result == AuditResults.Succeeded)).Should().Be(1);
        await AssertMarkerCountAsync(database.ConnectionString, 2);
    }

    [Theory]
    [InlineData(AdminDecreaseAction.Disable)]
    [InlineData(AdminDecreaseAction.Demote)]
    public async Task LastEnabledMultiRoleAdmin_ShouldRejectWithoutSideEffects(
        AdminDecreaseAction action)
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var userId = Guid.NewGuid();
        await SeedUsersAsync(
            database.ConnectionString,
            new TestUser(
                userId,
                $"multi-role-admin-{Guid.NewGuid():N}",
                false,
                [IdentityRoleNames.Admin, "Aardvark"]));
        var initialStamp = (await GetSecurityStampsAsync(database.ConnectionString, userId))[userId];

        await using var scope = IdentityMutationTestScope.Create(database.ConnectionString);
        var result = await scope.ExecuteAsync(action, userId);

        result.IsSuccess.Should().BeFalse();
        await AssertEnabledAdminCountAsync(database.ConnectionString, 1);
        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var user = await verification.Users.SingleAsync(item => item.Id == userId);
        IdentityGovernanceHelper.IsUserDisabled(user).Should().BeFalse();
        user.SecurityStamp.Should().Be(initialStamp);
        var roles = await verification.UserRoles
            .Where(item => item.UserId == userId)
            .Join(
                verification.Roles,
                userRole => userRole.RoleId,
                role => role.Id,
                (_, role) => role.Name!)
            .OrderBy(role => role)
            .ToListAsync();
        roles.Should().Equal("Aardvark", IdentityRoleNames.Admin);
        (await verification.AuditLogs.CountAsync(entry =>
            entry.TargetId == userId.ToString() && entry.Result == AuditResults.Rejected))
            .Should().Be(1);
        await AssertMarkerCountAsync(database.ConnectionString, 1);
    }

    [Theory]
    [InlineData(AdminDecreaseAction.Disable)]
    [InlineData(AdminDecreaseAction.Demote)]
    public async Task IdentitySeedAndAdminDecrease_ShouldShareTheSameInvariantLock(
        AdminDecreaseAction action)
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var bootstrapUserName = $"bootstrap-{Guid.NewGuid():N}";
        var bootstrapUserId = Guid.NewGuid();
        await SeedUsersAsync(
            database.ConnectionString,
            new TestUser(
                bootstrapUserId,
                bootstrapUserName,
                false,
                [IdentityRoleNames.Admin]));
        var configuration = CreateBootstrapConfiguration(bootstrapUserName);

        await using var controlConnection = new NpgsqlConnection(database.ConnectionString);
        await controlConnection.OpenAsync();
        await using var controlTransaction = await controlConnection.BeginTransactionAsync();
        await PostgreSqlAdvisoryLock.AcquireTransactionAsync(
            controlConnection,
            controlTransaction,
            PostgresIdentityEnabledAdminInvariantGuard.ResourceKey);

        var seedAcquireStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var seedTask = RunIdentitySeedAsync(
            database.ConnectionString,
            configuration,
            seedAcquireStarted);
        await using var mutationScope = IdentityMutationTestScope.Create(database.ConnectionString);
        var mutationTask = mutationScope.ExecuteAsync(action, bootstrapUserId);

        try
        {
            await Task.WhenAll(
                seedAcquireStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)),
                mutationScope.WaitForAcquireAttemptAsync());
            await controlTransaction.CommitAsync();
        }
        catch
        {
            await controlTransaction.RollbackAsync();
            throw;
        }

        await seedTask;
        var mutationResult = await mutationTask;
        mutationResult.IsSuccess.Should().BeFalse();
        await AssertEnabledAdminCountAsync(database.ConnectionString, 1);

        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.AuditLogs.CountAsync(entry =>
            entry.TargetId == bootstrapUserId.ToString() &&
            entry.Result == AuditResults.Rejected)).Should().Be(1);
    }

    [Fact]
    public async Task IdentitySeed_ShouldRejectMissingBootstrapWhenNoEnabledAdminExists()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);

        Func<Task> action = () => RunIdentitySeedAsync(
            database.ConnectionString,
            new ConfigurationBuilder().Build());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*at least one enabled Admin*");
        await AssertEnabledAdminCountAsync(database.ConnectionString, 0);
        await AssertMarkerCountAsync(database.ConnectionString, 0);
    }

    [Fact]
    public async Task IdentitySeed_ShouldNotSilentlyEnableDisabledBootstrapAdmin()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var bootstrapUserName = $"disabled-bootstrap-{Guid.NewGuid():N}";
        var bootstrapUserId = Guid.NewGuid();
        await SeedUsersAsync(
            database.ConnectionString,
            new TestUser(
                bootstrapUserId,
                bootstrapUserName,
                true,
                [IdentityRoleNames.Admin]));
        var configuration = CreateBootstrapConfiguration(bootstrapUserName);

        Func<Task> action = () => RunIdentitySeedAsync(database.ConnectionString, configuration);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*at least one enabled Admin*");
        await AssertEnabledAdminCountAsync(database.ConnectionString, 0);
        await AssertUserDisabledAsync(database.ConnectionString, bootstrapUserId, expected: true);
        await AssertMarkerCountAsync(database.ConnectionString, 0);
    }

    [Fact]
    public async Task IdentitySeed_ShouldPreserveDisabledBootstrapWhenAnotherAdminIsEnabled()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var bootstrapUserName = $"disabled-bootstrap-{Guid.NewGuid():N}";
        var bootstrapUserId = Guid.NewGuid();
        await SeedUsersAsync(
            database.ConnectionString,
            new TestUser(
                bootstrapUserId,
                bootstrapUserName,
                true,
                [IdentityRoleNames.Admin]),
            new TestUser(
                Guid.NewGuid(),
                $"recovery-admin-{Guid.NewGuid():N}",
                false,
                [IdentityRoleNames.Admin]));

        await RunIdentitySeedAsync(
            database.ConnectionString,
            CreateBootstrapConfiguration(bootstrapUserName));

        await AssertEnabledAdminCountAsync(database.ConnectionString, 1);
        await AssertUserDisabledAsync(database.ConnectionString, bootstrapUserId, expected: true);
        await AssertMarkerCountAsync(database.ConnectionString, 1);
    }

    [Fact]
    public async Task InitializationStatus_ShouldRequireAnEnabledAdminAndHandleMissingRole()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var emptyStatus = await GetInitializationStatusAsync(
            database.ConnectionString,
            new ConfigurationBuilder().Build());
        emptyStatus.HasAdminRole.Should().BeFalse();
        emptyStatus.HasEnabledAdminUser.Should().BeFalse();
        emptyStatus.IsInitialized.Should().BeFalse();

        var bootstrapUserName = $"disabled-status-{Guid.NewGuid():N}";
        await SeedUsersAsync(
            database.ConnectionString,
            new TestUser(
                Guid.NewGuid(),
                bootstrapUserName,
                true,
                [IdentityRoleNames.Admin]));
        var disabledStatus = await GetInitializationStatusAsync(
            database.ConnectionString,
            CreateBootstrapConfiguration(bootstrapUserName));
        disabledStatus.HasAdminRole.Should().BeTrue();
        disabledStatus.HasEnabledAdminUser.Should().BeFalse();
        disabledStatus.IsInitialized.Should().BeFalse();
    }

    private static async Task<InitializationStatusDto> GetInitializationStatusAsync(
        string connectionString,
        IConfiguration configuration)
    {
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(connectionString));
        using var managers = IdentityManagerTestScope.Create(dbContext);
        var invariant = new EnabledAdminInvariantPolicy(
            managers.UserManager,
            new PostgresIdentityEnabledAdminInvariantGuard(dbContext));
        var handler = new GetInitializationStatusQueryHandler(
            managers.RoleManager,
            invariant,
            configuration);
        var result = await handler.Handle(
            new GetInitializationStatusQuery(),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    private static async Task RunIdentitySeedAsync(
        string connectionString,
        IConfiguration configuration,
        TaskCompletionSource? acquireStarted = null)
    {
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(connectionString));
        using var managers = IdentityManagerTestScope.Create(dbContext);
        var permissionCatalog = new PermissionCatalog();
        IIdentityEnabledAdminInvariantGuard invariantGuard =
            new PostgresIdentityEnabledAdminInvariantGuard(dbContext);
        if (acquireStarted is not null)
        {
            invariantGuard = new SignalingIdentityEnabledAdminInvariantGuard(
                invariantGuard,
                acquireStarted);
        }
        var invariant = new EnabledAdminInvariantPolicy(
            managers.UserManager,
            invariantGuard);
        await MigrationWorkerIdentitySeeder.SeedAsync(
            managers.RoleManager,
            managers.UserManager,
            permissionCatalog,
            new IdentityAccessService(
                managers.UserManager,
                managers.RoleManager,
                permissionCatalog),
            invariant,
            CreateService(connectionString, dbContext),
            configuration,
            CancellationToken.None);
    }

    private static IConfiguration CreateBootstrapConfiguration(string userName)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:UserName"] = userName,
                ["BootstrapAdmin:Password"] = "ValidPassword123!"
            })
            .Build();
    }

    private static async Task SeedUsersAsync(
        string connectionString,
        params TestUser[] users)
    {
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(connectionString));
        using var managers = IdentityManagerTestScope.Create(dbContext);
        var roleNames = users
            .SelectMany(user => user.Roles)
            .Append(IdentityRoleNames.Admin)
            .Append(IdentityRoleNames.User)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in roleNames)
        {
            (await managers.RoleManager.CreateAsync(new IdentityRole<Guid>(roleName)))
                .Succeeded.Should().BeTrue();
        }

        foreach (var definition in users)
        {
            var user = new ApplicationUser
            {
                Id = definition.Id,
                UserName = definition.UserName
            };
            if (definition.Disabled)
            {
                IdentityGovernanceHelper.MarkUserDisabled(user);
            }

            (await managers.UserManager.CreateAsync(user)).Succeeded.Should().BeTrue();
            foreach (var role in definition.Roles)
            {
                (await managers.UserManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
            }
        }
    }

    private static async Task AssertEnabledAdminCountAsync(
        string connectionString,
        int expected)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)::int
            FROM identity."AspNetUsers" AS users
            INNER JOIN identity."AspNetUserRoles" AS user_roles
                ON user_roles."UserId" = users."Id"
            INNER JOIN identity."AspNetRoles" AS roles
                ON roles."Id" = user_roles."RoleId"
            WHERE roles."NormalizedName" = 'ADMIN'
              AND NOT (
                  users."LockoutEnabled"
                  AND users."LockoutEnd" IS NOT NULL
                  AND users."LockoutEnd" > NOW())
            """;
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(expected);
    }

    private static async Task<Dictionary<Guid, string?>> GetSecurityStampsAsync(
        string connectionString,
        params Guid[] userIds)
    {
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(connectionString));
        return await dbContext.Users
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.SecurityStamp);
    }

    private static async Task AssertUserDisabledAsync(
        string connectionString,
        Guid userId,
        bool expected)
    {
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(connectionString));
        var user = await dbContext.Users.SingleAsync(item => item.Id == userId);
        IdentityGovernanceHelper.IsUserDisabled(user).Should().Be(expected);
    }

    private sealed class IdentityMutationTestScope : IAsyncDisposable
    {
        private readonly IdentityStoreDbContext dbContext;
        private readonly IdentityManagerTestScope managers;
        private readonly CountingIdentityEnabledAdminInvariantGuard invariantGuard;
        private readonly EnabledAdminInvariantPolicy invariant;
        private readonly IIdentityAuditLogWriter auditWriter;
        private readonly IdentityTransactionalExecutionService transactionalExecutionService;

        private IdentityMutationTestScope(
            string connectionString,
            IdentityStoreDbContext dbContext,
            IdentityManagerTestScope managers)
        {
            this.dbContext = dbContext;
            this.managers = managers;
            invariantGuard = new CountingIdentityEnabledAdminInvariantGuard(
                new PostgresIdentityEnabledAdminInvariantGuard(dbContext));
            invariant = new EnabledAdminInvariantPolicy(
                managers.UserManager,
                invariantGuard);
            auditWriter = new IdentityAuditLogWriter(dbContext);
            transactionalExecutionService = CreateService(connectionString, dbContext);
        }

        public int InvariantAcquireCount => invariantGuard.AcquireCount;

        public Task WaitForAcquireAttemptAsync()
        {
            return invariantGuard.WaitForAcquireAttemptAsync();
        }

        public static IdentityMutationTestScope Create(
            string connectionString,
            params IInterceptor[] interceptors)
        {
            var dbContext = new IdentityStoreDbContext(
                CreateIdentityOptions(connectionString, interceptors));
            return new IdentityMutationTestScope(
                connectionString,
                dbContext,
                IdentityManagerTestScope.Create(dbContext));
        }

        public Task<Result<UserSummaryDto>> ExecuteAsync(
            AdminDecreaseAction action,
            Guid userId)
        {
            return action switch
            {
                AdminDecreaseAction.Disable => new DisableUserCommandHandler(
                    managers.UserManager,
                    invariant,
                    auditWriter,
                    transactionalExecutionService).Handle(
                    new DisableUserCommand(userId.ToString()),
                    CancellationToken.None),
                AdminDecreaseAction.Demote => new UpdateUserRoleCommandHandler(
                    managers.UserManager,
                    managers.RoleManager,
                    invariant,
                    auditWriter,
                    transactionalExecutionService).Handle(
                    new UpdateUserRoleCommand(userId.ToString(), IdentityRoleNames.User),
                    CancellationToken.None),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
            };
        }

        public async ValueTask DisposeAsync()
        {
            managers.Dispose();
            await dbContext.DisposeAsync();
        }
    }

    private sealed class CountingIdentityEnabledAdminInvariantGuard(
        IIdentityEnabledAdminInvariantGuard inner) : IIdentityEnabledAdminInvariantGuard
    {
        private int acquireCount;
        private readonly Channel<int> acquireAttempts = Channel.CreateUnbounded<int>();

        public int AcquireCount => Volatile.Read(ref acquireCount);

        public async Task AcquireAsync(CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref acquireCount);
            var acquireTask = inner.AcquireAsync(cancellationToken);
            acquireAttempts.Writer.TryWrite(attempt).Should().BeTrue();
            await acquireTask;
        }

        public async Task WaitForAcquireAttemptAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _ = await acquireAttempts.Reader.ReadAsync(timeout.Token);
        }
    }

    private sealed class SignalingIdentityEnabledAdminInvariantGuard(
        IIdentityEnabledAdminInvariantGuard inner,
        TaskCompletionSource acquireStarted) : IIdentityEnabledAdminInvariantGuard
    {
        public async Task AcquireAsync(CancellationToken cancellationToken = default)
        {
            var acquireTask = inner.AcquireAsync(cancellationToken);
            acquireStarted.TrySetResult();
            await acquireTask;
        }
    }

    public enum AdminDecreaseAction
    {
        Disable,
        Demote
    }

    private sealed record TestUser(
        Guid Id,
        string UserName,
        bool Disabled,
        string[] Roles);
}
