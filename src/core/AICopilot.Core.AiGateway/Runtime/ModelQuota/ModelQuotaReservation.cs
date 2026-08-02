using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Runtime.ModelQuota;

public enum ModelQuotaReservationStatus
{
    Active = 0,
    Settled = 1,
    Released = 2,
    ReconciliationRequired = 3,
    Expired = 4
}

public sealed class ModelQuotaReservation : BaseEntity<ModelQuotaReservationId>
{
    private ModelQuotaReservation()
    {
    }

    public ModelQuotaReservation(
        string tenantKeyHash,
        Guid? userId,
        string roleKeyHash,
        LanguageModelId modelId,
        string endpointId,
        string poolName,
        DateTimeOffset windowStartedAtUtc,
        DateTimeOffset windowEndsAtUtc,
        int estimatedInputTokens,
        int estimatedOutputTokens,
        int concurrencySlots,
        long fencingToken,
        string correlationHash,
        DateTimeOffset reservedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (windowEndsAtUtc <= windowStartedAtUtc ||
            expiresAtUtc <= reservedAtUtc ||
            estimatedInputTokens < 0 ||
            estimatedOutputTokens < 0 ||
            concurrencySlots <= 0 ||
            fencingToken <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowEndsAtUtc));
        }

        Id = ModelQuotaReservationId.New();
        TenantKeyHash = NormalizeRequired(tenantKeyHash, nameof(tenantKeyHash), 128);
        UserId = userId;
        RoleKeyHash = NormalizeRequired(roleKeyHash, nameof(roleKeyHash), 128);
        ModelId = modelId;
        EndpointId = NormalizeRequired(endpointId, nameof(endpointId), 160);
        PoolName = NormalizeRequired(poolName, nameof(poolName), 120);
        WindowStartedAtUtc = windowStartedAtUtc;
        WindowEndsAtUtc = windowEndsAtUtc;
        EstimatedInputTokens = estimatedInputTokens;
        EstimatedOutputTokens = estimatedOutputTokens;
        ConcurrencySlots = concurrencySlots;
        FencingToken = fencingToken;
        CorrelationHash = NormalizeRequired(correlationHash, nameof(correlationHash), 128);
        Status = ModelQuotaReservationStatus.Active;
        ReservedAtUtc = reservedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string TenantKeyHash { get; private set; } = string.Empty;

    public Guid? UserId { get; private set; }

    public string RoleKeyHash { get; private set; } = string.Empty;

    public LanguageModelId ModelId { get; private set; }

    public string EndpointId { get; private set; } = string.Empty;

    public string PoolName { get; private set; } = string.Empty;

    public DateTimeOffset WindowStartedAtUtc { get; private set; }

    public DateTimeOffset WindowEndsAtUtc { get; private set; }

    public int EstimatedInputTokens { get; private set; }

    public int EstimatedOutputTokens { get; private set; }

    public int ActualInputTokens { get; private set; }

    public int ActualOutputTokens { get; private set; }

    public int ConcurrencySlots { get; private set; }

    public long FencingToken { get; private set; }

    public string CorrelationHash { get; private set; } = string.Empty;

    public ModelQuotaReservationStatus Status { get; private set; }

    public string? FailureCode { get; private set; }

    public DateTimeOffset ReservedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? SettledAtUtc { get; private set; }

    public void Settle(long fencingToken, int actualInputTokens, int actualOutputTokens, DateTimeOffset nowUtc)
    {
        EnsureActiveFence(fencingToken);
        if (actualInputTokens < 0 || actualOutputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actualInputTokens));
        }

        ActualInputTokens = actualInputTokens;
        ActualOutputTokens = actualOutputTokens;
        Status = ModelQuotaReservationStatus.Settled;
        SettledAtUtc = nowUtc;
    }

    public void Release(long fencingToken, DateTimeOffset nowUtc)
    {
        EnsureActiveFence(fencingToken);
        Status = ModelQuotaReservationStatus.Released;
        SettledAtUtc = nowUtc;
    }

    public void RequireReconciliation(long fencingToken, string failureCode, DateTimeOffset nowUtc)
    {
        EnsureActiveFence(fencingToken);
        FailureCode = NormalizeRequired(failureCode, nameof(failureCode), 120);
        Status = ModelQuotaReservationStatus.ReconciliationRequired;
        SettledAtUtc = nowUtc;
    }

    public void Expire(DateTimeOffset nowUtc)
    {
        if (Status is (ModelQuotaReservationStatus.Active or ModelQuotaReservationStatus.ReconciliationRequired) &&
            ExpiresAtUtc <= nowUtc)
        {
            Status = ModelQuotaReservationStatus.Expired;
            SettledAtUtc = nowUtc;
        }
    }

    private void EnsureActiveFence(long fencingToken)
    {
        if (Status != ModelQuotaReservationStatus.Active || fencingToken != FencingToken)
        {
            throw new InvalidOperationException("Model quota reservation fencing token is stale.");
        }
    }

    private static string NormalizeRequired(string value, string parameterName, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentException($"{parameterName} exceeds {maxLength} characters.", parameterName);
    }
}
