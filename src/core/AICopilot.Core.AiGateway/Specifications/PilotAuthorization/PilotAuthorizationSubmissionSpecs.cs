using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.PilotAuthorization;

public sealed class PilotAuthorizationSubmissionByIdSpec : Specification<PilotAuthorizationSubmission>
{
    public PilotAuthorizationSubmissionByIdSpec(PilotAuthorizationSubmissionId id)
    {
        FilterCondition = submission => submission.Id == id;
    }
}

public sealed class PilotAuthorizationSubmissionListSpec : Specification<PilotAuthorizationSubmission>
{
    public PilotAuthorizationSubmissionListSpec(Guid? requestedByUserId = null)
    {
        if (requestedByUserId is { } userId)
        {
            FilterCondition = submission => submission.RequestedByUserId == userId;
        }

        SetOrderByDescending(submission => submission.UpdatedAt);
    }
}

public sealed class PilotAuthorizationExpiredOpenSubmissionsSpec : Specification<PilotAuthorizationSubmission>
{
    public PilotAuthorizationExpiredOpenSubmissionsSpec(DateTimeOffset nowUtc)
    {
        FilterCondition = submission =>
            submission.ExpiresAt != null
            && submission.ExpiresAt < nowUtc
            && submission.Status != PilotAuthorizationSubmissionStatus.Rejected
            && submission.Status != PilotAuthorizationSubmissionStatus.Revoked
            && submission.Status != PilotAuthorizationSubmissionStatus.Expired;

        SetOrderBy(submission => submission.ExpiresAt ?? DateTimeOffset.MaxValue);
    }
}
