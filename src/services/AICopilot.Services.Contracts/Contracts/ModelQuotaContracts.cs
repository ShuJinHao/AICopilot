using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.Services.Contracts;

public enum ModelQuotaReservationResult
{
    Granted = 0,
    RateLimited = 1,
    TokenLimited = 2,
    ConcurrencyLimited = 3,
    Duplicate = 4,
    StaleFence = 5,
    ReconciliationRequired = 6,
    PolicyUnavailable = 7
}

public sealed record ModelQuotaReservationRequest(
    string TenantKeyHash,
    Guid? UserId,
    string RoleKeyHash,
    LanguageModelId ModelId,
    string EndpointId,
    string PoolName,
    int EstimatedInputTokens,
    int EstimatedOutputTokens,
    int ConcurrencySlots,
    int EndpointRpmLimit,
    int EndpointTpmLimit,
    int EndpointConcurrencyLimit,
    int ModelRpmLimit,
    int ModelTpmLimit,
    int ModelConcurrencyLimit,
    int UserRpmLimit,
    int UserTpmLimit,
    int UserConcurrencyLimit,
    int RoleRpmLimit,
    int RoleTpmLimit,
    int RoleConcurrencyLimit,
    int TenantRpmLimit,
    int TenantTpmLimit,
    int TenantConcurrencyLimit,
    string CorrelationHash,
    DateTimeOffset RequestedAtUtc,
    TimeSpan ReservationLease);

public sealed record ModelQuotaReservationLease(
    ModelQuotaReservationId ReservationId,
    long FencingToken,
    string CorrelationHash,
    string EndpointId,
    DateTimeOffset ExpiresAtUtc);

public sealed record ModelQuotaReservationOutcome(
    ModelQuotaReservationResult Result,
    ModelQuotaReservationLease? Lease,
    DateTimeOffset? RetryAtUtc,
    string SafeReason);

public sealed record ModelQuotaSettlement(
    ModelQuotaReservationLease Lease,
    int ActualInputTokens,
    int ActualOutputTokens,
    bool WasDispatched,
    bool OutcomeKnown,
    string? FailureCode,
    DateTimeOffset SettledAtUtc);

public interface IModelQuotaReservationStore
{
    Task<ModelQuotaReservationOutcome> TryReserveAsync(
        ModelQuotaReservationRequest request,
        CancellationToken cancellationToken = default);

    Task<ModelQuotaReservationResult> SettleAsync(
        ModelQuotaSettlement settlement,
        CancellationToken cancellationToken = default);

    Task<int> ReclaimExpiredAsync(
        DateTimeOffset nowUtc,
        int maxItems,
        CancellationToken cancellationToken = default);
}
