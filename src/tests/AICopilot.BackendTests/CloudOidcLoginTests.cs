using System.Security.Claims;
using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Commands;
using AICopilot.IdentityService.Services;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "Identity")]
public sealed class CloudOidcLoginTests
{
    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldJitCreateUserBinding_AndIssueLocalAiToken()
    {
        var userManager = new InMemoryUserManager();
        var roleManager = new InMemoryRoleManager(IdentityRoleNames.User);
        var bindingStore = new InMemoryExternalIdentityBindingStore();
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var tokenGenerator = new RecordingJwtTokenGenerator();
        var handler = CreateHandler(userManager, roleManager, bindingStore, auditWriter, tokenGenerator);

        var result = await handler.Handle(new FinalizeCloudOidcLoginCommand(CreateProfile()), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        result.Value!.UserName.Should().Be("E0001");
        result.Value.Token.Should().Be("ai-token");
        userManager.StoredUsers.Should().ContainSingle(user => user.UserName == "E0001");
        userManager.GetAssignedRoles("E0001").Should().BeEquivalentTo(IdentityRoleNames.User);
        bindingStore.Bindings.Should().ContainSingle(binding =>
            binding.Provider == ExternalIdentityProviders.Cloud &&
            binding.TenantId == CloudOidcIdentityProfile.DefaultTenantId &&
            binding.ExternalUserId == "cloud-user-1" &&
            binding.EmployeeNo == "E0001");
        tokenGenerator.LastUser.Should().NotBeNull();
        tokenGenerator.LastUser!.Roles.Should().BeEquivalentTo(IdentityRoleNames.User);
        tokenGenerator.LastUser.Claims.Should().Contain(claim =>
            claim.Type == ExternalIdentityJwtClaimTypes.IdentityProvider &&
            claim.Value == ExternalIdentityProviders.Cloud);
        tokenGenerator.LastUser.Claims.Should().Contain(claim =>
            claim.Type == ExternalIdentityJwtClaimTypes.CloudUserId &&
            claim.Value == "cloud-user-1");
        auditWriter.Requests.Should().ContainSingle(request =>
            request.ActionCode == "Identity.CloudOidcFirstBind" &&
            request.Result == AuditResults.Succeeded);
        auditWriter.Requests.Single().Metadata.Should().Contain(new KeyValuePair<string, string>(
            "cloudStatusVersion",
            "v1"));
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldReject_WhenEmployeeNoConflictsWithExistingLocalUser()
    {
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "E0001",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var userManager = new InMemoryUserManager(existingUser);
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User),
            new InMemoryExternalIdentityBindingStore(),
            new InMemoryIdentityAuditLogWriter(),
            new RecordingJwtTokenGenerator());

        var result = await handler.Handle(new FinalizeCloudOidcLoginCommand(CreateProfile()), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Unauthorized);
        result.Errors!.OfType<ApiProblemDescriptor>().Single().Code.Should().Be(AuthProblemCodes.ExternalIdentityConflict);
        userManager.StoredUsers.Should().ContainSingle(user => user.Id == existingUser.Id);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldReject_WhenCloudIdentityIsInactive()
    {
        var auditWriter = new InMemoryIdentityAuditLogWriter();
        var userManager = new InMemoryUserManager();
        var handler = CreateHandler(
            userManager,
            new InMemoryRoleManager(IdentityRoleNames.User),
            new InMemoryExternalIdentityBindingStore(),
            auditWriter,
            new RecordingJwtTokenGenerator());

        var result = await handler.Handle(
            new FinalizeCloudOidcLoginCommand(CreateProfile(accountEnabled: false)),
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

    private static FinalizeCloudOidcLoginCommandHandler CreateHandler(
        InMemoryUserManager userManager,
        InMemoryRoleManager roleManager,
        InMemoryExternalIdentityBindingStore bindingStore,
        InMemoryIdentityAuditLogWriter auditWriter,
        RecordingJwtTokenGenerator tokenGenerator)
    {
        return new FinalizeCloudOidcLoginCommandHandler(
            userManager,
            roleManager,
            bindingStore,
            auditWriter,
            tokenGenerator,
            new InlineTransactionalExecutionService());
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
                ServiceAccountToken = "service-token",
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

    private static CloudOidcIdentityProfile CreateProfile(bool accountEnabled = true)
    {
        return new CloudOidcIdentityProfile(
            "https://cloud.example.com",
            "cloud-user-1",
            CloudOidcIdentityProfile.DefaultTenantId,
            "E0001",
            "张三",
            "employee-1",
            "E0001",
            "D001",
            "制造一部",
            "v1",
            accountEnabled,
            EmployeeActive: true);
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
        public Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            return operation(cancellationToken);
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

        public override Task<IdentityResult> DeleteAsync(ApplicationUser user)
        {
            _users.Remove(user.Id);
            _roles.Remove(user.Id);
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
