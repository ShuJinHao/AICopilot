using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.ExternalIdentities;

public sealed class IdentityUserFreshReadStore(IdentityStoreDbContext dbContext)
    : IIdentityUserFreshReadStore
{
    public Task<ApplicationUser?> FindByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUserName);

        return dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                user => user.NormalizedUserName == normalizedUserName,
                cancellationToken);
    }

    public Task<ApplicationUser?> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);

        return dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public async Task<ApplicationUser?> InitializeSecurityStampIfMissingAsync(
        Guid userId,
        string securityStamp,
        string concurrencyStamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyStamp);

        _ = await dbContext.Users
            .Where(user =>
                user.Id == userId &&
                (user.SecurityStamp == null || user.SecurityStamp.Trim() == string.Empty))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.SecurityStamp, securityStamp)
                    .SetProperty(user => user.ConcurrencyStamp, concurrencyStamp),
                cancellationToken);

        return await FindByIdAsync(userId, cancellationToken);
    }
}
