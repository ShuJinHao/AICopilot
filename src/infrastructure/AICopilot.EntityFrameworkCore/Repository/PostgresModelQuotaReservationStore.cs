using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal sealed class PostgresModelQuotaReservationStore(
    AgentExecutionTransactionRunner transactionRunner)
    : IModelQuotaReservationStore
{
    public Task<ModelQuotaReservationOutcome> TryReserveAsync(
        ModelQuotaReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        return transactionRunner.ExecuteAsync(
            "Agent.ModelQuotaReserve",
            async (context, token) =>
            {
                var now = request.RequestedAtUtc.ToUniversalTime();
                var windowStart = StartOfMinute(now);
                var windowEnd = windowStart.AddMinutes(1);
                var lockScopes = new List<string>
                {
                    $"correlation|{request.CorrelationHash}",
                    $"model|{request.ModelId.Value:D}|{windowStart:O}",
                    $"model-concurrency|{request.ModelId.Value:D}",
                    $"endpoint|{request.ModelId.Value:D}|{request.EndpointId}|{windowStart:O}",
                    $"endpoint-concurrency|{request.ModelId.Value:D}|{request.EndpointId}",
                    $"tenant|{request.TenantKeyHash}|{windowStart:O}",
                    $"tenant-concurrency|{request.TenantKeyHash}",
                    $"role|{request.TenantKeyHash}|{request.RoleKeyHash}|{windowStart:O}",
                    $"role-concurrency|{request.TenantKeyHash}|{request.RoleKeyHash}"
                };
                if (request.UserId.HasValue)
                {
                    lockScopes.Add($"user|{request.TenantKeyHash}|{request.UserId.Value:D}|{windowStart:O}");
                    lockScopes.Add($"user-concurrency|{request.TenantKeyHash}|{request.UserId.Value:D}");
                }

                foreach (var lockScope in lockScopes.OrderBy(value => value, StringComparer.Ordinal))
                {
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock(hashtextextended({lockScope}, 0))",
                        token);
                }

                var duplicate = await context.ModelQuotaReservations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        reservation => reservation.CorrelationHash == request.CorrelationHash,
                        token);
                if (duplicate is not null)
                {
                    var duplicateLease = duplicate.Status == ModelQuotaReservationStatus.Active
                        ? new ModelQuotaReservationLease(
                            duplicate.Id,
                            duplicate.FencingToken,
                            duplicate.CorrelationHash,
                            duplicate.EndpointId,
                            duplicate.ExpiresAtUtc)
                        : null;
                    return Attempt(new ModelQuotaReservationOutcome(
                        ModelQuotaReservationResult.Duplicate,
                        duplicateLease,
                        duplicate.WindowEndsAtUtc,
                        "Quota reservation correlation already exists."));
                }

                var windowReservations = await context.ModelQuotaReservations
                    .AsNoTracking()
                    .Where(reservation =>
                        reservation.WindowStartedAtUtc == windowStart &&
                        reservation.Status != ModelQuotaReservationStatus.Released)
                    .ToListAsync(token);
                var endpoint = windowReservations.Where(reservation =>
                    reservation.ModelId == request.ModelId &&
                    reservation.EndpointId == request.EndpointId).ToArray();
                var model = windowReservations.Where(reservation =>
                    reservation.ModelId == request.ModelId).ToArray();
                var tenant = windowReservations.Where(reservation =>
                    reservation.TenantKeyHash == request.TenantKeyHash).ToArray();
                var user = request.UserId.HasValue
                    ? windowReservations.Where(reservation =>
                        reservation.TenantKeyHash == request.TenantKeyHash &&
                        reservation.UserId == request.UserId).ToArray()
                    : [];
                var role = windowReservations.Where(reservation =>
                    reservation.TenantKeyHash == request.TenantKeyHash &&
                    reservation.RoleKeyHash == request.RoleKeyHash).ToArray();

                if (ExceedsCount(endpoint.Length, request.EndpointRpmLimit) ||
                    ExceedsCount(model.Length, request.ModelRpmLimit) ||
                    request.UserId.HasValue && ExceedsCount(user.Length, request.UserRpmLimit) ||
                    ExceedsCount(role.Length, request.RoleRpmLimit) ||
                    ExceedsCount(tenant.Length, request.TenantRpmLimit))
                {
                    return Attempt(Denied(
                        ModelQuotaReservationResult.RateLimited,
                        windowEnd,
                        "Distributed model request rate limit is exhausted."));
                }

                var tokenEstimate = checked(request.EstimatedInputTokens + request.EstimatedOutputTokens);
                if (ExceedsTokens(endpoint, tokenEstimate, request.EndpointTpmLimit) ||
                    ExceedsTokens(model, tokenEstimate, request.ModelTpmLimit) ||
                    request.UserId.HasValue && ExceedsTokens(user, tokenEstimate, request.UserTpmLimit) ||
                    ExceedsTokens(role, tokenEstimate, request.RoleTpmLimit) ||
                    ExceedsTokens(tenant, tokenEstimate, request.TenantTpmLimit))
                {
                    return Attempt(Denied(
                        ModelQuotaReservationResult.TokenLimited,
                        windowEnd,
                        "Distributed model token limit is exhausted."));
                }

                var activeReservations = await context.ModelQuotaReservations
                    .AsNoTracking()
                    .Where(reservation =>
                        (reservation.Status == ModelQuotaReservationStatus.Active ||
                         reservation.Status == ModelQuotaReservationStatus.ReconciliationRequired) &&
                        reservation.ExpiresAtUtc > now)
                    .ToArrayAsync(token);
                var activeEndpoint = activeReservations.Where(reservation =>
                    reservation.ModelId == request.ModelId &&
                    reservation.EndpointId == request.EndpointId).ToArray();
                var activeModel = activeReservations.Where(reservation =>
                    reservation.ModelId == request.ModelId).ToArray();
                var activeTenant = activeReservations.Where(reservation =>
                    reservation.TenantKeyHash == request.TenantKeyHash).ToArray();
                var activeUser = request.UserId.HasValue
                    ? activeReservations.Where(reservation =>
                        reservation.TenantKeyHash == request.TenantKeyHash &&
                        reservation.UserId == request.UserId).ToArray()
                    : [];
                var activeRole = activeReservations.Where(reservation =>
                    reservation.TenantKeyHash == request.TenantKeyHash &&
                    reservation.RoleKeyHash == request.RoleKeyHash).ToArray();
                if (ExceedsConcurrency(activeEndpoint, request.ConcurrencySlots, request.EndpointConcurrencyLimit) ||
                    ExceedsConcurrency(activeModel, request.ConcurrencySlots, request.ModelConcurrencyLimit) ||
                    request.UserId.HasValue &&
                    ExceedsConcurrency(activeUser, request.ConcurrencySlots, request.UserConcurrencyLimit) ||
                    ExceedsConcurrency(activeRole, request.ConcurrencySlots, request.RoleConcurrencyLimit) ||
                    ExceedsConcurrency(activeTenant, request.ConcurrencySlots, request.TenantConcurrencyLimit))
                {
                    var retryAt = activeReservations
                        .Select(reservation => reservation.ExpiresAtUtc)
                        .DefaultIfEmpty(now.AddSeconds(1))
                        .Min();
                    return Attempt(Denied(
                        ModelQuotaReservationResult.ConcurrencyLimited,
                        retryAt,
                        "Distributed model concurrency limit is exhausted."));
                }

                var fencingToken = await context.Database
                    .SqlQuery<long>($"SELECT nextval('aigateway.model_quota_fencing_seq') AS \"Value\"")
                    .SingleAsync(token);
                var reservation = new ModelQuotaReservation(
                    request.TenantKeyHash,
                    request.UserId,
                    request.RoleKeyHash,
                    request.ModelId,
                    request.EndpointId,
                    request.PoolName,
                    windowStart,
                    windowEnd,
                    request.EstimatedInputTokens,
                    request.EstimatedOutputTokens,
                    request.ConcurrencySlots,
                    fencingToken,
                    request.CorrelationHash,
                    now,
                    now.Add(request.ReservationLease));
                context.ModelQuotaReservations.Add(reservation);
                return Attempt(new ModelQuotaReservationOutcome(
                    ModelQuotaReservationResult.Granted,
                    new ModelQuotaReservationLease(
                        reservation.Id,
                        reservation.FencingToken,
                        reservation.CorrelationHash,
                        reservation.EndpointId,
                        reservation.ExpiresAtUtc),
                    RetryAtUtc: null,
                    "Distributed model quota reservation granted."));
            },
            cancellationToken);
    }

    public Task<ModelQuotaReservationResult> SettleAsync(
        ModelQuotaSettlement settlement,
        CancellationToken cancellationToken = default)
    {
        return transactionRunner.ExecuteAsync(
            "Agent.ModelQuotaSettle",
            async (context, token) =>
            {
                var reservation = await context.ModelQuotaReservations
                    .FromSqlInterpolated($$"""
                        SELECT reservation.*, reservation.xmin
                        FROM aigateway.model_quota_reservations AS reservation
                        WHERE id = {{settlement.Lease.ReservationId.Value}}
                        FOR UPDATE
                        """)
                    .SingleOrDefaultAsync(token);
                if (reservation is null ||
                    reservation.FencingToken != settlement.Lease.FencingToken ||
                    !string.Equals(reservation.CorrelationHash, settlement.Lease.CorrelationHash, StringComparison.Ordinal))
                {
                    return Attempt(ModelQuotaReservationResult.StaleFence);
                }

                if (reservation.Status != ModelQuotaReservationStatus.Active)
                {
                    return Attempt(reservation.Status == ModelQuotaReservationStatus.ReconciliationRequired
                        ? ModelQuotaReservationResult.ReconciliationRequired
                        : ModelQuotaReservationResult.Duplicate);
                }

                if (!settlement.WasDispatched)
                {
                    reservation.Release(settlement.Lease.FencingToken, settlement.SettledAtUtc);
                }
                else if (!settlement.OutcomeKnown)
                {
                    reservation.RequireReconciliation(
                        settlement.Lease.FencingToken,
                        string.IsNullOrWhiteSpace(settlement.FailureCode)
                            ? "model_call_outcome_unknown"
                            : settlement.FailureCode,
                        settlement.SettledAtUtc);
                }
                else
                {
                    reservation.Settle(
                        settlement.Lease.FencingToken,
                        settlement.ActualInputTokens,
                        settlement.ActualOutputTokens,
                        settlement.SettledAtUtc);
                }

                return Attempt(reservation.Status == ModelQuotaReservationStatus.ReconciliationRequired
                    ? ModelQuotaReservationResult.ReconciliationRequired
                    : ModelQuotaReservationResult.Granted);
            },
            cancellationToken);
    }

    public Task<int> ReclaimExpiredAsync(
        DateTimeOffset nowUtc,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(maxItems, 1, 1000);
        return transactionRunner.ExecuteAsync(
            "Agent.ModelQuotaReclaimExpired",
            async (context, token) =>
            {
                var expired = await context.ModelQuotaReservations
                    .FromSqlInterpolated($$"""
                        SELECT reservation.*, reservation.xmin
                        FROM aigateway.model_quota_reservations AS reservation
                        WHERE status IN ('Active', 'ReconciliationRequired')
                          AND expires_at_utc <= {{nowUtc}}
                        ORDER BY expires_at_utc, id
                        FOR UPDATE SKIP LOCKED
                        LIMIT {{take}}
                        """)
                    .ToListAsync(token);
                foreach (var reservation in expired)
                {
                    reservation.Expire(nowUtc);
                }

                return Attempt(expired.Count);
            },
            cancellationToken);
    }

    private static bool ExceedsCount(int current, int limit) => limit > 0 && current + 1 > limit;

    private static bool ExceedsTokens(
        IReadOnlyCollection<ModelQuotaReservation> reservations,
        int requestedTokens,
        int limit)
    {
        if (limit <= 0)
        {
            return false;
        }

        var consumed = reservations.Sum(reservation =>
            reservation.Status == ModelQuotaReservationStatus.Settled
                ? Math.Max(
                    checked(reservation.ActualInputTokens + reservation.ActualOutputTokens),
                    checked(reservation.EstimatedInputTokens + reservation.EstimatedOutputTokens))
                : checked(reservation.EstimatedInputTokens + reservation.EstimatedOutputTokens));
        return checked(consumed + requestedTokens) > limit;
    }

    private static bool ExceedsConcurrency(
        IReadOnlyCollection<ModelQuotaReservation> reservations,
        int requestedSlots,
        int limit)
    {
        return limit > 0 &&
               checked(reservations.Sum(reservation => reservation.ConcurrencySlots) + requestedSlots) > limit;
    }

    private static DateTimeOffset StartOfMinute(DateTimeOffset value)
    {
        var seconds = value.ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(seconds - seconds % 60);
    }

    private static ModelQuotaReservationOutcome Denied(
        ModelQuotaReservationResult result,
        DateTimeOffset retryAtUtc,
        string safeReason)
    {
        return new ModelQuotaReservationOutcome(result, Lease: null, retryAtUtc, safeReason);
    }

    private static void Validate(ModelQuotaReservationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantKeyHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RoleKeyHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EndpointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PoolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CorrelationHash);
        if (request.EstimatedInputTokens < 0 || request.EstimatedOutputTokens < 0 ||
            request.ConcurrencySlots <= 0 || request.ReservationLease <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static AgentExecutionTransactionAttempt<T> Attempt<T>(T value) => new(value);
}
