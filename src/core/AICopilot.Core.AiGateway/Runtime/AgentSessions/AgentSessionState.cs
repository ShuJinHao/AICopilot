using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.Core.AiGateway.Runtime.AgentSessions;

public enum AgentSessionRuntimeStatus
{
    Ready = 0,
    Running = 1,
    Interrupted = 2
}

/// <summary>
/// Runtime record for the serialized Microsoft Agent Framework session. This
/// is not a domain aggregate and has no independent business lifecycle.
/// </summary>
public sealed class AgentSessionState
{
    private AgentSessionState()
    {
    }

    private AgentSessionState(
        SessionId sessionId,
        Guid userId,
        string? tenantId,
        string protectedState,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc)
    {
        SessionId = sessionId;
        UserId = userId;
        TenantId = NormalizeTenant(tenantId);
        AgentSchemaVersion = 1;
        ProtectedState = protectedState;
        Status = AgentSessionRuntimeStatus.Ready;
        Version = 1;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public SessionId SessionId { get; private set; }

    public Guid UserId { get; private set; }

    public string? TenantId { get; private set; }

    public int AgentSchemaVersion { get; private set; }

    public string ProtectedState { get; private set; } = null!;

    public AgentSessionRuntimeStatus Status { get; private set; }

    public Guid? ActiveTurnId { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public string? ProtectedApprovalBindings { get; private set; }

    public static AgentSessionState Create(
        SessionId sessionId,
        Guid userId,
        string? tenantId,
        string protectedState,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Agent session owner is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(protectedState))
        {
            throw new ArgumentException("Protected AgentSession state is required.", nameof(protectedState));
        }

        return new AgentSessionState(
            sessionId,
            userId,
            tenantId,
            protectedState,
            nowUtc,
            expiresAtUtc);
    }

    public void BeginTurn(Guid turnId, DateTimeOffset nowUtc, DateTimeOffset expiresAtUtc)
    {
        if (turnId == Guid.Empty)
        {
            throw new ArgumentException("Turn id is required.", nameof(turnId));
        }

        Status = AgentSessionRuntimeStatus.Running;
        ActiveTurnId = turnId;
        Touch(nowUtc, expiresAtUtc);
    }

    public void PersistCheckpoint(
        Guid turnId,
        string protectedState,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc)
    {
        EnsureActiveTurn(turnId);
        ProtectedState = protectedState;
        Touch(nowUtc, expiresAtUtc);
    }

    public void CompleteTurn(
        Guid turnId,
        string protectedState,
        string? protectedApprovalBindings,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc)
    {
        EnsureActiveTurn(turnId);
        ProtectedState = protectedState;
        ProtectedApprovalBindings = protectedApprovalBindings;
        ActiveTurnId = null;
        Status = AgentSessionRuntimeStatus.Ready;
        Touch(nowUtc, expiresAtUtc);
    }

    public void InterruptTurn(
        Guid turnId,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc)
    {
        EnsureActiveTurn(turnId);
        ActiveTurnId = null;
        ProtectedApprovalBindings = null;
        Status = AgentSessionRuntimeStatus.Interrupted;
        Touch(nowUtc, expiresAtUtc);
    }

    public void MarkLeftoverRunningInterrupted(
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (Status != AgentSessionRuntimeStatus.Running)
        {
            return;
        }

        ActiveTurnId = null;
        ProtectedApprovalBindings = null;
        Status = AgentSessionRuntimeStatus.Interrupted;
        Touch(nowUtc, expiresAtUtc);
    }

    public void PersistModeChange(
        string protectedState,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc)
    {
        ProtectedState = protectedState;
        Touch(nowUtc, expiresAtUtc);
    }

    private void EnsureActiveTurn(Guid turnId)
    {
        if (Status != AgentSessionRuntimeStatus.Running || ActiveTurnId != turnId)
        {
            throw new InvalidOperationException("Agent session active turn does not match.");
        }
    }

    private void Touch(DateTimeOffset nowUtc, DateTimeOffset expiresAtUtc)
    {
        Version = checked(Version + 1);
        UpdatedAtUtc = nowUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    private static string? NormalizeTenant(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();
    }
}
