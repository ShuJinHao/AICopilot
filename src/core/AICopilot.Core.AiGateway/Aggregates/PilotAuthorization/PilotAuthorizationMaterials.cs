namespace AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;

public sealed record PilotCredentialWindow(string? PlanningSummary, DateTimeOffset? PlanningApprovedAt)
{
    public static PilotCredentialWindow Empty() => new(null, null);
}

public sealed record PilotRollbackPlan(string RollbackOwner, string EmergencyOwner, string? RollbackSummary)
{
    private PilotRollbackPlan()
        : this(string.Empty, string.Empty, null)
    {
    }

    public static PilotRollbackPlan Empty() => new(string.Empty, string.Empty, null);
}

public sealed record PilotEvidenceArchive(string? EvidenceSummary, Guid[] ArtifactIds)
{
    public static PilotEvidenceArchive Empty() => new(null, []);
}

public sealed record PilotAuthorizationMaterialIntake(
    string? BusinessScope,
    string? Department,
    string? PilotOwner,
    DateTimeOffset? ExecutionWindowStart,
    DateTimeOffset? ExecutionWindowEnd,
    DateTimeOffset? RollbackWindowStart,
    DateTimeOffset? RollbackWindowEnd,
    string? CredentialOwner,
    string? SecretStorageMode,
    string? SecretReferenceNameHash,
    string? PostRunAuditArchiveFormat,
    string? SignedApprovalRef)
{
    private PilotAuthorizationMaterialIntake()
        : this(null, null, null, null, null, null, null, null, null, null, null, null)
    {
    }

    public static PilotAuthorizationMaterialIntake Empty() =>
        new(null, null, null, null, null, null, null, null, null, null, null, null);
}
