using System.Security.Cryptography;
using System.Text;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.CloudDelegations;
using AICopilot.EntityFrameworkCore.ExternalIdentities;
using AICopilot.EntityFrameworkCore.Locking;
using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Commands;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static AICopilot.PersistenceTests.IdentityPersistenceTestSupport;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class CloudOidcBindingConcurrencyTests(PostgresPersistenceFixture fixture)
{
    private static readonly IDataProtectionProvider DelegationDataProtectionProvider =
        new EphemeralDataProtectionProvider();

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldSerializeConcurrentFirstLoginIdempotently()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        await SeedRolesAsync(database.ConnectionString, IdentityRoleNames.User);
        const string userName = "E-JIT-1001";

        await using var firstContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        await using var secondContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var firstManagers = IdentityManagerTestScope.Create(firstContext);
        using var secondManagers = IdentityManagerTestScope.Create(secondContext);

        var firstLockAcquired = NewSignal();
        var releaseFirstLock = NewSignal();
        var secondLockAttempted = NewSignal();
        var firstHandler = CreateFinalizeHandler(
            database.ConnectionString,
            firstContext,
            firstManagers,
            new HoldingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(firstContext),
                firstLockAcquired,
                releaseFirstLock));
        var secondHandler = CreateFinalizeHandler(
            database.ConnectionString,
            secondContext,
            secondManagers,
            new SignalingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(secondContext),
                secondLockAttempted));
        var profile = CreateProfile(userName, "same-jit-subject");

        var firstTask = firstHandler.Handle(
            new FinalizeCloudOidcLoginCommand(profile, CreateDelegationToken("same-jit-subject")),
            CancellationToken.None);
        await firstLockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondTask = secondHandler.Handle(
            new FinalizeCloudOidcLoginCommand(profile, CreateDelegationToken("same-jit-subject")),
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
        results.Should().OnlyContain(result => result.Status == ResultStatus.Ok);
        results.Select(result => result.Value!.UserName).Should().OnlyContain(name => name == userName);

        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.Users.CountAsync(user => user.NormalizedUserName == userName.ToUpperInvariant()))
            .Should().Be(1);
        var binding = await verification.ExternalIdentityBindings.SingleAsync();
        binding.ExternalUserId.Should().Be(NormalizeCloudSubject("same-jit-subject"));
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcFirstBind" &&
            entry.Result == AuditResults.Succeeded)).Should().Be(1);
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcLogin" &&
            entry.Result == AuditResults.Succeeded)).Should().Be(1);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldSerializeDifferentIdentitiesForSameNormalizedUserName()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        await SeedRolesAsync(database.ConnectionString, IdentityRoleNames.User);
        const string firstUserName = "Case-Sensitive-1001";
        const string secondUserName = "case-sensitive-1001";

        await using var firstContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        await using var secondContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var firstManagers = IdentityManagerTestScope.Create(firstContext);
        using var secondManagers = IdentityManagerTestScope.Create(secondContext);

        var firstLockAcquired = NewSignal();
        var releaseFirstLock = NewSignal();
        var secondLockAttempted = NewSignal();
        var firstHandler = CreateFinalizeHandler(
            database.ConnectionString,
            firstContext,
            firstManagers,
            new HoldingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(firstContext),
                firstLockAcquired,
                releaseFirstLock));
        var secondHandler = CreateFinalizeHandler(
            database.ConnectionString,
            secondContext,
            secondManagers,
            new SignalingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(secondContext),
                secondLockAttempted));

        var firstTask = firstHandler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(firstUserName, "cloud-subject-a"),
                CreateDelegationToken("cloud-subject-a")),
            CancellationToken.None);
        await firstLockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondTask = secondHandler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(secondUserName, "cloud-subject-b"),
                CreateDelegationToken("cloud-subject-b")),
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
        (await verification.Users.CountAsync()).Should().Be(1);
        (await verification.ExternalIdentityBindings.CountAsync()).Should().Be(1);
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcLocalUserBoundToDifferentIdentity" &&
            entry.Result == AuditResults.Rejected)).Should().Be(1);
    }

    [Fact]
    public async Task FinalizeAndConfirmCloudOidcLogin_ShouldNotCreateSecondUserWhenTheyCompete()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        await SeedRolesAsync(database.ConnectionString, IdentityRoleNames.User);
        const string userName = "E-JIT-CONFIRM-1001";

        await using var finalizeContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        await using var confirmContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var finalizeManagers = IdentityManagerTestScope.Create(finalizeContext);
        using var confirmManagers = IdentityManagerTestScope.Create(confirmContext);

        var firstLockAcquired = NewSignal();
        var releaseFirstLock = NewSignal();
        var secondLockAttempted = NewSignal();
        var finalizeHandler = CreateFinalizeHandler(
            database.ConnectionString,
            finalizeContext,
            finalizeManagers,
            new HoldingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(finalizeContext),
                firstLockAcquired,
                releaseFirstLock));
        var confirmHandler = CreateHandler(
            database.ConnectionString,
            confirmContext,
            confirmManagers,
            new SignalingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(confirmContext),
                secondLockAttempted));

        var finalizeTask = finalizeHandler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(userName, "jit-cloud-subject"),
                CreateDelegationToken("jit-cloud-subject")),
            CancellationToken.None);
        await firstLockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var confirmTask = confirmHandler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(userName, "confirm-cloud-subject"),
                CreateDelegationToken("confirm-cloud-subject"),
                "irrelevant-password"),
            CancellationToken.None);

        try
        {
            await secondLockAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (await Task.WhenAny(confirmTask, Task.Delay(TimeSpan.FromMilliseconds(250))))
                .Should().NotBeSameAs(confirmTask);
        }
        finally
        {
            releaseFirstLock.TrySetResult(true);
        }

        (await finalizeTask).Status.Should().Be(ResultStatus.Ok);
        var confirmResult = await confirmTask;
        confirmResult.Status.Should().Be(ResultStatus.Unauthorized);
        confirmResult.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should()
            .Be(AuthProblemCodes.ExternalIdentityConflict);

        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.Users.CountAsync()).Should().Be(1);
        var binding = await verification.ExternalIdentityBindings.SingleAsync();
        binding.ExternalUserId.Should().Be(NormalizeCloudSubject("jit-cloud-subject"));
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcExistingAccountHasNoPassword" &&
            entry.Result == AuditResults.Rejected)).Should().Be(1);
    }

    [Fact]
    public async Task CanonicalAdminCollectionAndConflictingLogin_ShouldSerializeWithoutReplacingBinding()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        const string canonicalUserName = CloudOidcCanonicalAdminOptions.RequiredEmployeeNo;
        var canonicalUserId = await SeedAdminAsync(
            database.ConnectionString,
            canonicalUserName,
            hasPassword: false);

        await using var canonicalContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        await using var ordinaryContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var canonicalManagers = IdentityManagerTestScope.Create(canonicalContext);
        using var ordinaryManagers = IdentityManagerTestScope.Create(ordinaryContext);

        var firstLockAcquired = NewSignal();
        var releaseFirstLock = NewSignal();
        var secondLockAttempted = NewSignal();
        var canonicalHandler = CreateFinalizeHandler(
            database.ConnectionString,
            canonicalContext,
            canonicalManagers,
            new HoldingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(canonicalContext),
                firstLockAcquired,
                releaseFirstLock));
        var ordinaryHandler = CreateFinalizeHandler(
            database.ConnectionString,
            ordinaryContext,
            ordinaryManagers,
            new SignalingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(ordinaryContext),
                secondLockAttempted));

        var canonicalTask = canonicalHandler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(canonicalUserName, "canonical-cloud-subject"),
                CreateDelegationToken("canonical-cloud-subject")),
            CancellationToken.None);
        await firstLockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var ordinaryTask = ordinaryHandler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(canonicalUserName, "conflicting-cloud-subject"),
                CreateDelegationToken("conflicting-cloud-subject")),
            CancellationToken.None);

        try
        {
            await secondLockAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (await Task.WhenAny(ordinaryTask, Task.Delay(TimeSpan.FromMilliseconds(250))))
                .Should().NotBeSameAs(ordinaryTask);
        }
        finally
        {
            releaseFirstLock.TrySetResult(true);
        }

        (await canonicalTask).Status.Should().Be(ResultStatus.Ok);
        var ordinaryResult = await ordinaryTask;
        ordinaryResult.Status.Should().Be(ResultStatus.Unauthorized);
        ordinaryResult.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should()
            .Be(AuthProblemCodes.ExternalIdentityConflict);

        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.Users.CountAsync()).Should().Be(1);
        var binding = await verification.ExternalIdentityBindings.SingleAsync();
        binding.UserId.Should().Be(canonicalUserId);
        binding.ExternalUserId.Should().Be(NormalizeCloudSubject("canonical-cloud-subject"));
        var roleNames = await (
            from userRole in verification.UserRoles
            join role in verification.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == canonicalUserId
            select role.Name).ToArrayAsync();
        roleNames.Should().Equal(IdentityRoleNames.Admin);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldUseSecurityStampCommittedWhileWaitingForBindingLock()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        const string userName = "E-FRESH-STAMP-1001";
        const string externalUserId = "fresh-stamp-subject";
        const string refreshedSecurityStamp = "security-stamp-after-lock";
        await SeedIdentityUserAsync(
            database.ConnectionString,
            userName,
            IdentityRoleNames.User,
            externalUserId);

        await using var handlerContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var managers = IdentityManagerTestScope.Create(handlerContext);
        var lockAcquired = NewSignal();
        var releaseLock = NewSignal();
        var tokenGenerator = new RecordingJwtTokenGenerator();
        var handler = CreateFinalizeHandler(
            database.ConnectionString,
            handlerContext,
            managers,
            new HoldingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(handlerContext),
                lockAcquired,
                releaseLock),
            tokenGenerator: tokenGenerator);

        var loginTask = handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(userName, externalUserId),
                CreateDelegationToken(externalUserId)),
            CancellationToken.None);
        await lockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await using var mutationContext = new IdentityStoreDbContext(
                CreateIdentityOptions(database.ConnectionString));
            var user = await mutationContext.Users.SingleAsync(
                item => item.NormalizedUserName == userName.ToUpperInvariant());
            user.SecurityStamp = refreshedSecurityStamp;
            await mutationContext.SaveChangesAsync();
        }
        finally
        {
            releaseLock.TrySetResult(true);
        }

        (await loginTask).Status.Should().Be(ResultStatus.Ok);
        tokenGenerator.User.Should().NotBeNull();
        tokenGenerator.User!.SecurityStamp.Should().Be(refreshedSecurityStamp);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldInitializeAndPersistMissingSecurityStamp()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        const string userName = "E-MISSING-STAMP-1001";
        const string externalUserId = "missing-stamp-subject";
        var userId = await SeedIdentityUserAsync(
            database.ConnectionString,
            userName,
            IdentityRoleNames.User,
            externalUserId);
        await SetSecurityStampAsync(database.ConnectionString, userId, null);

        await using var handlerContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var managers = IdentityManagerTestScope.Create(handlerContext);
        var tokenGenerator = new RecordingJwtTokenGenerator();
        var handler = CreateFinalizeHandler(
            database.ConnectionString,
            handlerContext,
            managers,
            new PostgresExternalIdentityBindingInvariantGuard(handlerContext),
            tokenGenerator: tokenGenerator);

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(userName, externalUserId),
                CreateDelegationToken(externalUserId)),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var persistedUser = await verification.Users.SingleAsync(user => user.Id == userId);
        persistedUser.SecurityStamp.Should().NotBeNullOrWhiteSpace();
        tokenGenerator.User.Should().NotBeNull();
        tokenGenerator.User!.SecurityStamp.Should().Be(persistedUser.SecurityStamp);
        var roleNames = await (
            from userRole in verification.UserRoles
            join role in verification.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId
            select role.Name).ToArrayAsync();
        roleNames.Should().Equal(IdentityRoleNames.User);
    }

    [Fact]
    public async Task ConfirmExistingCloudOidcAccount_ShouldInitializeAndPersistBlankSecurityStamp()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        const string userName = "E-BLANK-STAMP-1001";
        var userId = await SeedIdentityUserAsync(
            database.ConnectionString,
            userName,
            IdentityRoleNames.User);
        await SetSecurityStampAsync(database.ConnectionString, userId, " ");

        await using var handlerContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var managers = IdentityManagerTestScope.Create(handlerContext);
        var tokenGenerator = new RecordingJwtTokenGenerator();
        var handler = CreateHandler(
            database.ConnectionString,
            handlerContext,
            managers,
            new PostgresExternalIdentityBindingInvariantGuard(handlerContext),
            tokenGenerator);

        var result = await handler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(userName, "blank-stamp-subject"),
                CreateDelegationToken("blank-stamp-subject"),
                "ValidPassword123!"),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        var persistedUser = await verification.Users.SingleAsync(user => user.Id == userId);
        persistedUser.SecurityStamp.Should().NotBeNullOrWhiteSpace();
        tokenGenerator.User.Should().NotBeNull();
        tokenGenerator.User!.SecurityStamp.Should().Be(persistedUser.SecurityStamp);
        (await verification.ExternalIdentityBindings.CountAsync(binding =>
            binding.UserId == userId &&
            binding.ExternalUserId == NormalizeCloudSubject("blank-stamp-subject"))).Should().Be(1);
        var roleNames = await (
            from userRole in verification.UserRoles
            join role in verification.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId
            select role.Name).ToArrayAsync();
        roleNames.Should().Equal(IdentityRoleNames.User);
    }

    [Fact]
    public async Task CanonicalAdminCollection_ShouldRejectUserDisabledWhileWaitingForBindingLock()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        const string canonicalUserName = CloudOidcCanonicalAdminOptions.RequiredEmployeeNo;
        var canonicalUserId = await SeedAdminAsync(
            database.ConnectionString,
            canonicalUserName);
        _ = await SeedAdminAsync(
            database.ConnectionString,
            "BOOTSTRAP-SAFETY-ADMIN");

        await using var handlerContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var managers = IdentityManagerTestScope.Create(handlerContext);
        var lockAcquired = NewSignal();
        var releaseLock = NewSignal();
        var handler = CreateFinalizeHandler(
            database.ConnectionString,
            handlerContext,
            managers,
            new HoldingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(handlerContext),
                lockAcquired,
                releaseLock));

        var loginTask = handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(canonicalUserName, "disabled-canonical-subject"),
                CreateDelegationToken("disabled-canonical-subject")),
            CancellationToken.None);
        await lockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await using var mutationContext = new IdentityStoreDbContext(
                CreateIdentityOptions(database.ConnectionString));
            var user = await mutationContext.Users.SingleAsync(item => item.Id == canonicalUserId);
            IdentityGovernanceHelper.MarkUserDisabled(user);
            IdentityGovernanceHelper.RefreshSecurityStamp(user);
            await mutationContext.SaveChangesAsync();
        }
        finally
        {
            releaseLock.TrySetResult(true);
        }

        var result = await loginTask;
        result.Status.Should().Be(ResultStatus.Unauthorized);
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should()
            .Be(AuthProblemCodes.AccountDisabled);

        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.ExternalIdentityBindings.CountAsync()).Should().Be(0);
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcLocalUserDisabled" &&
            entry.Result == AuditResults.Rejected)).Should().Be(1);
    }

    [Fact]
    public async Task ConfirmExistingCloudOidcAccount_ShouldRejectUserDisabledWhileWaitingForBindingLock()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        const string userName = "E-CONFIRM-FRESH-DISABLED";
        var userId = await SeedIdentityUserAsync(
            database.ConnectionString,
            userName,
            IdentityRoleNames.User);

        await using var handlerContext = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        using var managers = IdentityManagerTestScope.Create(handlerContext);
        var lockAcquired = NewSignal();
        var releaseLock = NewSignal();
        var handler = CreateHandler(
            database.ConnectionString,
            handlerContext,
            managers,
            new HoldingInvariantGuard(
                new PostgresExternalIdentityBindingInvariantGuard(handlerContext),
                lockAcquired,
                releaseLock));

        var confirmTask = handler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(userName, "disabled-confirm-subject"),
                CreateDelegationToken("disabled-confirm-subject"),
                "ValidPassword123!"),
            CancellationToken.None);
        await lockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await using var mutationContext = new IdentityStoreDbContext(
                CreateIdentityOptions(database.ConnectionString));
            var user = await mutationContext.Users.SingleAsync(item => item.Id == userId);
            IdentityGovernanceHelper.MarkUserDisabled(user);
            IdentityGovernanceHelper.RefreshSecurityStamp(user);
            await mutationContext.SaveChangesAsync();
        }
        finally
        {
            releaseLock.TrySetResult(true);
        }

        var result = await confirmTask;
        result.Status.Should().Be(ResultStatus.Unauthorized);
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should()
            .Be(AuthProblemCodes.AccountDisabled);

        await using var verification = new IdentityStoreDbContext(
            CreateIdentityOptions(database.ConnectionString));
        (await verification.ExternalIdentityBindings.CountAsync()).Should().Be(0);
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcExistingAccountDisabled" &&
            entry.Result == AuditResults.Rejected)).Should().Be(1);
    }

    [Fact]
    public async Task IdentityTransaction_ShouldMapOnlyKnownExternalIdentityUniqueConstraints()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        await using (var seedContext = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            using var seedManagers = IdentityManagerTestScope.Create(seedContext);
            (await seedManagers.UserManager.CreateAsync(
                new ApplicationUser { Id = firstUserId, UserName = "known-constraint-user-a" }))
                .Succeeded.Should().BeTrue();
            (await seedManagers.UserManager.CreateAsync(
                new ApplicationUser { Id = secondUserId, UserName = "known-constraint-user-b" }))
                .Succeeded.Should().BeTrue();
            seedContext.ExternalIdentityBindings.Add(new ExternalIdentityBinding
            {
                Id = Guid.NewGuid(),
                UserId = firstUserId,
                Provider = ExternalIdentityProviders.Cloud,
                TenantId = CloudOidcIdentityProfile.DefaultTenantId,
                ExternalUserId = "known-constraint-subject",
                AccountEnabledSnapshot = true,
                EmployeeActiveSnapshot = true,
                LastLoginAtUtc = DateTime.UtcNow,
                LastSyncAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            seedContext.Roles.Add(new IdentityRole<Guid>
            {
                Id = Guid.NewGuid(),
                Name = "ConstraintRole",
                NormalizedName = "CONSTRAINTROLE"
            });
            await seedContext.SaveChangesAsync();
        }

        await using (var knownContext = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            var service = CreateService(database.ConnectionString, knownContext);
            var knownAction = () => service.ExecuteResultAsync(
                _ =>
                {
                    knownContext.ExternalIdentityBindings.Add(new ExternalIdentityBinding
                    {
                        Id = Guid.NewGuid(),
                        UserId = secondUserId,
                        Provider = ExternalIdentityProviders.Cloud,
                        TenantId = CloudOidcIdentityProfile.DefaultTenantId,
                        ExternalUserId = "known-constraint-subject",
                        AccountEnabledSnapshot = true,
                        EmployeeActiveSnapshot = true,
                        LastLoginAtUtc = DateTime.UtcNow,
                        LastSyncAtUtc = DateTime.UtcNow,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    });
                    return Task.FromResult(Result.Success());
                });

            var knownAssertion = await knownAction.Should()
                .ThrowAsync<ExternalIdentityInvariantConflictException>();
            knownAssertion.Which.ConflictKind.Should()
                .Be(ExternalIdentityInvariantConflictKind.ExternalIdentity);
        }

        await using (var unknownContext = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            var service = CreateService(database.ConnectionString, unknownContext);
            var unknownAction = () => service.ExecuteResultAsync(
                _ =>
                {
                    unknownContext.Roles.Add(new IdentityRole<Guid>
                    {
                        Id = Guid.NewGuid(),
                        Name = "constraintrole",
                        NormalizedName = "CONSTRAINTROLE"
                    });
                    return Task.FromResult(Result.Success());
                });

            await unknownAction.Should().ThrowAsync<DbUpdateException>();
        }
    }

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
                CreateDelegationToken(),
                password),
            CancellationToken.None);
        await firstLockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondTask = secondHandler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(secondUserName),
                CreateDelegationToken(),
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
        binding.ExternalUserId.Should().Be(NormalizeCloudSubject("shared-cloud-subject"));
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcExistingAccountConfirmed" &&
            entry.Result == AuditResults.Succeeded)).Should().Be(1);
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcExternalIdentityBoundToDifferentUser" &&
            entry.Result == AuditResults.Rejected)).Should().Be(1);
    }

    [Fact]
    public async Task ConfirmExistingCloudOidcAccount_ShouldSerializeRealPostgresUserProviderConflict()
    {
        await using var database = await CreateMigratedDatabaseAsync(fixture);
        var userId = Guid.NewGuid();
        const string userName = "E1001";
        const string password = "ValidPassword123!";
        const string firstExternalUserId = "cloud-subject-1";
        const string secondExternalUserId = "cloud-subject-2";

        await using (var seedContext = new IdentityStoreDbContext(
                         CreateIdentityOptions(database.ConnectionString)))
        {
            using var seedManagers = IdentityManagerTestScope.Create(seedContext);
            (await seedManagers.UserManager.CreateAsync(
                new ApplicationUser { Id = userId, UserName = userName },
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
                CreateProfile(userName, firstExternalUserId),
                CreateDelegationToken(firstExternalUserId),
                password),
            CancellationToken.None);
        await firstLockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondTask = secondHandler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(userName, secondExternalUserId),
                CreateDelegationToken(secondExternalUserId),
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
        binding.UserId.Should().Be(userId);
        binding.ExternalUserId.Should().Be(NormalizeCloudSubject(firstExternalUserId));
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcExistingAccountConfirmed" &&
            entry.Result == AuditResults.Succeeded)).Should().Be(1);
        (await verification.AuditLogs.CountAsync(entry =>
            entry.ActionCode == "Identity.CloudOidcLocalUserBoundToDifferentIdentity" &&
            entry.Result == AuditResults.Rejected)).Should().Be(1);
    }

    private static ConfirmExistingCloudOidcAccountCommandHandler CreateHandler(
        string connectionString,
        IdentityStoreDbContext dbContext,
        IdentityManagerTestScope managers,
        IExternalIdentityBindingInvariantGuard invariantGuard,
        IJwtTokenGenerator? tokenGenerator = null)
    {
        return new ConfirmExistingCloudOidcAccountCommandHandler(
            managers.UserManager,
            new ExternalIdentityBindingStore(dbContext),
            new IdentityUserFreshReadStore(dbContext),
            invariantGuard,
            new IdentityAuditLogWriter(dbContext),
            tokenGenerator ?? new StubJwtTokenGenerator(),
            new CloudDelegationGrantStore(
                dbContext,
                DelegationDataProtectionProvider),
            CreateService(connectionString, dbContext));
    }

    private static FinalizeCloudOidcLoginCommandHandler CreateFinalizeHandler(
        string connectionString,
        IdentityStoreDbContext dbContext,
        IdentityManagerTestScope managers,
        IExternalIdentityBindingInvariantGuard invariantGuard,
        IJwtTokenGenerator? tokenGenerator = null)
    {
        return new FinalizeCloudOidcLoginCommandHandler(
            managers.UserManager,
            managers.RoleManager,
            new ExternalIdentityBindingStore(dbContext),
            new IdentityUserFreshReadStore(dbContext),
            invariantGuard,
            new IdentityAuditLogWriter(dbContext),
            tokenGenerator ?? new StubJwtTokenGenerator(),
            new CloudDelegationGrantStore(
                dbContext,
                DelegationDataProtectionProvider),
            Options.Create(new CloudOidcCanonicalAdminOptions()),
            CreateService(connectionString, dbContext));
    }

    private static async Task SeedRolesAsync(
        string connectionString,
        params string[] roleNames)
    {
        await using var context = new IdentityStoreDbContext(
            CreateIdentityOptions(connectionString));
        using var managers = IdentityManagerTestScope.Create(context);
        foreach (var roleName in roleNames.Distinct(StringComparer.Ordinal))
        {
            if (!await managers.RoleManager.RoleExistsAsync(roleName))
            {
                (await managers.RoleManager.CreateAsync(new IdentityRole<Guid>(roleName)))
                    .Succeeded.Should().BeTrue();
            }
        }
    }

    private static async Task<Guid> SeedAdminAsync(
        string connectionString,
        string userName,
        bool hasPassword = true)
    {
        return await SeedIdentityUserAsync(
            connectionString,
            userName,
            IdentityRoleNames.Admin,
            hasPassword: hasPassword);
    }

    private static async Task<Guid> SeedIdentityUserAsync(
        string connectionString,
        string userName,
        string roleName,
        string? externalUserId = null,
        bool hasPassword = true)
    {
        await SeedRolesAsync(
            connectionString,
            roleName,
            IdentityRoleNames.User);
        await using var context = new IdentityStoreDbContext(
            CreateIdentityOptions(connectionString));
        using var managers = IdentityManagerTestScope.Create(context);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var createResult = hasPassword
            ? await managers.UserManager.CreateAsync(user, "ValidPassword123!")
            : await managers.UserManager.CreateAsync(user);
        createResult.Succeeded.Should().BeTrue();
        (await managers.UserManager.AddToRoleAsync(user, roleName))
            .Succeeded.Should().BeTrue();

        if (externalUserId is not null)
        {
            var now = DateTime.UtcNow;
            context.ExternalIdentityBindings.Add(new ExternalIdentityBinding
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Provider = ExternalIdentityProviders.Cloud,
                TenantId = CloudOidcIdentityProfile.DefaultTenantId,
                ExternalUserId = NormalizeCloudSubject(externalUserId),
                AccountEnabledSnapshot = true,
                EmployeeActiveSnapshot = true,
                LastLoginAtUtc = now,
                LastSyncAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            await context.SaveChangesAsync();
        }

        return user.Id;
    }

    private static async Task SetSecurityStampAsync(
        string connectionString,
        Guid userId,
        string? securityStamp)
    {
        await using var context = new IdentityStoreDbContext(
            CreateIdentityOptions(connectionString));
        var user = await context.Users.SingleAsync(item => item.Id == userId);
        user.SecurityStamp = securityStamp;
        await context.SaveChangesAsync();
    }

    private static CloudOidcIdentityProfile CreateProfile(
        string employeeNo,
        string externalUserId = "shared-cloud-subject")
    {
        return new CloudOidcIdentityProfile(
            "https://cloud.example.com",
            NormalizeCloudSubject(externalUserId),
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

    private static CloudDelegationTokenInput CreateDelegationToken(
        string externalUserId = "shared-cloud-subject")
    {
        var subject = NormalizeCloudSubject(externalUserId);
        return new CloudDelegationTokenInput(
            "persistence-test-delegated-token",
            DateTime.UtcNow.AddMinutes(CloudDelegationDefaults.LifetimeMinutes),
            new CloudDelegationTokenEvidence(
                "https://cloud.example.com",
                subject,
                subject,
                CloudOidcIdentityProfile.DefaultTenantId,
                CloudDelegationDefaults.Audience,
                CloudDelegationDefaults.Actor,
                [CloudDelegationDefaults.Scope]));
    }

    private static string NormalizeCloudSubject(string value)
    {
        if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
        {
            return parsed.ToString("D");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
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

    private sealed class RecordingJwtTokenGenerator : IJwtTokenGenerator
    {
        public JwtTokenUser? User { get; private set; }

        public Task<string> GenerateTokenAsync(
            JwtTokenUser user,
            CancellationToken cancellationToken = default)
        {
            User = user;
            return Task.FromResult($"token-{user.UserName}");
        }
    }

    private sealed class HoldingInvariantGuard(
        IExternalIdentityBindingInvariantGuard inner,
        TaskCompletionSource<bool> acquired,
        TaskCompletionSource<bool> release) : IExternalIdentityBindingInvariantGuard
    {
        public async Task AcquireAsync(
            ExternalIdentityBindingInvariantScope scope,
            CancellationToken cancellationToken = default)
        {
            await inner.AcquireAsync(scope, cancellationToken);
            acquired.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class SignalingInvariantGuard(
        IExternalIdentityBindingInvariantGuard inner,
        TaskCompletionSource<bool> attempted) : IExternalIdentityBindingInvariantGuard
    {
        public Task AcquireAsync(
            ExternalIdentityBindingInvariantScope scope,
            CancellationToken cancellationToken = default)
        {
            attempted.TrySetResult(true);
            return inner.AcquireAsync(scope, cancellationToken);
        }
    }
}
