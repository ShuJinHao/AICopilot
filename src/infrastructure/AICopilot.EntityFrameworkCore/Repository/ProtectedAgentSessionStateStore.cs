using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal sealed class ProtectedAgentSessionStateStore(
    AiGatewayDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider) : IAgentSessionStateStore
{
    internal const int AgentSchemaVersion = 1;
    internal const int MaxSerializedStateBytes = 2 * 1024 * 1024;
    internal static readonly TimeSpan SlidingTimeToLive = TimeSpan.FromDays(30);
    private const string StateProtectionPurpose = "AICopilot.AgentSessionState.v1";
    private const string ApprovalProtectionPurpose = "AICopilot.AgentSessionApprovalBindings.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector stateProtector =
        dataProtectionProvider.CreateProtector(StateProtectionPurpose);
    private readonly IDataProtector approvalProtector =
        dataProtectionProvider.CreateProtector(ApprovalProtectionPurpose);

    public void AddNew(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        string serializedSessionState)
    {
        ValidateSerializedState(serializedSessionState);
        var now = DateTimeOffset.UtcNow;
        dbContext.AgentSessionStates.Add(AgentSessionState.Create(
            new SessionId(sessionId),
            userId,
            tenantId,
            stateProtector.Protect(serializedSessionState),
            now,
            now.Add(SlidingTimeToLive)));
    }

    public async Task<AgentSessionStateSnapshot> LoadOwnedAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var state = await FindAsync(sessionId, cancellationToken);
        EnsureOwnedAndUsable(state, userId, tenantId, allowRunning: true);
        return ToSnapshot(state);
    }

    public async Task<AgentSessionStateSnapshot> BeginTurnAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        Guid turnId,
        bool approvalContinuation,
        CancellationToken cancellationToken = default)
    {
        var state = await FindAsync(sessionId, cancellationToken);
        EnsureOwnedAndUsable(state, userId, tenantId, allowRunning: true);
        var now = DateTimeOffset.UtcNow;
        if (state.Status == AgentSessionRuntimeStatus.Running)
        {
            state.MarkLeftoverRunningInterrupted(now, now.Add(SlidingTimeToLive));
            await SaveAsync(cancellationToken);
            throw new AgentSessionStateException(
                AgentSessionStateFailure.Interrupted,
                "The previous agent turn was left running and the session is now interrupted.");
        }

        if (state.Status == AgentSessionRuntimeStatus.Interrupted)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.Interrupted,
                "The agent session is interrupted and cannot be replayed automatically.");
        }

        var pending = UnprotectApprovals(state.ProtectedApprovalBindings);
        if (approvalContinuation)
        {
            if (pending.Count == 0)
            {
                throw new AgentSessionStateException(
                    AgentSessionStateFailure.ApprovalMissing,
                    "The agent session has no pending approval.");
            }
        }
        else if (pending.Count > 0)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.ApprovalPending,
                "The agent session has a pending approval.");
        }

        state.BeginTurn(turnId, now, now.Add(SlidingTimeToLive));
        await SaveAsync(cancellationToken);
        return ToSnapshot(state);
    }

    public async Task<AgentSessionStateSnapshot> PersistCheckpointAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        Guid turnId,
        string serializedSessionState,
        CancellationToken cancellationToken = default)
    {
        ValidateSerializedState(serializedSessionState);
        var state = await FindAsync(sessionId, cancellationToken);
        EnsureOwnedAndUsable(state, userId, tenantId, allowRunning: true);
        EnsureTurn(state, turnId);
        var now = DateTimeOffset.UtcNow;
        state.PersistCheckpoint(
            turnId,
            stateProtector.Protect(serializedSessionState),
            now,
            now.Add(SlidingTimeToLive));
        await SaveAsync(cancellationToken);
        return ToSnapshot(state);
    }

    public async Task<AgentSessionStateSnapshot> CompleteTurnAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        Guid turnId,
        string serializedSessionState,
        IReadOnlyCollection<AgentApprovalBinding> pendingApprovals,
        CancellationToken cancellationToken = default)
    {
        ValidateSerializedState(serializedSessionState);
        ValidateApprovalBindings(pendingApprovals);
        if (pendingApprovals.Any(binding =>
                binding.SessionId != sessionId ||
                binding.UserId != userId ||
                !string.Equals(
                    NormalizeTenant(binding.TenantId),
                    NormalizeTenant(tenantId),
                    StringComparison.Ordinal)))
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.OwnershipMismatch,
                "The approval binding identity does not match the AgentSession owner.");
        }
        var state = await FindAsync(sessionId, cancellationToken);
        EnsureOwnedAndUsable(state, userId, tenantId, allowRunning: true);
        EnsureTurn(state, turnId);
        var now = DateTimeOffset.UtcNow;
        state.CompleteTurn(
            turnId,
            stateProtector.Protect(serializedSessionState),
            ProtectApprovals(pendingApprovals),
            now,
            now.Add(SlidingTimeToLive));
        await SaveAsync(cancellationToken);
        return ToSnapshot(state);
    }

    public async Task InterruptTurnAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        Guid turnId,
        CancellationToken cancellationToken = default)
    {
        var state = await FindAsync(sessionId, cancellationToken);
        EnsureOwnedAndUsable(state, userId, tenantId, allowRunning: true);
        if (state.Status != AgentSessionRuntimeStatus.Running ||
            state.ActiveTurnId != turnId)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        state.InterruptTurn(turnId, now, now.Add(SlidingTimeToLive));
        await SaveAsync(cancellationToken);
    }

    public async Task<AgentSessionStateSnapshot> PersistModeChangeAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        long expectedVersion,
        string serializedSessionState,
        CancellationToken cancellationToken = default)
    {
        ValidateSerializedState(serializedSessionState);
        var state = await FindAsync(sessionId, cancellationToken);
        EnsureOwnedAndUsable(state, userId, tenantId);
        if (state.Version != expectedVersion)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.VersionConflict,
                "The agent session version changed.");
        }

        if (UnprotectApprovals(state.ProtectedApprovalBindings).Count > 0)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.ApprovalPending,
                "Agent mode cannot change while approval is pending.");
        }

        var now = DateTimeOffset.UtcNow;
        state.PersistModeChange(
            stateProtector.Protect(serializedSessionState),
            now,
            now.Add(SlidingTimeToLive));
        await SaveAsync(cancellationToken);
        return ToSnapshot(state);
    }

    private async Task<AgentSessionState> FindAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var sessionKey = new SessionId(sessionId);
        var state = await dbContext.AgentSessionStates.SingleOrDefaultAsync(
            item => item.SessionId == sessionKey,
            cancellationToken);
        return state ?? throw new AgentSessionStateException(
            AgentSessionStateFailure.Missing,
            "The session does not have a persisted AgentSession state.");
    }

    private void EnsureOwnedAndUsable(
        AgentSessionState state,
        Guid userId,
        string? tenantId,
        bool allowRunning = false)
    {
        if (state.UserId != userId ||
            !string.Equals(
                NormalizeTenant(state.TenantId),
                NormalizeTenant(tenantId),
                StringComparison.Ordinal))
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.OwnershipMismatch,
                "The AgentSession state does not belong to the current identity.");
        }

        if (state.AgentSchemaVersion != AgentSchemaVersion)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.SchemaMismatch,
                "The AgentSession schema is no longer supported.");
        }

        if (state.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.Expired,
                "The AgentSession state expired.");
        }

        if (!allowRunning && state.Status == AgentSessionRuntimeStatus.Running)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.AlreadyRunning,
                "The AgentSession is currently running.");
        }
    }

    private AgentSessionStateSnapshot ToSnapshot(AgentSessionState state)
    {
        try
        {
            var serialized = stateProtector.Unprotect(state.ProtectedState);
            ValidateSerializedState(serialized);
            var approvals = UnprotectApprovals(state.ProtectedApprovalBindings);
            ValidateApprovalBindings(approvals);
            if (approvals.Any(binding =>
                    binding.SessionId != state.SessionId.Value ||
                    binding.UserId != state.UserId ||
                    !string.Equals(
                        NormalizeTenant(binding.TenantId),
                        NormalizeTenant(state.TenantId),
                        StringComparison.Ordinal)))
            {
                throw new AgentSessionStateException(
                    AgentSessionStateFailure.Corrupt,
                    "A protected approval binding does not match its AgentSession owner.");
            }

            return new AgentSessionStateSnapshot(
                state.SessionId.Value,
                state.UserId,
                state.TenantId,
                state.AgentSchemaVersion,
                serialized,
                state.Status,
                state.ActiveTurnId,
                state.Version,
                state.UpdatedAtUtc,
                state.ExpiresAtUtc,
                approvals);
        }
        catch (AgentSessionStateException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is System.Security.Cryptography.CryptographicException or
            JsonException)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.Corrupt,
                "The protected AgentSession state is corrupt.");
        }
    }

    private string? ProtectApprovals(IReadOnlyCollection<AgentApprovalBinding> approvals)
    {
        if (approvals.Count == 0)
        {
            return null;
        }

        var serialized = JsonSerializer.Serialize(approvals, JsonOptions);
        if (Encoding.UTF8.GetByteCount(serialized) > MaxSerializedStateBytes)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.Oversize,
                "The approval binding payload exceeds 2 MiB.");
        }

        return approvalProtector.Protect(serialized);
    }

    private IReadOnlyList<AgentApprovalBinding> UnprotectApprovals(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return [];
        }

        try
        {
            var json = approvalProtector.Unprotect(protectedValue);
            return JsonSerializer.Deserialize<List<AgentApprovalBinding>>(json, JsonOptions)
                   ?? throw new JsonException("Approval bindings payload is null.");
        }
        catch (Exception exception) when (
            exception is System.Security.Cryptography.CryptographicException or
            JsonException)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.Corrupt,
                "The protected approval binding is corrupt.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.VersionConflict,
                "The agent session version changed.");
        }
    }

    private static void EnsureTurn(AgentSessionState state, Guid turnId)
    {
        if (state.Status != AgentSessionRuntimeStatus.Running ||
            state.ActiveTurnId != turnId)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.TurnMismatch,
                "The active agent turn no longer matches.");
        }
    }

    private static void ValidateSerializedState(string serializedSessionState)
    {
        if (string.IsNullOrWhiteSpace(serializedSessionState))
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.Corrupt,
                "The serialized AgentSession state is empty.");
        }

        if (Encoding.UTF8.GetByteCount(serializedSessionState) > MaxSerializedStateBytes)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.Oversize,
                "The serialized AgentSession state exceeds 2 MiB.");
        }

        try
        {
            using var _ = JsonDocument.Parse(serializedSessionState);
        }
        catch (JsonException)
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.Corrupt,
                "The serialized AgentSession state is not valid JSON.");
        }
    }

    private static void ValidateApprovalBindings(
        IReadOnlyCollection<AgentApprovalBinding> approvals)
    {
        if (approvals.Count > 64 ||
            approvals.Any(binding =>
                binding.SessionId == Guid.Empty ||
                binding.UserId == Guid.Empty ||
                string.IsNullOrWhiteSpace(binding.RequestId) ||
                string.IsNullOrWhiteSpace(binding.ToolCallId) ||
                string.IsNullOrWhiteSpace(binding.ToolName) ||
                binding.Arguments is null ||
                binding.ToolSchemaVersion <= 0 ||
                binding.CanonicalArgumentsDigest.Length != 64 ||
                !string.Equals(
                    ComputeArgumentsDigest(binding.Arguments),
                    binding.CanonicalArgumentsDigest,
                    StringComparison.Ordinal)))
        {
            throw new AgentSessionStateException(
                AgentSessionStateFailure.Corrupt,
                "The approval binding is incomplete.");
        }
    }

    private static string ComputeArgumentsDigest(
        IReadOnlyDictionary<string, object?> arguments)
    {
        var element = JsonSerializer.SerializeToElement(arguments, JsonOptions);
        var canonical = AgentCanonicalJsonV1.Canonicalize(element);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string? NormalizeTenant(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();
    }
}
