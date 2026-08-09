using System.Security.Cryptography;
using AICopilot.Services.Contracts;
using AICopilot.EntityFrameworkCore.Locking;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AICopilot.EntityFrameworkCore.CloudDelegations;

public sealed class CloudDelegationGrantStore(
    IdentityStoreDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider) : ICloudDelegationGrantStore
{
    internal const string ProtectionPurpose =
        "AICopilot.CloudDelegationGrant.AccessToken.v1";
    internal const string PurgeLockScope =
        "AICopilot.CloudDelegationGrant.PurgeExpired.v1";
    private static readonly TimeSpan MetadataRetention = TimeSpan.FromHours(24);

    private readonly IDataProtector protector =
        dataProtectionProvider.CreateProtector(ProtectionPurpose);

    public Task<CloudDelegationGrantSnapshot> CreateAsync(
        CreateCloudDelegationGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.GrantId == Guid.Empty ||
            request.AiUserId == Guid.Empty ||
            request.CloudUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.AccessToken) ||
            request.ExpiresAtUtc <= request.CreatedAtUtc ||
            string.IsNullOrWhiteSpace(request.Issuer) ||
            string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.IssuedStatusVersion))
        {
            throw new InvalidOperationException("Cloud delegation grant input is incomplete or expired.");
        }

        var grant = new CloudDelegationGrant
        {
            GrantId = request.GrantId,
            AiUserId = request.AiUserId,
            CloudUserId = request.CloudUserId,
            Issuer = request.Issuer,
            TenantId = request.TenantId,
            ProtectedToken = protector.Protect(request.AccessToken),
            ExpiresAtUtc = request.ExpiresAtUtc,
            IssuedStatusVersion = request.IssuedStatusVersion,
            CreatedAtUtc = request.CreatedAtUtc
        };
        dbContext.CloudDelegationGrants.Add(grant);
        return Task.FromResult(ToSnapshot(grant));
    }

    public async Task<CloudDelegationAccessToken?> ResolveAsync(
        Guid grantId,
        Guid aiUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        if (grantId == Guid.Empty || aiUserId == Guid.Empty)
        {
            return null;
        }

        var grant = await dbContext.CloudDelegationGrants
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.GrantId == grantId &&
                candidate.AiUserId == aiUserId &&
                candidate.RevokedAtUtc == null &&
                candidate.ProtectedToken != null &&
                candidate.ExpiresAtUtc > utcNow,
                cancellationToken);
        if (grant is null)
        {
            return null;
        }

        try
        {
            return new CloudDelegationAccessToken(
                grant.GrantId,
                grant.AiUserId,
                grant.CloudUserId,
                protector.Unprotect(grant.ProtectedToken!),
                grant.ExpiresAtUtc);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task<CloudDelegationRevocationResult> RevokeCurrentAsync(
        Guid grantId,
        Guid aiUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        if (grantId == Guid.Empty || aiUserId == Guid.Empty)
        {
            return new CloudDelegationRevocationResult(false, false);
        }

        var grant = await dbContext.CloudDelegationGrants.SingleOrDefaultAsync(
            candidate =>
                candidate.GrantId == grantId &&
                candidate.AiUserId == aiUserId,
            cancellationToken);
        if (grant is null)
        {
            return new CloudDelegationRevocationResult(false, false);
        }

        var alreadyRevoked = grant.RevokedAtUtc.HasValue;
        if (!alreadyRevoked)
        {
            grant.RevokedAtUtc = utcNow;
        }

        grant.ProtectedToken = null;
        return new CloudDelegationRevocationResult(true, alreadyRevoked);
    }

    public async Task<CloudDelegationPurgeResult> PurgeExpiredAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "Cloud delegation cleanup batch size must be between 1 and 500.");
        }

        var currentTransaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Cloud delegation cleanup must run inside the Identity transaction boundary.");
        var lockAcquired = await PostgreSqlAdvisoryLock.TryAcquireTransactionAsync(
            dbContext.Database.GetDbConnection(),
            currentTransaction.GetDbTransaction(),
            PostgreSqlAdvisoryLock.CreateKey(PurgeLockScope),
            cancellationToken);
        if (!lockAcquired)
        {
            return new CloudDelegationPurgeResult(false, 0, 0);
        }

        var metadataCutoffUtc = utcNow.Subtract(MetadataRetention);
        var candidates = await dbContext.CloudDelegationGrants
            .Where(grant =>
                (grant.ExpiresAtUtc <= utcNow && grant.ProtectedToken != null) ||
                grant.ExpiresAtUtc <= metadataCutoffUtc ||
                (grant.RevokedAtUtc != null && grant.RevokedAtUtc <= metadataCutoffUtc))
            .OrderBy(grant => grant.ExpiresAtUtc)
            .ThenBy(grant => grant.GrantId)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        var clearedTokenCount = 0;
        var deletedMetadataCount = 0;
        foreach (var grant in candidates)
        {
            var shouldDelete = grant.ExpiresAtUtc <= metadataCutoffUtc ||
                               grant.RevokedAtUtc <= metadataCutoffUtc;
            if (shouldDelete)
            {
                dbContext.CloudDelegationGrants.Remove(grant);
                deletedMetadataCount++;
                continue;
            }

            if (grant.ExpiresAtUtc <= utcNow && grant.ProtectedToken is not null)
            {
                grant.ProtectedToken = null;
                clearedTokenCount++;
            }
        }

        return new CloudDelegationPurgeResult(
            true,
            clearedTokenCount,
            deletedMetadataCount);
    }

    private static CloudDelegationGrantSnapshot ToSnapshot(CloudDelegationGrant grant)
    {
        return new CloudDelegationGrantSnapshot(
            grant.GrantId,
            grant.AiUserId,
            grant.CloudUserId,
            grant.Issuer,
            grant.TenantId,
            grant.ExpiresAtUtc,
            grant.IssuedStatusVersion,
            grant.RevokedAtUtc,
            grant.CreatedAtUtc);
    }
}
