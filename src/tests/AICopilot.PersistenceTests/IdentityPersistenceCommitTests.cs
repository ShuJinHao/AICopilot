using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Commands;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static AICopilot.PersistenceTests.IdentityPersistenceTestSupport;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class IdentityPersistenceCommitTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task IdentityTransaction_ShouldRollbackAuditRows_WhenOperationFails()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
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
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var service = CreateService(database.ConnectionString, dbContext);

        (await service.ExecuteAsync(_ => Task.FromResult(42))).Should().Be(42);

        await AssertMarkerCountAsync(database.ConnectionString, 0);
    }

    [Fact]
    public async Task IdentityTransaction_ShouldRejectResultThroughGenericExecutionEntry()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
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
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var service = CreateService(database.ConnectionString, dbContext);
        var userName = $"identity-rejected-{Guid.NewGuid():N}";
        using var managers = IdentityManagerTestScope.Create(dbContext);
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
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        await using var dbContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var managers = IdentityManagerTestScope.Create(dbContext);
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
        await using var database = await CreateMigratedDatabaseAsync(fixture);
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
        await using var database = await CreateMigratedDatabaseAsync(fixture);
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

}
