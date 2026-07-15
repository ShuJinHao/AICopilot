using AICopilot.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AICopilot.PersistenceTestKit;

internal sealed class IdentityManagerTestScope : IDisposable
{
    private readonly ServiceProvider serviceProvider;

    private IdentityManagerTestScope(
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

    public static IdentityManagerTestScope Create(IdentityStoreDbContext dbContext)
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
        return new IdentityManagerTestScope(serviceProvider, userManager, roleManager);
    }

    public void Dispose()
    {
        UserManager.Dispose();
        RoleManager.Dispose();
        serviceProvider.Dispose();
    }
}
