using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;

public sealed class PilotAuthorizationSubmission
    : BaseEntity<PilotAuthorizationSubmissionId>, IAggregateRoot<PilotAuthorizationSubmissionId>
{
    private PilotAuthorizationSubmission()
    {
    }

    public PilotAuthorizationSubmission(
        Guid requestedByUserId,
        string? requestedByUserName,
        string title,
        string businessPurpose,
        IReadOnlyCollection<string>? endpointCodes,
        int maxRows,
        int timeRangeDays,
        string dataOwner,
        string toolOwner,
        string finalOwner,
        string rollbackOwner,
        string emergencyOwner,
        string? evidenceSummary,
        string? rollbackSummary,
        string? businessScope,
        string? department,
        string? pilotOwner,
        DateTimeOffset? executionWindowStart,
        DateTimeOffset? executionWindowEnd,
        DateTimeOffset? rollbackWindowStart,
        DateTimeOffset? rollbackWindowEnd,
        string? credentialOwner,
        string? secretStorageMode,
        string? secretReferenceNameHash,
        string? postRunAuditArchiveFormat,
        string? signedApprovalRef,
        DateTimeOffset? expiresAt,
        DateTimeOffset nowUtc)
    {
        if (requestedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Requested user id is required.", nameof(requestedByUserId));
        }

        Id = PilotAuthorizationSubmissionId.New();
        RequestedByUserId = requestedByUserId;
        RequestedByUserName = NormalizeOptional(requestedByUserName, 160);
        CreatedAt = nowUtc;
        ApplyDraft(
            title,
            businessPurpose,
            endpointCodes,
            maxRows,
            timeRangeDays,
            dataOwner,
            toolOwner,
            finalOwner,
            rollbackOwner,
            emergencyOwner,
            evidenceSummary,
            rollbackSummary,
            businessScope,
            department,
            pilotOwner,
            executionWindowStart,
            executionWindowEnd,
            rollbackWindowStart,
            rollbackWindowEnd,
            credentialOwner,
            secretStorageMode,
            secretReferenceNameHash,
            postRunAuditArchiveFormat,
            signedApprovalRef,
            expiresAt,
            nowUtc);
        Status = PilotAuthorizationSubmissionStatus.Draft;
    }

    public Guid RequestedByUserId { get; private set; }

    public string? RequestedByUserName { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string BusinessPurpose { get; private set; } = string.Empty;

    public string[] EndpointCodes { get; private set; } = [];

    public int MaxRows { get; private set; }

    public int TimeRangeDays { get; private set; }

    public string DataOwner { get; private set; } = string.Empty;

    public string ToolOwner { get; private set; } = string.Empty;

    public string FinalOwner { get; private set; } = string.Empty;

    public PilotAuthorizationSubmissionStatus Status { get; private set; }

    public string MachineValidationStatus { get; private set; } = "NotEvaluated";

    public string[] MachineRejectedReasons { get; private set; } = [];

    public PilotAuthorizationReview Review { get; private set; } = PilotAuthorizationReview.Empty();

    public PilotCredentialWindow CredentialWindow { get; private set; } = PilotCredentialWindow.Empty();

    public PilotRollbackPlan RollbackPlan { get; private set; } = PilotRollbackPlan.Empty();

    public PilotEvidenceArchive EvidenceArchive { get; private set; } = PilotEvidenceArchive.Empty();

    public PilotAuthorizationMaterialIntake MaterialIntake { get; private set; } =
        PilotAuthorizationMaterialIntake.Empty();

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdateDraft(
        string title,
        string businessPurpose,
        IReadOnlyCollection<string>? endpointCodes,
        int maxRows,
        int timeRangeDays,
        string dataOwner,
        string toolOwner,
        string finalOwner,
        string rollbackOwner,
        string emergencyOwner,
        string? evidenceSummary,
        string? rollbackSummary,
        string? businessScope,
        string? department,
        string? pilotOwner,
        DateTimeOffset? executionWindowStart,
        DateTimeOffset? executionWindowEnd,
        DateTimeOffset? rollbackWindowStart,
        DateTimeOffset? rollbackWindowEnd,
        string? credentialOwner,
        string? secretStorageMode,
        string? secretReferenceNameHash,
        string? postRunAuditArchiveFormat,
        string? signedApprovalRef,
        DateTimeOffset? expiresAt,
        DateTimeOffset nowUtc)
    {
        EnsureEditableDraft();
        ApplyDraft(
            title,
            businessPurpose,
            endpointCodes,
            maxRows,
            timeRangeDays,
            dataOwner,
            toolOwner,
            finalOwner,
            rollbackOwner,
            emergencyOwner,
            evidenceSummary,
            rollbackSummary,
            businessScope,
            department,
            pilotOwner,
            executionWindowStart,
            executionWindowEnd,
            rollbackWindowStart,
            rollbackWindowEnd,
            credentialOwner,
            secretStorageMode,
            secretReferenceNameHash,
            postRunAuditArchiveFormat,
            signedApprovalRef,
            expiresAt,
            nowUtc);
    }

    public void Submit(PilotAuthorizationMachineValidationResult validationResult, DateTimeOffset nowUtc)
    {
        EnsureEditableDraft();
        Status = PilotAuthorizationSubmissionStatus.Submitted;
        MachineValidationStatus = validationResult.IsAccepted ? "Accepted" : "Rejected";
        MachineRejectedReasons = NormalizeStrings(validationResult.RejectedReasons, 320);
        Review = Review.MarkSubmitted(nowUtc);

        if (validationResult.IsAccepted)
        {
            Status = PilotAuthorizationSubmissionStatus.ReviewPending;
            Review = Review.MarkReviewStarted(nowUtc);
        }
        else
        {
            Status = PilotAuthorizationSubmissionStatus.MachineRejected;
        }

        UpdatedAt = nowUtc;
    }

    public void ApproveCredentialWindowPlanning(
        Guid reviewerUserId,
        string? reviewerUserName,
        string? reason,
        string? credentialWindowSummary,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(PilotAuthorizationSubmissionStatus.ReviewPending);
        EnsureDecisionTextIsSafe(reason, credentialWindowSummary);
        Status = PilotAuthorizationSubmissionStatus.ApprovedForCredentialWindowPlanning;
        CredentialWindow = new PilotCredentialWindow(
            NormalizeOptional(credentialWindowSummary, 1000),
            nowUtc);
        Review = Review.MarkDecision(reviewerUserId, reviewerUserName, reason, Status, nowUtc);
        UpdatedAt = nowUtc;
    }

    public void ApproveLimitedPilotExecutionPlanning(
        Guid reviewerUserId,
        string? reviewerUserName,
        string? reason,
        DateTimeOffset nowUtc)
    {
        EnsureStatus(PilotAuthorizationSubmissionStatus.ApprovedForCredentialWindowPlanning);
        EnsureDecisionTextIsSafe(reason);
        Status = PilotAuthorizationSubmissionStatus.ApprovedForLimitedPilotExecutionPlanning;
        Review = Review.MarkDecision(reviewerUserId, reviewerUserName, reason, Status, nowUtc);
        UpdatedAt = nowUtc;
    }

    public void Reject(Guid reviewerUserId, string? reviewerUserName, string reason, DateTimeOffset nowUtc)
    {
        if (Status is not (PilotAuthorizationSubmissionStatus.ReviewPending
            or PilotAuthorizationSubmissionStatus.ApprovedForCredentialWindowPlanning
            or PilotAuthorizationSubmissionStatus.ApprovedForLimitedPilotExecutionPlanning))
        {
            throw new InvalidOperationException("Pilot authorization submission is not reviewable.");
        }

        EnsureDecisionTextIsSafe(reason);
        Status = PilotAuthorizationSubmissionStatus.Rejected;
        Review = Review.MarkDecision(reviewerUserId, reviewerUserName, Require(reason, nameof(reason), 1000), Status, nowUtc);
        UpdatedAt = nowUtc;
    }

    public void Revoke(Guid reviewerUserId, string? reviewerUserName, string reason, DateTimeOffset nowUtc)
    {
        if (Status is not (PilotAuthorizationSubmissionStatus.ApprovedForCredentialWindowPlanning
            or PilotAuthorizationSubmissionStatus.ApprovedForLimitedPilotExecutionPlanning))
        {
            throw new InvalidOperationException("Only planning approvals can be revoked.");
        }

        EnsureDecisionTextIsSafe(reason);
        Status = PilotAuthorizationSubmissionStatus.Revoked;
        Review = Review.MarkDecision(reviewerUserId, reviewerUserName, Require(reason, nameof(reason), 1000), Status, nowUtc);
        UpdatedAt = nowUtc;
    }

    public void Expire(Guid reviewerUserId, string? reviewerUserName, string? reason, DateTimeOffset nowUtc)
    {
        EnsureCanExpire();
        EnsureDecisionTextIsSafe(reason);
        Status = PilotAuthorizationSubmissionStatus.Expired;
        Review = Review
            .MarkDecision(reviewerUserId, reviewerUserName, reason, Status, nowUtc)
            .MarkExpired(nowUtc);
        UpdatedAt = nowUtc;
    }

    public void ExpireBySystem(DateTimeOffset nowUtc)
    {
        EnsureCanExpire();
        Status = PilotAuthorizationSubmissionStatus.Expired;
        Review = Review.MarkExpired(nowUtc);
        UpdatedAt = nowUtc;
    }

    private void ApplyDraft(
        string title,
        string businessPurpose,
        IReadOnlyCollection<string>? endpointCodes,
        int maxRows,
        int timeRangeDays,
        string dataOwner,
        string toolOwner,
        string finalOwner,
        string rollbackOwner,
        string emergencyOwner,
        string? evidenceSummary,
        string? rollbackSummary,
        string? businessScope,
        string? department,
        string? pilotOwner,
        DateTimeOffset? executionWindowStart,
        DateTimeOffset? executionWindowEnd,
        DateTimeOffset? rollbackWindowStart,
        DateTimeOffset? rollbackWindowEnd,
        string? credentialOwner,
        string? secretStorageMode,
        string? secretReferenceNameHash,
        string? postRunAuditArchiveFormat,
        string? signedApprovalRef,
        DateTimeOffset? expiresAt,
        DateTimeOffset nowUtc)
    {
        PilotAuthorizationSensitiveContentGuard.ThrowIfUnsafe(
        [
            new("title", title),
            new("businessPurpose", businessPurpose),
            new("endpointCodes", string.Join("\n", endpointCodes ?? Array.Empty<string>())),
            new("dataOwner", dataOwner),
            new("toolOwner", toolOwner),
            new("finalOwner", finalOwner),
            new("rollbackOwner", rollbackOwner),
            new("emergencyOwner", emergencyOwner),
            new("evidenceSummary", evidenceSummary),
            new("rollbackSummary", rollbackSummary),
            new("businessScope", businessScope),
            new("department", department),
            new("pilotOwner", pilotOwner),
            new("credentialOwner", credentialOwner),
            new("secretStorageMode", secretStorageMode),
            new("secretReferenceNameHash", secretReferenceNameHash),
            new("postRunAuditArchiveFormat", postRunAuditArchiveFormat),
            new("signedApprovalRef", signedApprovalRef)
        ]);

        if (expiresAt is null)
        {
            throw new ArgumentException("expiresAt is required.", nameof(expiresAt));
        }

        Title = Require(title, nameof(title), 200);
        BusinessPurpose = Require(businessPurpose, nameof(businessPurpose), 1000);
        EndpointCodes = NormalizeStrings(endpointCodes, 120);
        MaxRows = maxRows;
        TimeRangeDays = timeRangeDays;
        DataOwner = NormalizeOptional(dataOwner, 160) ?? string.Empty;
        ToolOwner = NormalizeOptional(toolOwner, 160) ?? string.Empty;
        FinalOwner = NormalizeOptional(finalOwner, 160) ?? string.Empty;
        RollbackPlan = new PilotRollbackPlan(
            NormalizeOptional(rollbackOwner, 160) ?? string.Empty,
            NormalizeOptional(emergencyOwner, 160) ?? string.Empty,
            NormalizeOptional(rollbackSummary, 1000));
        EvidenceArchive = new PilotEvidenceArchive(
            NormalizeOptional(evidenceSummary, 1000),
            []);
        MaterialIntake = new PilotAuthorizationMaterialIntake(
            NormalizeOptional(businessScope, 500),
            NormalizeOptional(department, 160),
            NormalizeOptional(pilotOwner, 160),
            executionWindowStart,
            executionWindowEnd,
            rollbackWindowStart,
            rollbackWindowEnd,
            NormalizeOptional(credentialOwner, 160),
            NormalizeOptional(secretStorageMode, 120),
            NormalizeOptional(secretReferenceNameHash, 160),
            NormalizeOptional(postRunAuditArchiveFormat, 160),
            NormalizeOptional(signedApprovalRef, 240));
        ExpiresAt = expiresAt;
        MachineValidationStatus = "NotEvaluated";
        MachineRejectedReasons = [];
        UpdatedAt = nowUtc;
    }

    private void EnsureCanExpire()
    {
        if (Status is PilotAuthorizationSubmissionStatus.Rejected
            or PilotAuthorizationSubmissionStatus.Revoked
            or PilotAuthorizationSubmissionStatus.Expired)
        {
            throw new InvalidOperationException("Terminal Pilot authorization submission cannot expire again.");
        }
    }

    private static void EnsureDecisionTextIsSafe(params string?[] values)
    {
        PilotAuthorizationSensitiveContentGuard.ThrowIfUnsafe(
            values.Select((value, index) => new PilotAuthorizationSensitiveField($"decisionText{index}", value)));
    }

    private void EnsureEditableDraft()
    {
        if (Status is not (PilotAuthorizationSubmissionStatus.Draft or PilotAuthorizationSubmissionStatus.MachineRejected))
        {
            throw new InvalidOperationException("Pilot authorization submission can only be edited before review.");
        }
    }

    private void EnsureStatus(PilotAuthorizationSubmissionStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Pilot authorization submission status must be {expected}.");
        }
    }

    internal static string Require(string? value, string paramName, int maxLength)
    {
        var normalized = NormalizeOptional(value, maxLength);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized;
    }

    internal static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }

    internal static string[] NormalizeStrings(IReadOnlyCollection<string>? values, int maxLength)
    {
        return (values ?? [])
            .Select(value => NormalizeOptional(value, maxLength))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
