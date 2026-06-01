using AICopilot.AiGatewayService.PilotAuthorization;
using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.HttpApi.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/aigateway")]
[Authorize]
public class AiGatewayTrialPilotController(ISender sender) : ApiControllerBase(sender)
{
    [HttpGet("trial-operations/campaigns")]
    public async Task<IActionResult> GetTrialCampaigns()
    {
        return ReturnResult(await Sender.Send(new GetTrialCampaignsQuery()));
    }

    [HttpGet("trial-operations/campaigns/{id:guid}")]
    public async Task<IActionResult> GetTrialCampaignDetail(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetTrialCampaignDetailQuery(id)));
    }

    [HttpPost("trial-operations/campaigns")]
    public async Task<IActionResult> CreateTrialCampaign(CreateTrialCampaignCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPatch("trial-operations/campaigns/{id:guid}/status")]
    public async Task<IActionResult> UpdateTrialCampaignStatus(Guid id, UpdateTrialCampaignStatusRequest request)
    {
        return ReturnResult(await Sender.Send(new UpdateTrialCampaignStatusCommand(id, request.Status)));
    }

    [HttpPost("trial-operations/campaigns/{id:guid}/attach-task")]
    public async Task<IActionResult> AttachAgentTaskToTrialCampaign(Guid id, AttachAgentTaskToTrialCampaignRequest request)
    {
        return ReturnResult(await Sender.Send(new AttachAgentTaskToTrialCampaignCommand(
            id,
            request.TaskId,
            request.ScenarioId,
            request.TrialMode)));
    }

    [HttpPost("trial-operations/campaigns/{id:guid}/risks")]
    public async Task<IActionResult> UpsertTrialRiskIssue(Guid id, UpsertTrialRiskIssueRequest request)
    {
        return ReturnResult(await Sender.Send(new UpsertTrialRiskIssueCommand(
            id,
            request.IssueId,
            request.Severity,
            request.Category,
            request.Status,
            request.Owner,
            request.SourceRef,
            request.ResolutionHash)));
    }

    [HttpPost("trial-operations/campaigns/{id:guid}/readiness")]
    public async Task<IActionResult> RunPilotReadinessEvaluation(Guid id)
    {
        return ReturnResult(await Sender.Send(new RunPilotReadinessEvaluationCommand(id)));
    }

    [HttpPost("trial-operations/campaigns/{id:guid}/evidence-package")]
    public async Task<IActionResult> GenerateTrialEvidencePackage(Guid id)
    {
        return ReturnResult(await Sender.Send(new GenerateTrialEvidencePackageCommand(id)));
    }

    [HttpGet("pilot-authorization/submissions")]
    public async Task<IActionResult> GetPilotAuthorizationSubmissions()
    {
        return ReturnResult(await Sender.Send(new GetPilotAuthorizationSubmissionsQuery()));
    }

    [HttpGet("pilot-authorization/submissions/{id:guid}")]
    public async Task<IActionResult> GetPilotAuthorizationSubmission(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetPilotAuthorizationSubmissionQuery(id)));
    }

    [HttpGet("pilot-authorization/submissions/{id:guid}/audit-timeline")]
    public async Task<IActionResult> GetPilotAuthorizationAuditTimeline(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetPilotAuthorizationAuditTimelineQuery(id)));
    }

    [HttpPost("pilot-authorization/submissions")]
    public async Task<IActionResult> CreatePilotAuthorizationSubmission(PilotAuthorizationSubmissionUpsertRequest request)
    {
        return ReturnResult(await Sender.Send(new CreatePilotAuthorizationSubmissionCommand(
            request.Title,
            request.BusinessPurpose,
            request.EndpointCodes,
            request.MaxRows,
            request.TimeRangeDays,
            request.DataOwner,
            request.ToolOwner,
            request.FinalOwner,
            request.RollbackOwner,
            request.EmergencyOwner,
            request.EvidenceSummary,
            request.RollbackSummary,
            request.BusinessScope,
            request.Department,
            request.PilotOwner,
            request.ExecutionWindowStart,
            request.ExecutionWindowEnd,
            request.RollbackWindowStart,
            request.RollbackWindowEnd,
            request.CredentialOwner,
            request.SecretStorageMode,
            request.SecretReferenceNameHash,
            request.PostRunAuditArchiveFormat,
            request.SignedApprovalRef,
            request.ExpiresAt)));
    }

    [HttpPut("pilot-authorization/submissions/{id:guid}")]
    public async Task<IActionResult> UpdatePilotAuthorizationSubmission(
        Guid id,
        PilotAuthorizationSubmissionUpsertRequest request)
    {
        return ReturnResult(await Sender.Send(new UpdatePilotAuthorizationSubmissionCommand(
            id,
            request.Title,
            request.BusinessPurpose,
            request.EndpointCodes,
            request.MaxRows,
            request.TimeRangeDays,
            request.DataOwner,
            request.ToolOwner,
            request.FinalOwner,
            request.RollbackOwner,
            request.EmergencyOwner,
            request.EvidenceSummary,
            request.RollbackSummary,
            request.BusinessScope,
            request.Department,
            request.PilotOwner,
            request.ExecutionWindowStart,
            request.ExecutionWindowEnd,
            request.RollbackWindowStart,
            request.RollbackWindowEnd,
            request.CredentialOwner,
            request.SecretStorageMode,
            request.SecretReferenceNameHash,
            request.PostRunAuditArchiveFormat,
            request.SignedApprovalRef,
            request.ExpiresAt)));
    }

    [HttpPost("pilot-authorization/submissions/{id:guid}/submit")]
    public async Task<IActionResult> SubmitPilotAuthorizationSubmission(Guid id)
    {
        return ReturnResult(await Sender.Send(new SubmitPilotAuthorizationSubmissionCommand(id)));
    }

    [HttpPost("pilot-authorization/submissions/{id:guid}/approve-credential-window-planning")]
    public async Task<IActionResult> ApprovePilotAuthorizationCredentialWindowPlanning(
        Guid id,
        PilotAuthorizationDecisionRequest request)
    {
        return ReturnResult(await Sender.Send(new ApprovePilotAuthorizationCredentialWindowPlanningCommand(
            id,
            request.Reason,
            request.CredentialWindowPlanningSummary)));
    }

    [HttpPost("pilot-authorization/submissions/{id:guid}/approve-limited-pilot-execution-planning")]
    public async Task<IActionResult> ApprovePilotAuthorizationLimitedPilotExecutionPlanning(
        Guid id,
        PilotAuthorizationDecisionRequest request)
    {
        return ReturnResult(await Sender.Send(new ApprovePilotAuthorizationLimitedPilotExecutionPlanningCommand(
            id,
            request.Reason)));
    }

    [HttpPost("pilot-authorization/submissions/{id:guid}/reject")]
    public async Task<IActionResult> RejectPilotAuthorizationSubmission(
        Guid id,
        PilotAuthorizationDecisionRequest request)
    {
        return ReturnResult(await Sender.Send(new RejectPilotAuthorizationSubmissionCommand(
            id,
            request.Reason ?? "Rejected by Pilot authorization reviewer.")));
    }

    [HttpPost("pilot-authorization/submissions/{id:guid}/revoke")]
    public async Task<IActionResult> RevokePilotAuthorizationSubmission(
        Guid id,
        PilotAuthorizationDecisionRequest request)
    {
        return ReturnResult(await Sender.Send(new RevokePilotAuthorizationSubmissionCommand(
            id,
            request.Reason ?? "Revoked by Pilot authorization reviewer.")));
    }

    [HttpPost("pilot-authorization/submissions/{id:guid}/expire")]
    public async Task<IActionResult> ExpirePilotAuthorizationSubmission(
        Guid id,
        PilotAuthorizationDecisionRequest request)
    {
        return ReturnResult(await Sender.Send(new ExpirePilotAuthorizationSubmissionCommand(
            id,
            request.Reason)));
    }
}
