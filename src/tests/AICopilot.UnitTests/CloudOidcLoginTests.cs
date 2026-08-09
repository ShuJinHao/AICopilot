using System.Security.Claims;
using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Commands;
using AICopilot.IdentityService.Services;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AICopilot.UnitTests;

public sealed class CloudOidcLoginTests
{
    private const string DefaultCloudUserId = "10000000-0000-0000-0000-000000000001";
    private const string CanonicalAdminCloudUserId = "10000000-0000-0000-0000-000000101650";
    private const string PreviousCanonicalAdminCloudUserId = "20000000-0000-0000-0000-000000101650";

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldJitCreateUserBinding_AndIssueLocalAiToken()
    {
        var userManager = new InMemoryUserManager();
        var roleManager = new InMemoryRoleManager(IdentityRoleNames.User);
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var tokenGenerator = new RecordingJwtTokenGenerator();
        var handler = CreateHandler(userManager, roleManager, bindingStore, auditWriter, tokenGenerator);

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(),
                CreateDelegationToken()),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        result.Value!.UserName.Should().Be("E0001");
        result.Value.Token.Should().Be("ai-token");
        userManager.StoredUsers.Should().ContainSingle(user => user.UserName == "E0001");
        userManager.GetAssignedRoles("E0001").Should().BeEquivalentTo(IdentityRoleNames.User);
        bindingStore.Bindings.Should().ContainSingle(binding =>
            binding.Provider == ExternalIdentityProviders.Cloud &&
            binding.TenantId == CloudOidcIdentityProfile.DefaultTenantId &&
            binding.ExternalUserId == DefaultCloudUserId &&
            binding.EmployeeNo == "E0001");
        tokenGenerator.LastUser.Should().NotBeNull();
        tokenGenerator.LastUser!.Roles.Should().BeEquivalentTo(IdentityRoleNames.User);
        tokenGenerator.LastUser.Claims.Should().Contain(claim =>
            claim.Type == ExternalIdentityJwtClaimTypes.IdentityProvider &&
            claim.Value == ExternalIdentityProviders.Cloud);
        tokenGenerator.LastUser.Claims.Should().Contain(claim =>
            claim.Type == ExternalIdentityJwtClaimTypes.CloudUserId &&
            claim.Value == DefaultCloudUserId);
        var delegationClaim = tokenGenerator.LastUser.Claims.Should()
            .ContainSingle(claim =>
                claim.Type == ExternalIdentityJwtClaimTypes.CloudDelegationId)
            .Which;
        Guid.TryParse(delegationClaim.Value, out var delegationId).Should().BeTrue();
        delegationId.Should().NotBeEmpty();
        tokenGenerator.LastUser.ExpiresAtUtc.Should().NotBeNull();
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudOidcFirstBind" &&
            request.Result == AuditResults.Succeeded);
        auditWriter.Requests.Single().Metadata.Should().Contain(new KeyValuePair<string, string>(
            "cloudStatusVersion",
            "v1"));
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldRequirePasswordConfirmation_WhenEnabledSameNameLocalUserExists()
    {
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "E0001",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(existingUser);
        userManager.SetPassword(existingUser, "Local-Password-1!");
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User),
            new InMemoryExternalIdentityBindingStore(),
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator());

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(),
                CreateDelegationToken()),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unauthorized);
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should()
            .Be(AuthProblemCodes.ExternalIdentityConfirmationRequired);
        userManager.StoredUsers.Should().ContainSingle(user => user.Id == existingUser.Id);
    }

    [Fact]
    public async Task ConfirmExistingCloudOidcAccount_ShouldBindWithLocalPassword_AndPreserveLocalRoles()
    {
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "E0001",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var originalSecurityStamp = existingUser.SecurityStamp;
        var userManager = new InMemoryUserManager(existingUser);
        userManager.SetPassword(existingUser, "Local-Password-1!");
        await userManager.AddToRoleAsync(existingUser, IdentityRoleNames.Admin);
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var tokenGenerator = new RecordingJwtTokenGenerator();
        var handler = CreateConfirmHandler(
            userManager,
            bindingStore,
            auditWriter,
            tokenGenerator);

        var result = await handler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(),
                CreateDelegationToken(),
                "Local-Password-1!"),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        result.Value!.UserName.Should().Be("E0001");
        bindingStore.Bindings.Should().ContainSingle(binding =>
            binding.UserId == existingUser.Id &&
            binding.ExternalUserId == DefaultCloudUserId);
        tokenGenerator.LastUser!.Roles.Should().BeEquivalentTo(IdentityRoleNames.Admin);
        existingUser.SecurityStamp.Should().Be(originalSecurityStamp);
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudOidcExistingAccountConfirmed" &&
            request.Result == AuditResults.Succeeded);
    }

    [Fact]
    public async Task ConfirmExistingCloudOidcAccount_ShouldRetainNoBinding_WhenPasswordIsInvalid()
    {
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "E0001",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(existingUser);
        userManager.SetPassword(existingUser, "Local-Password-1!");
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var handler = CreateConfirmHandler(
            userManager,
            bindingStore,
            auditWriter,
            new RecordingJwtTokenGenerator());

        var result = await handler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(),
                CreateDelegationToken(),
                "wrong-password"),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unauthorized);
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should()
            .Be(AuthProblemCodes.InvalidCredentials);
        bindingStore.Bindings.Should().BeEmpty();
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudOidcExistingAccountPasswordRejected" &&
            request.Result == AuditResults.Rejected);
    }

    [Fact]
    public async Task ConfirmExistingCloudOidcAccount_ShouldRejectPasswordlessAccountWithoutRetainingARecoverableState()
    {
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "E0001",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(existingUser);
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var handler = CreateConfirmHandler(
            userManager,
            bindingStore,
            auditWriter,
            new RecordingJwtTokenGenerator());

        var result = await handler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(),
                CreateDelegationToken(),
                "irrelevant-password"),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unauthorized);
        var problem = result.Errors!.OfType<ApiProblemDescriptor>().Single();
        problem.Code.Should().Be(AuthProblemCodes.ExternalIdentityConflict);
        problem.Detail.Should().Contain("没有可用于确认的密码");
        bindingStore.Bindings.Should().BeEmpty();
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudOidcExistingAccountHasNoPassword" &&
            request.Result == AuditResults.Rejected);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldLockNormalizedUserNameAndProspectiveUserId()
    {
        var userManager = new InMemoryUserManager();
        var guard = new RecordingExternalIdentityBindingInvariantGuard();
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User),
            new InMemoryExternalIdentityBindingStore(),
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator(),
            invariantGuard: guard);

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(preferredUserName: "e0001", employeeNo: "e0001"),
                CreateDelegationToken()),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        var scope = guard.Scopes.Should().ContainSingle().Which;
        scope.Provider.Should().Be(ExternalIdentityProviders.Cloud);
        scope.NormalizedUserName.Should().Be("E0001");
        scope.KnownUserIds.Should().ContainSingle().Which.Should()
            .Be(userManager.StoredUsers.Single().Id);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldMapOnlyKnownUniquenessFailuresToConflict()
    {
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var transactionService = new KnownConflictTransactionalExecutionService(
            ExternalIdentityInvariantConflictKind.NormalizedUserName);
        var handler = CreateHandler(
            new InMemoryUserManager(),
            new InMemoryRoleManager(IdentityRoleNames.User),
            new InMemoryExternalIdentityBindingStore(),
            auditWriter,
            new RecordingJwtTokenGenerator(),
            transactionService: transactionService);

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(),
                CreateDelegationToken()),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unauthorized);
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should()
            .Be(AuthProblemCodes.ExternalIdentityConflict);
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudOidcNormalizedUserNameConflict" &&
            request.Result == AuditResults.Rejected);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldNotDisguiseUnknownTransactionFailureAsConflict()
    {
        var failure = new InvalidOperationException("identity database unavailable");
        var handler = CreateHandler(
            new InMemoryUserManager(),
            new InMemoryRoleManager(IdentityRoleNames.User),
            new InMemoryExternalIdentityBindingStore(),
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator(),
            transactionService: new FailingTransactionalExecutionService(failure));

        var action = () => handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(),
                CreateDelegationToken()),
            CancellationToken.None);

        var assertion = await action.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(failure);
    }

    [Theory]
    [InlineData(
        CloudOidcExternalSessionAuditReason.Cancelled,
        "Identity.CloudOidcConfirmationCancelled")]
    [InlineData(
        CloudOidcExternalSessionAuditReason.InvalidOrExpired,
        "Identity.CloudOidcExternalSessionInvalid")]
    public async Task AuditCloudOidcExternalSession_ShouldWriteCredentialFreeStructuredRejection(
        CloudOidcExternalSessionAuditReason reason,
        string expectedActionCode)
    {
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var handler = new AuditCloudOidcExternalSessionCommandHandler(
            auditWriter,
            new InlineTransactionalExecutionService());

        var result = await handler.Handle(
            new AuditCloudOidcExternalSessionCommand(reason, CreateProfile()),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        var audit = auditWriter.Requests.Should().ContainSingle().Which;
        audit.ActionCode.Should().Be(expectedActionCode);
        audit.Result.Should().Be(AuditResults.Rejected);
        audit.Metadata.Should().ContainKey("cloudUserId").WhoseValue.Should().Be(DefaultCloudUserId);
        var serializedAudit = System.Text.Json.JsonSerializer.Serialize(audit);
        serializedAudit.Contains("password", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        serializedAudit.Contains("token", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        serializedAudit.Contains("cookie=", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmExistingCloudOidcAccount_ShouldReuseExactBindingIdempotently()
    {
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "E0001",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(existingUser);
        userManager.SetPassword(existingUser, "Local-Password-1!");
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        await bindingStore.CreateAsync(new CreateExternalIdentityBindingRequest(
            existingUser.Id,
            ExternalIdentityProviders.Cloud,
            CloudOidcIdentityProfile.DefaultTenantId,
            DefaultCloudUserId,
            "employee-1",
            "E0001",
            "张三",
            "D001",
            "制造一部",
            "old-version",
            AccountEnabledSnapshot: true,
            EmployeeActiveSnapshot: true,
            DateTime.UtcNow));
        var handler = CreateConfirmHandler(
            userManager,
            bindingStore,
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator());

        var result = await handler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(),
                CreateDelegationToken(),
                "Local-Password-1!"),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        bindingStore.Bindings.Should().ContainSingle();
        bindingStore.Bindings.Single().StatusVersion.Should().Be("v1");
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldBindExistingCanonicalAdminWithoutPassword()
    {
        var existingAdmin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "101650",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(existingAdmin);
        await userManager.AddToRoleAsync(existingAdmin, IdentityRoleNames.Admin);
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var tokenGenerator = new RecordingJwtTokenGenerator();
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User, IdentityRoleNames.Admin),
            bindingStore,
            auditWriter,
            tokenGenerator);

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(
                    subject: CanonicalAdminCloudUserId,
                    preferredUserName: "101650",
                    employeeNo: "101650",
                    employeeId: "employee-admin"),
                CreateDelegationToken(CanonicalAdminCloudUserId)),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        result.Value!.UserName.Should().Be("101650");
        userManager.StoredUsers.Should().ContainSingle(user => user.Id == existingAdmin.Id);
        bindingStore.Bindings.Should().ContainSingle(binding =>
            binding.UserId == existingAdmin.Id &&
            binding.Provider == ExternalIdentityProviders.Cloud &&
            binding.TenantId == CloudOidcIdentityProfile.DefaultTenantId &&
            binding.ExternalUserId == CanonicalAdminCloudUserId &&
            binding.EmployeeNo == "101650");
        tokenGenerator.LastUser.Should().NotBeNull();
        tokenGenerator.LastUser!.Roles.Should().BeEquivalentTo(IdentityRoleNames.Admin);
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudOidcCanonicalAdminCollected" &&
            request.Result == AuditResults.Succeeded &&
            request.TargetId == existingAdmin.Id.ToString());
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldRejectPasswordBearingLegacyCanonicalEmergencyAccount()
    {
        var legacyEmergencyAdmin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = CloudOidcCanonicalAdminOptions.RequiredEmployeeNo,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(legacyEmergencyAdmin);
        userManager.SetPassword(legacyEmergencyAdmin, "Legacy-Emergency-Password-1!");
        await userManager.AddToRoleAsync(legacyEmergencyAdmin, IdentityRoleNames.Admin);
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var tokenGenerator = new RecordingJwtTokenGenerator();
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User, IdentityRoleNames.Admin),
            bindingStore,
            auditWriter,
            tokenGenerator);

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(
                    subject: CanonicalAdminCloudUserId,
                    preferredUserName: CloudOidcCanonicalAdminOptions.RequiredEmployeeNo,
                    employeeNo: CloudOidcCanonicalAdminOptions.RequiredEmployeeNo,
                    employeeId: "employee-admin"),
                CreateDelegationToken(CanonicalAdminCloudUserId)),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unauthorized);
        var problem = result.Errors!.OfType<ApiProblemDescriptor>().Single();
        problem.Code.Should().Be(
            AuthProblemCodes.EmergencyAdminCanonicalCloudAdminConflict);
        problem.Detail.Should().Contain(
            CloudOidcCanonicalAdminOptions.EmergencyAdminConflictReasonCode);
        bindingStore.Bindings.Should().BeEmpty();
        tokenGenerator.LastUser.Should().BeNull();
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudOidcCanonicalAdminEmergencyCollision" &&
            request.Result == AuditResults.Rejected);
    }

    [Fact]
    public async Task ConfirmExistingCloudOidcAccount_ShouldRejectCanonicalEmergencyPasswordPath()
    {
        var legacyEmergencyAdmin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = CloudOidcCanonicalAdminOptions.RequiredEmployeeNo,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(legacyEmergencyAdmin);
        userManager.SetPassword(legacyEmergencyAdmin, "Legacy-Emergency-Password-1!");
        await userManager.AddToRoleAsync(legacyEmergencyAdmin, IdentityRoleNames.Admin);
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var tokenGenerator = new RecordingJwtTokenGenerator();
        var handler = CreateConfirmHandler(
            userManager,
            bindingStore,
            auditWriter,
            tokenGenerator);

        var result = await handler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(
                    subject: CanonicalAdminCloudUserId,
                    preferredUserName: CloudOidcCanonicalAdminOptions.RequiredEmployeeNo,
                    employeeNo: CloudOidcCanonicalAdminOptions.RequiredEmployeeNo,
                    employeeId: "employee-admin"),
                CreateDelegationToken(CanonicalAdminCloudUserId),
                "Legacy-Emergency-Password-1!"),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unauthorized);
        var problem = result.Errors!.OfType<ApiProblemDescriptor>().Single();
        problem.Code.Should().Be(
            AuthProblemCodes.EmergencyAdminCanonicalCloudAdminConflict);
        problem.Detail.Should().Contain(
            CloudOidcCanonicalAdminOptions.EmergencyAdminConflictReasonCode);
        bindingStore.Bindings.Should().BeEmpty();
        tokenGenerator.LastUser.Should().BeNull();
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudOidcCanonicalAdminEmergencyCollision" &&
            request.Result == AuditResults.Rejected);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldPromoteAndBindExistingCanonicalOrdinaryUser()
    {
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "101650",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(existingUser);
        await userManager.AddToRoleAsync(existingUser, IdentityRoleNames.User);
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User, IdentityRoleNames.Admin),
            bindingStore,
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator());

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(
                    subject: CanonicalAdminCloudUserId,
                    preferredUserName: "101650",
                    employeeNo: "101650",
                    employeeId: "employee-admin"),
                CreateDelegationToken(CanonicalAdminCloudUserId)),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        userManager.GetAssignedRoles("101650").Should().BeEquivalentTo(
            IdentityRoleNames.User,
            IdentityRoleNames.Admin);
        bindingStore.Bindings.Should().ContainSingle(binding =>
            binding.UserId == existingUser.Id &&
            binding.ExternalUserId == CanonicalAdminCloudUserId);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldRejectCanonicalAdminWhenBoundToDifferentCloudSubject()
    {
        var existingAdmin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "101650",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(existingAdmin);
        await userManager.AddToRoleAsync(existingAdmin, IdentityRoleNames.Admin);
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        await bindingStore.CreateAsync(new CreateExternalIdentityBindingRequest(
            existingAdmin.Id,
            ExternalIdentityProviders.Cloud,
            CloudOidcIdentityProfile.DefaultTenantId,
            PreviousCanonicalAdminCloudUserId,
            "employee-admin",
            "101650",
            "管理员",
            "D001",
            "制造一部",
            "v1",
            AccountEnabledSnapshot: true,
            EmployeeActiveSnapshot: true,
            DateTime.UtcNow));
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User, IdentityRoleNames.Admin),
            bindingStore,
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator());

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(
                    subject: CanonicalAdminCloudUserId,
                    preferredUserName: "101650",
                    employeeNo: "101650",
                    employeeId: "employee-admin"),
                CreateDelegationToken(CanonicalAdminCloudUserId)),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unauthorized);
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should().Be(AuthProblemCodes.ExternalIdentityConflict);
        bindingStore.Bindings.Should().ContainSingle(binding =>
            binding.ExternalUserId == PreviousCanonicalAdminCloudUserId);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldCreateCanonicalAdminWhenMissing()
    {
        var userManager = new InMemoryUserManager();
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User, IdentityRoleNames.Admin),
            bindingStore,
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator());

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(
                    subject: CanonicalAdminCloudUserId,
                    preferredUserName: "101650",
                    employeeNo: "101650",
                    employeeId: "employee-admin"),
                CreateDelegationToken(CanonicalAdminCloudUserId)),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        userManager.StoredUsers.Should().ContainSingle(user => user.UserName == "101650");
        userManager.GetAssignedRoles("101650").Should().BeEquivalentTo(
            IdentityRoleNames.Admin);
        bindingStore.Bindings.Should().ContainSingle(binding =>
            binding.ExternalUserId == CanonicalAdminCloudUserId &&
            binding.EmployeeNo == "101650");
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldRejectDisabledCanonicalLocalAccount()
    {
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "101650",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = true,
            LockoutEnd = DateTimeOffset.MaxValue
        };
        var userManager = new InMemoryUserManager(existingUser);
        await userManager.AddToRoleAsync(existingUser, IdentityRoleNames.Admin);
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User, IdentityRoleNames.Admin),
            bindingStore,
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator());

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(
                    subject: CanonicalAdminCloudUserId,
                    preferredUserName: "101650",
                    employeeNo: "101650",
                    employeeId: "employee-admin"),
                CreateDelegationToken(CanonicalAdminCloudUserId)),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unauthorized);
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should()
            .Be(AuthProblemCodes.AccountDisabled);
        bindingStore.Bindings.Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldReuseCanonicalBindingIdempotently()
    {
        var userManager = new InMemoryUserManager();
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var grantStore = new InMemoryCloudDelegationGrantStore();
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User, IdentityRoleNames.Admin),
            bindingStore,
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator(),
            grantStore: grantStore);
        var profile = CreateProfile(
            subject: CanonicalAdminCloudUserId,
            preferredUserName: "101650",
            employeeNo: "101650",
            employeeId: "employee-admin");

        var first = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(profile, CreateDelegationToken(profile.Subject)),
            CancellationToken.None);
        var second = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(profile, CreateDelegationToken(profile.Subject)),
            CancellationToken.None);

        first.Status.Should().Be(ResultStatus.Ok);
        second.Status.Should().Be(ResultStatus.Ok);
        userManager.StoredUsers.Should().ContainSingle(user => user.UserName == "101650");
        bindingStore.Bindings.Should().ContainSingle(binding =>
            binding.ExternalUserId == CanonicalAdminCloudUserId);
        grantStore.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldNotIssueAiTokenWhenDelegationPersistenceFails()
    {
        var tokenGenerator = new RecordingJwtTokenGenerator();
        var failure = new InvalidOperationException("delegation store unavailable");
        var handler = CreateHandler(
            new InMemoryUserManager(),
            new InMemoryRoleManager(IdentityRoleNames.User),
            new InMemoryExternalIdentityBindingStore(),
            new InMemoryIdentityAuditLogWriter(),
            tokenGenerator,
            grantStore: new InMemoryCloudDelegationGrantStore(failure));

        var action = () => handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(),
                CreateDelegationToken()),
            CancellationToken.None);

        var assertion = await action.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(failure);
        tokenGenerator.LastUser.Should().BeNull();
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("subject")]
    [InlineData("delegated-user")]
    [InlineData("tenant")]
    [InlineData("audience")]
    [InlineData("actor")]
    [InlineData("scope")]
    public async Task FinalizeCloudOidcLogin_ShouldRejectMismatchedDelegationEvidenceBeforeMutation(
        string mismatch)
    {
        var profile = CreateProfile();
        var validEvidence = CreateDelegationToken().Evidence;
        var invalidEvidence = mismatch switch
        {
            "issuer" => validEvidence with { Issuer = "https://other-cloud.example.com" },
            "subject" => validEvidence with { Subject = Guid.NewGuid().ToString("D") },
            "delegated-user" => validEvidence with { DelegatedUserId = Guid.NewGuid().ToString("D") },
            "tenant" => validEvidence with { TenantId = "other-tenant" },
            "audience" => validEvidence with { Audience = "other-audience" },
            "actor" => validEvidence with { Actor = "other-actor" },
            "scope" => validEvidence with { Scopes = ["other.scope"] },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };
        var userManager = new InMemoryUserManager();
        var grantStore = new InMemoryCloudDelegationGrantStore();
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User),
            new InMemoryExternalIdentityBindingStore(),
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator(),
            grantStore: grantStore);

        var action = () => handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                profile,
                CreateDelegationToken(evidence: invalidEvidence)),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*delegation evidence*");
        userManager.StoredUsers.Should().BeEmpty();
        grantStore.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldCapDelegationAtThirtyMinutes()
    {
        var grantStore = new InMemoryCloudDelegationGrantStore();
        var handler = CreateHandler(
            new InMemoryUserManager(),
            new InMemoryRoleManager(IdentityRoleNames.User),
            new InMemoryExternalIdentityBindingStore(),
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator(),
            grantStore: grantStore);
        var startedAtUtc = DateTime.UtcNow;

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(),
                CreateDelegationToken(expiresAtUtc: startedAtUtc.AddHours(8))),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        grantStore.Requests.Should().ContainSingle();
        grantStore.Requests[0].ExpiresAtUtc.Should()
            .BeOnOrBefore(startedAtUtc.AddMinutes(CloudDelegationDefaults.LifetimeMinutes).AddSeconds(1));
    }

    [Fact]
    public async Task ConfirmExistingCloudOidcAccount_ShouldValidateAndCapDelegationBeforePersistingGrant()
    {
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "E0001",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(existingUser);
        userManager.SetPassword(existingUser, "Local-Password-1!");
        var grantStore = new InMemoryCloudDelegationGrantStore();
        var handler = CreateConfirmHandler(
            userManager,
            new InMemoryExternalIdentityBindingStore(),
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator(),
            grantStore: grantStore);
        var startedAtUtc = DateTime.UtcNow;

        var result = await handler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(),
                CreateDelegationToken(expiresAtUtc: startedAtUtc.AddHours(8)),
                "Local-Password-1!"),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        grantStore.Requests.Should().ContainSingle();
        grantStore.Requests[0].ExpiresAtUtc.Should()
            .BeOnOrBefore(startedAtUtc.AddMinutes(CloudDelegationDefaults.LifetimeMinutes).AddSeconds(1));
    }

    [Fact]
    public async Task ConfirmExistingCloudOidcAccount_ShouldRejectMismatchedDelegationBeforePasswordOrBinding()
    {
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "E0001",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(existingUser);
        userManager.SetPassword(existingUser, "Local-Password-1!");
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var grantStore = new InMemoryCloudDelegationGrantStore();
        var invalidEvidence = CreateDelegationToken().Evidence with
        {
            Audience = "wrong-audience"
        };
        var handler = CreateConfirmHandler(
            userManager,
            bindingStore,
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator(),
            grantStore: grantStore);

        var action = () => handler.Handle(
            new ConfirmExistingCloudOidcAccountCommand(
                CreateProfile(),
                CreateDelegationToken(evidence: invalidEvidence),
                "Local-Password-1!"),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*delegation evidence*");
        bindingStore.Bindings.Should().BeEmpty();
        grantStore.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldReject_WhenCloudIdentityIsInactive()
    {
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var userManager = new InMemoryUserManager();
        var transactionService = new InlineTransactionalExecutionService();
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User),
            new InMemoryExternalIdentityBindingStore(),
            auditWriter,
            new RecordingJwtTokenGenerator(),
            transactionService: transactionService);

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(
                CreateProfile(accountEnabled: false),
                CreateDelegationToken()),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unauthorized);
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should().Be(AuthProblemCodes.CloudIdentityInactive);
        userManager.StoredUsers.Should().BeEmpty();
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudOidcAccountDisabled" &&
            request.Result == AuditResults.Rejected);
        auditWriter.Requests.Single().Metadata.Should().Contain(new KeyValuePair<string, string>(
            "rejectionReason",
            "Identity.CloudOidcAccountDisabled"));
        transactionService.ResultExecutionCount.Should().Be(1);
        transactionService.GenericExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task CloudIdentityStatusValidator_ShouldPassAndCache_WhenCloudStatusMatches()
    {
        var userManager = new InMemoryUserManager();
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var user = await CreateBoundCloudUserAsync(userManager, bindingStore);
        var statusClient = new RecordingCloudIdentityStatusClient(
            CloudIdentityStatusCheckResult.Succeeded(CreateStatusSnapshot()));
        var cache = new InMemoryCloudIdentityStatusValidationCache();
        var validator = CreateStatusValidator(userManager, bindingStore, auditWriter, statusClient, cache);

        var firstResult = await validator.ValidateAsync(user, CreateCloudPrincipal(), CancellationToken.None);
        var secondResult = await validator.ValidateAsync(user, CreateCloudPrincipal(), CancellationToken.None);

        firstResult.IsValid.Should().BeTrue();
        secondResult.IsValid.Should().BeTrue();
        statusClient.CallCount.Should().Be(1);
        auditWriter.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CloudIdentityStatusValidator_ShouldRefreshSecurityStamp_WhenCloudAccountDisabled()
    {
        var userManager = new InMemoryUserManager();
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var user = await CreateBoundCloudUserAsync(userManager, bindingStore);
        var previousStamp = user.SecurityStamp;
        var statusClient = new RecordingCloudIdentityStatusClient(
            CloudIdentityStatusCheckResult.Succeeded(CreateStatusSnapshot(accountEnabled: false)));
        var validator = CreateStatusValidator(userManager, bindingStore, auditWriter, statusClient);

        var result = await validator.ValidateAsync(user, CreateCloudPrincipal(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.FailureCode.Should().Be(AuthProblemCodes.CloudIdentityInactive);
        user.SecurityStamp.Should().NotBe(previousStamp);
        bindingStore.Bindings.Single().AccountEnabledSnapshot.Should().BeFalse();
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudStatusRejected" &&
            request.Result == AuditResults.Rejected &&
            request.Metadata != null &&
            request.Metadata.ContainsKey("cloudStatusVersion"));
        auditWriter.Requests.Single().Metadata.Should().Contain(new KeyValuePair<string, string>(
            "rejectionReason",
            "cloud-account-disabled"));
    }

    [Fact]
    public async Task CloudIdentityStatusValidator_ShouldRejectAndRefreshSecurityStamp_WhenStatusCloudUserMismatchesToken()
    {
        var userManager = new InMemoryUserManager();
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var user = await CreateBoundCloudUserAsync(userManager, bindingStore);
        var previousStamp = user.SecurityStamp;
        var statusClient = new RecordingCloudIdentityStatusClient(
            CloudIdentityStatusCheckResult.Succeeded(CreateStatusSnapshot(cloudUserId: "different-cloud-user")));
        var validator = CreateStatusValidator(userManager, bindingStore, auditWriter, statusClient);

        var result = await validator.ValidateAsync(user, CreateCloudPrincipal(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.FailureCode.Should().Be(AuthProblemCodes.CloudIdentityUnverified);
        user.SecurityStamp.Should().NotBe(previousStamp);
        bindingStore.Bindings.Single().StatusVersion.Should().Be("v1");
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudStatusRejected" &&
            request.Result == AuditResults.Rejected);
        auditWriter.Requests.Single().Metadata.Should().Contain(new KeyValuePair<string, string>(
            "rejectionReason",
            "cloud-status-identity-mismatch"));
    }

    [Fact]
    public async Task CloudIdentityStatusValidator_ShouldRejectAndRefreshSecurityStamp_WhenStatusTenantMismatchesToken()
    {
        var userManager = new InMemoryUserManager();
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var user = await CreateBoundCloudUserAsync(userManager, bindingStore);
        var previousStamp = user.SecurityStamp;
        var statusClient = new RecordingCloudIdentityStatusClient(
            CloudIdentityStatusCheckResult.Succeeded(CreateStatusSnapshot(tenantId: "other-tenant")));
        var validator = CreateStatusValidator(userManager, bindingStore, auditWriter, statusClient);

        var result = await validator.ValidateAsync(user, CreateCloudPrincipal(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.FailureCode.Should().Be(AuthProblemCodes.CloudIdentityUnverified);
        user.SecurityStamp.Should().NotBe(previousStamp);
        bindingStore.Bindings.Single().StatusVersion.Should().Be("v1");
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudStatusRejected" &&
            request.Result == AuditResults.Rejected);
        auditWriter.Requests.Single().Metadata.Should().Contain(new KeyValuePair<string, string>(
            "rejectionReason",
            "cloud-status-identity-mismatch"));
    }

    [Fact]
    public async Task CloudIdentityStatusValidator_ShouldRefreshSecurityStamp_WhenCloudIdentityNotFound()
    {
        var userManager = new InMemoryUserManager();
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var user = await CreateBoundCloudUserAsync(userManager, bindingStore);
        var previousStamp = user.SecurityStamp;
        var statusClient = new RecordingCloudIdentityStatusClient(
            CloudIdentityStatusCheckResult.NotFound("not found"));
        var validator = CreateStatusValidator(userManager, bindingStore, auditWriter, statusClient);

        var result = await validator.ValidateAsync(user, CreateCloudPrincipal(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.FailureCode.Should().Be(AuthProblemCodes.SessionRevoked);
        user.SecurityStamp.Should().NotBe(previousStamp);
        auditWriter.Requests.Single().Metadata.Should().Contain(new KeyValuePair<string, string>(
            "rejectionReason",
            "cloud-identity-not-found"));
    }

    [Fact]
    public async Task CloudIdentityStatusValidator_ShouldRefreshSecurityStamp_WhenCloudEmployeeInactive()
    {
        var userManager = new InMemoryUserManager();
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var user = await CreateBoundCloudUserAsync(userManager, bindingStore);
        var previousStamp = user.SecurityStamp;
        var statusClient = new RecordingCloudIdentityStatusClient(
            CloudIdentityStatusCheckResult.Succeeded(CreateStatusSnapshot(employeeActive: false)));
        var validator = CreateStatusValidator(userManager, bindingStore, auditWriter, statusClient);

        var result = await validator.ValidateAsync(user, CreateCloudPrincipal(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.FailureCode.Should().Be(AuthProblemCodes.CloudIdentityInactive);
        user.SecurityStamp.Should().NotBe(previousStamp);
        bindingStore.Bindings.Single().EmployeeActiveSnapshot.Should().BeFalse();
        auditWriter.Requests.Single().Metadata.Should().Contain(new KeyValuePair<string, string>(
            "rejectionReason",
            "cloud-employee-inactive"));
    }

    [Fact]
    public async Task CloudIdentityStatusValidator_ShouldRefreshSecurityStamp_WhenStatusVersionChanged()
    {
        var userManager = new InMemoryUserManager();
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var user = await CreateBoundCloudUserAsync(userManager, bindingStore);
        var previousStamp = user.SecurityStamp;
        var statusClient = new RecordingCloudIdentityStatusClient(
            CloudIdentityStatusCheckResult.Succeeded(CreateStatusSnapshot(statusVersion: "v2")));
        var validator = CreateStatusValidator(userManager, bindingStore, auditWriter, statusClient);

        var result = await validator.ValidateAsync(user, CreateCloudPrincipal(statusVersion: "v1"), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.FailureCode.Should().Be(AuthProblemCodes.SessionRevoked);
        user.SecurityStamp.Should().NotBe(previousStamp);
        bindingStore.Bindings.Single().StatusVersion.Should().Be("v2");
        auditWriter.Requests.Single().Metadata.Should().Contain(new KeyValuePair<string, string>(
            "rejectionReason",
            "cloud-status-version-changed"));
    }

    [Fact]
    public async Task CloudIdentityStatusValidator_ShouldRejectWithoutRevoking_WhenCloudUnavailableAndNoCache()
    {
        var userManager = new InMemoryUserManager();
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var user = await CreateBoundCloudUserAsync(userManager, bindingStore);
        var previousStamp = user.SecurityStamp;
        var statusClient = new RecordingCloudIdentityStatusClient(
            CloudIdentityStatusCheckResult.Unavailable("Cloud unavailable"));
        var validator = CreateStatusValidator(userManager, bindingStore, auditWriter, statusClient);

        var result = await validator.ValidateAsync(user, CreateCloudPrincipal(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.FailureCode.Should().Be(AuthProblemCodes.CloudIdentityUnverified);
        user.SecurityStamp.Should().Be(previousStamp);
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudStatusRejected" &&
            request.Result == AuditResults.Rejected);
        auditWriter.Requests.Single().Metadata.Should().Contain(new KeyValuePair<string, string>(
            "rejectionReason",
            "cloud-status-unavailable"));
    }

    [Fact]
    public void CloudIdentityStatusOptions_ShouldRequireExplicitProductionIntent_WhenCloudOidcEnabled()
    {
        var options = new CloudIdentityStatusOptions { Enabled = false };

        Action act = () => options.EnsureValid(
            environmentName: "Production",
            cloudOidcEnabled: true,
            enabledWasExplicitlyConfigured: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CloudIdentityStatus:Enabled*");
    }

    [Fact]
    public void CloudIdentityStatusOptions_ShouldAllowExplicitDisabledProductionIntent()
    {
        var options = new CloudIdentityStatusOptions { Enabled = false };

        options.EnsureValid(
            environmentName: "Production",
            cloudOidcEnabled: true,
            enabledWasExplicitlyConfigured: true);
    }

    [Fact]
    public void CloudOidcCanonicalAdminOptions_ShouldRejectAnyNonCanonicalEmployeeNumber()
    {
        var options = new CloudOidcCanonicalAdminOptions
        {
            CanonicalAdminEmployeeNo = "E0001"
        };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CloudOidc:CanonicalAdminEmployeeNo*");
    }

    private static FinalizeCloudOidcLoginCommandHandler CreateHandler(
        InMemoryUserManager userManager,
        InMemoryRoleManager roleManager,
        InMemoryExternalIdentityBindingStore bindingStore,
        InMemoryIdentityAuditLogWriter auditWriter,
        RecordingJwtTokenGenerator tokenGenerator,
        CloudOidcCanonicalAdminOptions? canonicalAdminOptions = null,
        ITransactionalExecutionService? transactionService = null,
        IExternalIdentityBindingInvariantGuard? invariantGuard = null,
        ICloudDelegationGrantStore? grantStore = null)
    {
        return new FinalizeCloudOidcLoginCommandHandler(
            userManager,
            roleManager,
            bindingStore,
            new InMemoryIdentityUserFreshReadStore(userManager),
            invariantGuard ?? new NoOpExternalIdentityBindingInvariantGuard(),
            auditWriter,
            tokenGenerator,
            grantStore ?? new InMemoryCloudDelegationGrantStore(),
            Options.Create(canonicalAdminOptions ?? new CloudOidcCanonicalAdminOptions()),
            transactionService ?? new InlineTransactionalExecutionService());
    }

    private static ConfirmExistingCloudOidcAccountCommandHandler CreateConfirmHandler(
        InMemoryUserManager userManager,
        InMemoryExternalIdentityBindingStore bindingStore,
        InMemoryIdentityAuditLogWriter auditWriter,
        RecordingJwtTokenGenerator tokenGenerator,
        ITransactionalExecutionService? transactionService = null,
        IExternalIdentityBindingInvariantGuard? invariantGuard = null,
        ICloudDelegationGrantStore? grantStore = null)
    {
        return new ConfirmExistingCloudOidcAccountCommandHandler(
            userManager,
            bindingStore,
            new InMemoryIdentityUserFreshReadStore(userManager),
            invariantGuard ?? new NoOpExternalIdentityBindingInvariantGuard(),
            auditWriter,
            tokenGenerator,
            grantStore ?? new InMemoryCloudDelegationGrantStore(),
            transactionService ?? new InlineTransactionalExecutionService());
    }

    private static CloudIdentityStatusValidator CreateStatusValidator(
        InMemoryUserManager userManager,
        InMemoryExternalIdentityBindingStore bindingStore,
        InMemoryIdentityAuditLogWriter auditWriter,
        RecordingCloudIdentityStatusClient statusClient,
        ICloudIdentityStatusValidationCache? cache = null)
    {
        return new CloudIdentityStatusValidator(
            Options.Create(new CloudIdentityStatusOptions
            {
                Enabled = true,
                BaseUrl = "https://cloud.example.com",
                SigningSecret = "identity-status-signing-secret-32-bytes-minimum",
                RefreshIntervalSeconds = 60,
                TimeoutSeconds = 5
            }),
            statusClient,
            bindingStore,
            userManager,
            auditWriter,
            new InlineTransactionalExecutionService(),
            cache ?? new InMemoryCloudIdentityStatusValidationCache());
    }

    private static CloudOidcIdentityProfile CreateProfile(
        bool accountEnabled = true,
        string subject = DefaultCloudUserId,
        string preferredUserName = "E0001",
        string? employeeNo = "E0001",
        string? employeeId = "employee-1")
    {
        return new CloudOidcIdentityProfile(
            "https://cloud.example.com",
            subject,
            CloudOidcIdentityProfile.DefaultTenantId,
            preferredUserName,
            "张三",
            employeeId,
            employeeNo,
            "D001",
            "制造一部",
            "v1",
            accountEnabled,
            EmployeeActive: true);
    }

    private static CloudDelegationTokenInput CreateDelegationToken(
        string subject = DefaultCloudUserId,
        DateTime? expiresAtUtc = null,
        CloudDelegationTokenEvidence? evidence = null)
    {
        evidence ??= new CloudDelegationTokenEvidence(
            "https://cloud.example.com",
            subject,
            subject,
            CloudOidcIdentityProfile.DefaultTenantId,
            CloudDelegationDefaults.Audience,
            CloudDelegationDefaults.Actor,
            [CloudDelegationDefaults.Scope]);
        return new CloudDelegationTokenInput(
            "delegated-cloud-token",
            expiresAtUtc ?? DateTime.UtcNow.AddMinutes(CloudDelegationDefaults.LifetimeMinutes),
            evidence);
    }

    private static ClaimsPrincipal CreateCloudPrincipal(
        string cloudUserId = "cloud-user-1",
        string statusVersion = "v1",
        string tenantId = CloudOidcIdentityProfile.DefaultTenantId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ExternalIdentityJwtClaimTypes.IdentityProvider, ExternalIdentityProviders.Cloud),
                new Claim(ExternalIdentityJwtClaimTypes.CloudIssuer, "https://cloud.example.com"),
                new Claim(ExternalIdentityJwtClaimTypes.CloudTenantId, tenantId),
                new Claim(ExternalIdentityJwtClaimTypes.CloudUserId, cloudUserId),
                new Claim(ExternalIdentityJwtClaimTypes.CloudEmployeeId, "employee-1"),
                new Claim(ExternalIdentityJwtClaimTypes.CloudEmployeeNo, "E0001"),
                new Claim(ExternalIdentityJwtClaimTypes.CloudStatusVersion, statusVersion)
            ],
            "jwt"));
    }

    private static CloudIdentityStatusSnapshot CreateStatusSnapshot(
        string cloudUserId = "cloud-user-1",
        string statusVersion = "v1",
        bool accountEnabled = true,
        bool employeeActive = true,
        string tenantId = CloudOidcIdentityProfile.DefaultTenantId)
    {
        return new CloudIdentityStatusSnapshot(
            cloudUserId,
            tenantId,
            accountEnabled,
            employeeActive,
            statusVersion,
            DateTime.UtcNow);
    }

    private static async Task<ApplicationUser> CreateBoundCloudUserAsync(
        InMemoryUserManager userManager,
        InMemoryExternalIdentityBindingStore bindingStore,
        string statusVersion = "v1")
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "E0001",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        await userManager.CreateAsync(user);
        await bindingStore.CreateAsync(
            new CreateExternalIdentityBindingRequest(
                user.Id,
                ExternalIdentityProviders.Cloud,
                CloudOidcIdentityProfile.DefaultTenantId,
                "cloud-user-1",
                "employee-1",
                "E0001",
                "张三",
                "D001",
                "制造一部",
                statusVersion,
                AccountEnabledSnapshot: true,
                EmployeeActiveSnapshot: true,
                DateTime.UtcNow));
        return user;
    }

    private sealed class InlineTransactionalExecutionService : ITransactionalExecutionService
    {
        public int GenericExecutionCount { get; private set; }

        public int ResultExecutionCount { get; private set; }

        public Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            GenericExecutionCount++;
            return operation(cancellationToken);
        }

        public Task<Result<TValue>> ExecuteResultAsync<TValue>(
            Func<CancellationToken, Task<Result<TValue>>> operation,
            CancellationToken cancellationToken = default)
        {
            ResultExecutionCount++;
            return operation(cancellationToken);
        }

        public Task<Result> ExecuteResultAsync(
            Func<CancellationToken, Task<Result>> operation,
            CancellationToken cancellationToken = default)
        {
            ResultExecutionCount++;
            return operation(cancellationToken);
        }
    }

    private sealed class KnownConflictTransactionalExecutionService(
        ExternalIdentityInvariantConflictKind conflictKind)
        : ITransactionalExecutionService
    {
        public Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            return operation(cancellationToken);
        }

        public Task<Result<TValue>> ExecuteResultAsync<TValue>(
            Func<CancellationToken, Task<Result<TValue>>> operation,
            CancellationToken cancellationToken = default)
        {
            throw new ExternalIdentityInvariantConflictException(
                conflictKind,
                new InvalidOperationException("known unique constraint"));
        }

        public Task<Result> ExecuteResultAsync(
            Func<CancellationToken, Task<Result>> operation,
            CancellationToken cancellationToken = default)
        {
            throw new ExternalIdentityInvariantConflictException(
                conflictKind,
                new InvalidOperationException("known unique constraint"));
        }
    }

    private sealed class FailingTransactionalExecutionService(Exception failure)
        : ITransactionalExecutionService
    {
        public Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<TResult>(failure);
        }

        public Task<Result<TValue>> ExecuteResultAsync<TValue>(
            Func<CancellationToken, Task<Result<TValue>>> operation,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<Result<TValue>>(failure);
        }

        public Task<Result> ExecuteResultAsync(
            Func<CancellationToken, Task<Result>> operation,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<Result>(failure);
        }
    }

    private sealed class RecordingJwtTokenGenerator : IJwtTokenGenerator
    {
        public JwtTokenUser? LastUser { get; private set; }

        public Task<string> GenerateTokenAsync(JwtTokenUser user, CancellationToken cancellationToken = default)
        {
            LastUser = user;
            return Task.FromResult("ai-token");
        }
    }

    private sealed class InMemoryCloudDelegationGrantStore(
        Exception? createFailure = null) : ICloudDelegationGrantStore
    {
        private readonly Dictionary<Guid, (CreateCloudDelegationGrantRequest Request, string Token)> grants = [];

        public List<CreateCloudDelegationGrantRequest> Requests { get; } = [];

        public Task<CloudDelegationGrantSnapshot> CreateAsync(
            CreateCloudDelegationGrantRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (createFailure is not null)
            {
                return Task.FromException<CloudDelegationGrantSnapshot>(createFailure);
            }

            Requests.Add(request);
            grants.Add(request.GrantId, (request, request.AccessToken));
            return Task.FromResult(new CloudDelegationGrantSnapshot(
                request.GrantId,
                request.AiUserId,
                request.CloudUserId,
                request.Issuer,
                request.TenantId,
                request.ExpiresAtUtc,
                request.IssuedStatusVersion,
                RevokedAtUtc: null,
                request.CreatedAtUtc));
        }

        public Task<CloudDelegationAccessToken?> ResolveAsync(
            Guid grantId,
            Guid aiUserId,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!grants.TryGetValue(grantId, out var grant) ||
                grant.Request.AiUserId != aiUserId ||
                grant.Request.ExpiresAtUtc <= utcNow ||
                string.IsNullOrEmpty(grant.Token))
            {
                return Task.FromResult<CloudDelegationAccessToken?>(null);
            }

            return Task.FromResult<CloudDelegationAccessToken?>(new CloudDelegationAccessToken(
                grantId,
                aiUserId,
                grant.Request.CloudUserId,
                grant.Token,
                grant.Request.ExpiresAtUtc));
        }

        public Task<CloudDelegationRevocationResult> RevokeCurrentAsync(
            Guid grantId,
            Guid aiUserId,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!grants.TryGetValue(grantId, out var grant) ||
                grant.Request.AiUserId != aiUserId)
            {
                return Task.FromResult(new CloudDelegationRevocationResult(false, false));
            }

            var alreadyRevoked = string.IsNullOrEmpty(grant.Token);
            grants[grantId] = (grant.Request, string.Empty);
            return Task.FromResult(new CloudDelegationRevocationResult(true, alreadyRevoked));
        }

        public Task<CloudDelegationPurgeResult> PurgeExpiredAsync(
            DateTime utcNow,
            int batchSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CloudDelegationPurgeResult(true, 0, 0));
    }

    private sealed class RecordingCloudIdentityStatusClient(CloudIdentityStatusCheckResult result)
        : ICloudIdentityStatusClient
    {
        public int CallCount { get; private set; }

        public string? LastCloudUserId { get; private set; }

        public string? LastTenantId { get; private set; }

        public Task<CloudIdentityStatusCheckResult> GetStatusAsync(
            string cloudUserId,
            string tenantId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCloudUserId = cloudUserId;
            LastTenantId = tenantId;
            return Task.FromResult(result);
        }
    }

    private sealed class InMemoryCloudIdentityStatusValidationCache : ICloudIdentityStatusValidationCache
    {
        private readonly Dictionary<string, DateTimeOffset> _cache = [];

        public bool TryGetSuccess(string tenantId, string cloudUserId, string statusVersion, DateTimeOffset now)
        {
            var key = BuildKey(tenantId, cloudUserId, statusVersion);
            return _cache.TryGetValue(key, out var expiresAt) && expiresAt > now;
        }

        public void StoreSuccess(string tenantId, string cloudUserId, string statusVersion, DateTimeOffset expiresAt)
        {
            _cache[BuildKey(tenantId, cloudUserId, statusVersion)] = expiresAt;
        }

        public void Remove(string tenantId, string cloudUserId)
        {
            var prefix = $"{tenantId}:{cloudUserId}:";
            foreach (var key in _cache.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                _cache.Remove(key);
            }
        }

        private static string BuildKey(string tenantId, string cloudUserId, string statusVersion)
        {
            return $"{tenantId}:{cloudUserId}:{statusVersion}";
        }
    }

    private sealed class InMemoryIdentityAuditLogWriter : IIdentityAuditLogWriter
    {
        public List<AuditLogWriteRequest> Requests { get; } = [];

        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryExternalIdentityBindingStore : IExternalIdentityBindingStore
    {
        private readonly List<ExternalIdentityBindingSnapshot> _bindings = [];

        public IReadOnlyCollection<ExternalIdentityBindingSnapshot> Bindings => _bindings;

        public Task<ExternalIdentityBindingSnapshot?> FindByExternalIdentityAsync(
            string provider,
            string tenantId,
            string externalUserId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_bindings.FirstOrDefault(binding =>
                binding.Provider == provider &&
                binding.TenantId == tenantId &&
                binding.ExternalUserId == externalUserId));
        }

        public Task<ExternalIdentityBindingSnapshot?> FindByUserProviderAsync(
            Guid userId,
            string provider,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_bindings.FirstOrDefault(binding =>
                binding.UserId == userId &&
                binding.Provider == provider));
        }

        public Task<ExternalIdentityBindingSnapshot> CreateAsync(
            CreateExternalIdentityBindingRequest request,
            CancellationToken cancellationToken = default)
        {
            var binding = new ExternalIdentityBindingSnapshot(
                Guid.NewGuid(),
                request.UserId,
                request.Provider,
                request.TenantId,
                request.ExternalUserId,
                request.EmployeeId,
                request.EmployeeNo,
                request.DisplayNameSnapshot,
                request.DepartmentIdSnapshot,
                request.DepartmentNameSnapshot,
                request.StatusVersion,
                request.AccountEnabledSnapshot,
                request.EmployeeActiveSnapshot,
                request.NowUtc,
                request.NowUtc);
            _bindings.Add(binding);
            return Task.FromResult(binding);
        }

        public Task UpdateSnapshotAsync(
            UpdateExternalIdentityBindingSnapshotRequest request,
            CancellationToken cancellationToken = default)
        {
            var existing = _bindings.Single(binding => binding.Id == request.BindingId);
            _bindings.Remove(existing);
            _bindings.Add(existing with
            {
                EmployeeId = request.EmployeeId,
                EmployeeNo = request.EmployeeNo,
                DisplayNameSnapshot = request.DisplayNameSnapshot,
                DepartmentIdSnapshot = request.DepartmentIdSnapshot,
                DepartmentNameSnapshot = request.DepartmentNameSnapshot,
                StatusVersion = request.StatusVersion,
                AccountEnabledSnapshot = request.AccountEnabledSnapshot,
                EmployeeActiveSnapshot = request.EmployeeActiveSnapshot,
                LastLoginAtUtc = request.NowUtc,
                LastSyncAtUtc = request.NowUtc
            });
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpExternalIdentityBindingInvariantGuard
        : IExternalIdentityBindingInvariantGuard
    {
        public Task AcquireAsync(
            ExternalIdentityBindingInvariantScope scope,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryIdentityUserFreshReadStore(InMemoryUserManager userManager)
        : IIdentityUserFreshReadStore
    {
        public Task<ApplicationUser?> FindByNormalizedUserNameAsync(
            string normalizedUserName,
            CancellationToken cancellationToken = default)
        {
            return userManager.FindByNameAsync(normalizedUserName);
        }

        public Task<ApplicationUser?> FindByIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return userManager.FindByIdAsync(userId.ToString());
        }

        public async Task<ApplicationUser?> InitializeSecurityStampIfMissingAsync(
            Guid userId,
            string securityStamp,
            string concurrencyStamp,
            CancellationToken cancellationToken = default)
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is not null && string.IsNullOrWhiteSpace(user.SecurityStamp))
            {
                user.SecurityStamp = securityStamp;
                user.ConcurrencyStamp = concurrencyStamp;
            }

            return user;
        }
    }

    private sealed class RecordingExternalIdentityBindingInvariantGuard
        : IExternalIdentityBindingInvariantGuard
    {
        public List<ExternalIdentityBindingInvariantScope> Scopes { get; } = [];

        public Task AcquireAsync(
            ExternalIdentityBindingInvariantScope scope,
            CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryUserManager : UserManager<ApplicationUser>
    {
        private readonly Dictionary<Guid, ApplicationUser> _users = [];
        private readonly Dictionary<Guid, List<string>> _roles = [];

        public InMemoryUserManager(params ApplicationUser[] users)
            : base(
                new StubUserStore(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                [],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!,
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            foreach (var user in users)
            {
                _users[user.Id] = user;
            }
        }

        public IReadOnlyCollection<ApplicationUser> StoredUsers => _users.Values;

        public void SetPassword(ApplicationUser user, string password)
        {
            user.PasswordHash = PasswordHasher.HashPassword(user, password);
        }

        public IReadOnlyCollection<string> GetAssignedRoles(string userName)
        {
            var user = _users.Values.Single(item => item.UserName == userName);
            return _roles.TryGetValue(user.Id, out var roles) ? roles : [];
        }

        public override Task<ApplicationUser?> FindByNameAsync(string userName)
        {
            return Task.FromResult(_users.Values.FirstOrDefault(user =>
                string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase)));
        }

        public override Task<ApplicationUser?> FindByIdAsync(string userId)
        {
            return Guid.TryParse(userId, out var id) && _users.TryGetValue(id, out var user)
                ? Task.FromResult<ApplicationUser?>(user)
                : Task.FromResult<ApplicationUser?>(null);
        }

        public override Task<IdentityResult> CreateAsync(ApplicationUser user)
        {
            if (user.Id == Guid.Empty)
            {
                user.Id = Guid.NewGuid();
            }

            user.SecurityStamp ??= Guid.NewGuid().ToString("N");
            _users[user.Id] = user;
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role)
        {
            if (!_roles.TryGetValue(user.Id, out var roles))
            {
                roles = [];
                _roles[user.Id] = roles;
            }

            roles.Add(role);
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IList<string>> GetRolesAsync(ApplicationUser user)
        {
            return Task.FromResult<IList<string>>(
                _roles.TryGetValue(user.Id, out var roles) ? roles : []);
        }

        public override Task<IList<Claim>> GetClaimsAsync(ApplicationUser user)
        {
            return Task.FromResult<IList<Claim>>([]);
        }

        public override Task<IdentityResult> UpdateSecurityStampAsync(ApplicationUser user)
        {
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<bool> HasPasswordAsync(ApplicationUser user)
        {
            return Task.FromResult(!string.IsNullOrWhiteSpace(user.PasswordHash));
        }

        public override Task<bool> CheckPasswordAsync(ApplicationUser user, string password)
        {
            if (string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                return Task.FromResult(false);
            }

            var verification = PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            return Task.FromResult(verification != PasswordVerificationResult.Failed);
        }
    }

    private sealed class InMemoryRoleManager : RoleManager<IdentityRole<Guid>>
    {
        private readonly HashSet<string> _roles;

        public InMemoryRoleManager(params string[] roles)
            : base(
                new StubRoleStore(),
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                NullLogger<RoleManager<IdentityRole<Guid>>>.Instance)
        {
            _roles = roles.ToHashSet(StringComparer.Ordinal);
        }

        public override Task<bool> RoleExistsAsync(string roleName)
        {
            return Task.FromResult(_roles.Contains(roleName));
        }
    }

    private sealed class StubUserStore : IUserStore<ApplicationUser>
    {
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Success);
        }

        public void Dispose()
        {
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            return Task.FromResult<ApplicationUser?>(null);
        }

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            return Task.FromResult<ApplicationUser?>(null);
        }

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.NormalizedUserName);
        }

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.Id.ToString());
        }

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.UserName);
        }

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Success);
        }
    }

    private sealed class StubRoleStore : IRoleStore<IdentityRole<Guid>>
    {
        public Task<IdentityResult> CreateAsync(IdentityRole<Guid> role, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> DeleteAsync(IdentityRole<Guid> role, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Success);
        }

        public void Dispose()
        {
        }

        public Task<IdentityRole<Guid>?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IdentityRole<Guid>?>(null);
        }

        public Task<IdentityRole<Guid>?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
        {
            return Task.FromResult<IdentityRole<Guid>?>(null);
        }

        public Task<string?> GetNormalizedRoleNameAsync(IdentityRole<Guid> role, CancellationToken cancellationToken)
        {
            return Task.FromResult(role.NormalizedName);
        }

        public Task<string> GetRoleIdAsync(IdentityRole<Guid> role, CancellationToken cancellationToken)
        {
            return Task.FromResult(role.Id.ToString());
        }

        public Task<string?> GetRoleNameAsync(IdentityRole<Guid> role, CancellationToken cancellationToken)
        {
            return Task.FromResult(role.Name);
        }

        public Task SetNormalizedRoleNameAsync(IdentityRole<Guid> role, string? normalizedName, CancellationToken cancellationToken)
        {
            role.NormalizedName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetRoleNameAsync(IdentityRole<Guid> role, string? roleName, CancellationToken cancellationToken)
        {
            role.Name = roleName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> UpdateAsync(IdentityRole<Guid> role, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Success);
        }
    }
}
