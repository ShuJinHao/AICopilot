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
