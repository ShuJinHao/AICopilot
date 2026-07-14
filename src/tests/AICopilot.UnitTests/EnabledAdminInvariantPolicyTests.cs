using AICopilot.IdentityService.Authorization;
using AICopilot.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AICopilot.UnitTests;

public sealed class EnabledAdminInvariantPolicyTests
{
    [Fact]
    public async Task LastEnabledAdmin_ShouldBeDetectedFromEnabledRoleMembers()
    {
        var current = NewUser();
        using var userManager = new RoleMembershipUserManager([current]);
        var policy = new EnabledAdminInvariantPolicy(userManager, new RecordingGuard());

        var result = await policy.IsLastEnabledAdminAsync(current, [IdentityRoleNames.Admin]);

        result.Should().BeTrue();
        userManager.RoleQueryCount.Should().Be(1);
    }

    [Fact]
    public async Task AnotherEnabledAdmin_ShouldAllowCurrentAdminToDecrease()
    {
        var current = NewUser();
        using var userManager = new RoleMembershipUserManager([current, NewUser()]);
        var policy = new EnabledAdminInvariantPolicy(userManager, new RecordingGuard());

        var result = await policy.IsLastEnabledAdminAsync(current, [IdentityRoleNames.Admin]);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, "Admin")]
    [InlineData(false, "User")]
    public async Task NonDecreasingUser_ShouldNotQueryRoleMembership(bool disabled, string role)
    {
        var current = NewUser();
        if (disabled)
        {
            IdentityGovernanceHelper.MarkUserDisabled(current);
        }

        using var userManager = new RoleMembershipUserManager([current]);
        var policy = new EnabledAdminInvariantPolicy(userManager, new RecordingGuard());

        var result = await policy.IsLastEnabledAdminAsync(current, [role]);

        result.Should().BeFalse();
        userManager.RoleQueryCount.Should().Be(0);
    }

    [Fact]
    public async Task Acquire_ShouldForwardCallerCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();
        var guard = new RecordingGuard();
        using var userManager = new RoleMembershipUserManager([]);
        var policy = new EnabledAdminInvariantPolicy(userManager, guard);

        await policy.AcquireAsync(cancellation.Token);

        guard.LastToken.Should().Be(cancellation.Token);
    }

    private static ApplicationUser NewUser() => new() { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N") };

    private sealed class RecordingGuard : IIdentityEnabledAdminInvariantGuard
    {
        public CancellationToken LastToken { get; private set; }

        public Task AcquireAsync(CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class RoleMembershipUserManager(IList<ApplicationUser> users)
        : UserManager<ApplicationUser>(
            new NullUserStore(),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance)
    {
        public int RoleQueryCount { get; private set; }

        public override Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName)
        {
            RoleQueryCount++;
            return Task.FromResult(users);
        }
    }

    private sealed class NullUserStore : IUserStore<ApplicationUser>
    {
        public void Dispose() { }
        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString());
        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) { user.UserName = userName; return Task.CompletedTask; }
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
    }
}
