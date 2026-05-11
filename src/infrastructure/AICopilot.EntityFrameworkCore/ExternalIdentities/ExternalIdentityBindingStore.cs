using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.ExternalIdentities;

public sealed class ExternalIdentityBindingStore(IdentityStoreDbContext dbContext) : IExternalIdentityBindingStore
{
    public async Task<ExternalIdentityBindingSnapshot?> FindByExternalIdentityAsync(
        string provider,
        string tenantId,
        string externalUserId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ExternalIdentityBindings
            .AsNoTracking()
            .Where(binding =>
                binding.Provider == provider &&
                binding.TenantId == tenantId &&
                binding.ExternalUserId == externalUserId)
            .Select(ToSnapshotExpression())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ExternalIdentityBindingSnapshot?> FindByUserProviderAsync(
        Guid userId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ExternalIdentityBindings
            .AsNoTracking()
            .Where(binding => binding.UserId == userId && binding.Provider == provider)
            .Select(ToSnapshotExpression())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<ExternalIdentityBindingSnapshot> CreateAsync(
        CreateExternalIdentityBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var binding = new ExternalIdentityBinding
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Provider = request.Provider,
            TenantId = request.TenantId,
            ExternalUserId = request.ExternalUserId,
            EmployeeId = request.EmployeeId,
            EmployeeNo = request.EmployeeNo,
            DisplayNameSnapshot = request.DisplayNameSnapshot,
            DepartmentIdSnapshot = request.DepartmentIdSnapshot,
            DepartmentNameSnapshot = request.DepartmentNameSnapshot,
            StatusVersion = request.StatusVersion,
            AccountEnabledSnapshot = request.AccountEnabledSnapshot,
            EmployeeActiveSnapshot = request.EmployeeActiveSnapshot,
            LastLoginAtUtc = request.NowUtc,
            LastSyncAtUtc = request.NowUtc,
            CreatedAtUtc = request.NowUtc,
            UpdatedAtUtc = request.NowUtc
        };

        dbContext.ExternalIdentityBindings.Add(binding);
        return Task.FromResult(ToSnapshot(binding));
    }

    public async Task UpdateSnapshotAsync(
        UpdateExternalIdentityBindingSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        var binding = await dbContext.ExternalIdentityBindings
            .FirstOrDefaultAsync(item => item.Id == request.BindingId, cancellationToken)
            ?? throw new InvalidOperationException($"External identity binding '{request.BindingId}' was not found.");

        binding.EmployeeId = request.EmployeeId;
        binding.EmployeeNo = request.EmployeeNo;
        binding.DisplayNameSnapshot = request.DisplayNameSnapshot;
        binding.DepartmentIdSnapshot = request.DepartmentIdSnapshot;
        binding.DepartmentNameSnapshot = request.DepartmentNameSnapshot;
        binding.StatusVersion = request.StatusVersion;
        binding.AccountEnabledSnapshot = request.AccountEnabledSnapshot;
        binding.EmployeeActiveSnapshot = request.EmployeeActiveSnapshot;
        binding.LastLoginAtUtc = request.NowUtc;
        binding.LastSyncAtUtc = request.NowUtc;
        binding.UpdatedAtUtc = request.NowUtc;
    }

    private static System.Linq.Expressions.Expression<Func<ExternalIdentityBinding, ExternalIdentityBindingSnapshot>> ToSnapshotExpression()
    {
        return binding => new ExternalIdentityBindingSnapshot(
            binding.Id,
            binding.UserId,
            binding.Provider,
            binding.TenantId,
            binding.ExternalUserId,
            binding.EmployeeId,
            binding.EmployeeNo,
            binding.DisplayNameSnapshot,
            binding.DepartmentIdSnapshot,
            binding.DepartmentNameSnapshot,
            binding.StatusVersion,
            binding.AccountEnabledSnapshot,
            binding.EmployeeActiveSnapshot,
            binding.LastLoginAtUtc,
            binding.LastSyncAtUtc);
    }

    private static ExternalIdentityBindingSnapshot ToSnapshot(ExternalIdentityBinding binding)
    {
        return new ExternalIdentityBindingSnapshot(
            binding.Id,
            binding.UserId,
            binding.Provider,
            binding.TenantId,
            binding.ExternalUserId,
            binding.EmployeeId,
            binding.EmployeeNo,
            binding.DisplayNameSnapshot,
            binding.DepartmentIdSnapshot,
            binding.DepartmentNameSnapshot,
            binding.StatusVersion,
            binding.AccountEnabledSnapshot,
            binding.EmployeeActiveSnapshot,
            binding.LastLoginAtUtc,
            binding.LastSyncAtUtc);
    }
}
