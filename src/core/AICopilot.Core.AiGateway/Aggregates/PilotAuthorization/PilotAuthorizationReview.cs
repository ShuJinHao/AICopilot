namespace AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;

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
