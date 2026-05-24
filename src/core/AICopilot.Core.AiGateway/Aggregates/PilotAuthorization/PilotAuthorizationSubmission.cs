using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;

public enum PilotAuthorizationSubmissionStatus
{
    Draft = 0,
    Submitted = 1,
    MachineRejected = 2,
    ReviewPending = 3,
    ApprovedForCredentialWindowPlanning = 4,
    ApprovedForLimitedPilotExecutionPlanning = 5,
    Rejected = 6,
    Expired = 7,
    Revoked = 8
}

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

        Status = PilotAuthorizationSubmissionStatus.Revoked;
        Review = Review.MarkDecision(reviewerUserId, reviewerUserName, Require(reason, nameof(reason), 1000), Status, nowUtc);
        UpdatedAt = nowUtc;
    }

    public void Expire(DateTimeOffset nowUtc)
    {
        if (Status is PilotAuthorizationSubmissionStatus.Rejected
            or PilotAuthorizationSubmissionStatus.Revoked
            or PilotAuthorizationSubmissionStatus.Expired)
        {
            throw new InvalidOperationException("Terminal Pilot authorization submission cannot expire again.");
        }

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
        DateTimeOffset nowUtc)
    {
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
        MachineValidationStatus = "NotEvaluated";
        MachineRejectedReasons = [];
        UpdatedAt = nowUtc;
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

public sealed record PilotAuthorizationMachineValidationResult(
    bool IsAccepted,
    IReadOnlyCollection<string> RejectedReasons)
{
    public static PilotAuthorizationMachineValidationResult Accepted() => new(true, []);

    public static PilotAuthorizationMachineValidationResult Rejected(IReadOnlyCollection<string> rejectedReasons) =>
        new(false, rejectedReasons);
}

public sealed record PilotAuthorizationReview(
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewStartedAt,
    Guid? LastReviewerUserId,
    string? LastReviewerUserName,
    string? LastDecisionReason,
    string? LastDecisionStatus,
    DateTimeOffset? LastDecisionAt,
    DateTimeOffset? ExpiredAt)
{
    public static PilotAuthorizationReview Empty() => new(null, null, null, null, null, null, null, null);

    public PilotAuthorizationReview MarkSubmitted(DateTimeOffset nowUtc) => this with { SubmittedAt = nowUtc };

    public PilotAuthorizationReview MarkReviewStarted(DateTimeOffset nowUtc) => this with { ReviewStartedAt = nowUtc };

    public PilotAuthorizationReview MarkDecision(
        Guid reviewerUserId,
        string? reviewerUserName,
        string? reason,
        PilotAuthorizationSubmissionStatus decisionStatus,
        DateTimeOffset nowUtc)
    {
        if (reviewerUserId == Guid.Empty)
        {
            throw new ArgumentException("Reviewer user id is required.", nameof(reviewerUserId));
        }

        return this with
        {
            LastReviewerUserId = reviewerUserId,
            LastReviewerUserName = PilotAuthorizationSubmission.NormalizeOptional(reviewerUserName, 160),
            LastDecisionReason = PilotAuthorizationSubmission.NormalizeOptional(reason, 1000),
            LastDecisionStatus = decisionStatus.ToString(),
            LastDecisionAt = nowUtc
        };
    }

    public PilotAuthorizationReview MarkExpired(DateTimeOffset nowUtc) => this with { ExpiredAt = nowUtc };
}

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
