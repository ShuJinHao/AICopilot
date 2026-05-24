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
