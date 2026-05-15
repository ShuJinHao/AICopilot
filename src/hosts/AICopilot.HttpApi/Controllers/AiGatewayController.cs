using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.ApprovalPolicies;
using AICopilot.AiGatewayService.Commands.ConversationTemplates;
using AICopilot.AiGatewayService.Commands.LanguageModels;
using AICopilot.AiGatewayService.Commands.Sessions;
using AICopilot.AiGatewayService.Queries.ConversationTemplates;
using AICopilot.AiGatewayService.Queries.LanguageModels;
using AICopilot.AiGatewayService.Queries.Runtime;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.AiGatewayService.RoutingModels;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Uploads;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.HttpApi.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/aigateway")]
[Authorize]
public class AiGatewayController(ISender sender) : ApiControllerBase(sender)
{
    private const long MaxAiGatewayUploadBytes = 50_000_000;

    [HttpPost("language-model")]
    public async Task<IActionResult> CreateLanguageModel(CreateLanguageModelCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("language-model")]
    public async Task<IActionResult> UpdateLanguageModel(UpdateLanguageModelCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("language-model")]
    public async Task<IActionResult> DeleteLanguageModel(DeleteLanguageModelCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("language-model")]
    public async Task<IActionResult> GetLanguageModel([FromQuery] GetLanguageModelQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("language-model/list")]
    public async Task<IActionResult> GetListLanguageModels()
    {
        return ReturnResult(await Sender.Send(new GetListLanguageModelsQuery()));
    }

    [HttpGet("language-model/chat-options")]
    public async Task<IActionResult> GetSelectableChatModels()
    {
        return ReturnResult(await Sender.Send(new GetSelectableChatModelsQuery()));
    }

    [HttpPost("language-model/test")]
    public async Task<IActionResult> TestLanguageModel(TestLanguageModelCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("routing-model")]
    public async Task<IActionResult> CreateRoutingModel(CreateRoutingModelConfigurationCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("routing-model")]
    public async Task<IActionResult> UpdateRoutingModel(UpdateRoutingModelConfigurationCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("routing-model")]
    public async Task<IActionResult> DeleteRoutingModel(DeleteRoutingModelConfigurationCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("routing-model/activate")]
    public async Task<IActionResult> ActivateRoutingModel(ActivateRoutingModelConfigurationCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("routing-model")]
    public async Task<IActionResult> GetRoutingModel([FromQuery] GetRoutingModelConfigurationQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("routing-model/list")]
    public async Task<IActionResult> GetListRoutingModels()
    {
        return ReturnResult(await Sender.Send(new GetListRoutingModelConfigurationsQuery()));
    }

    [HttpGet("provider-reliability")]
    public async Task<IActionResult> GetProviderReliability()
    {
        return ReturnResult(await Sender.Send(new GetProviderReliabilityQuery()));
    }

    [HttpGet("runtime-settings")]
    public async Task<IActionResult> GetRuntimeSettings()
    {
        return ReturnResult(await Sender.Send(new GetChatRuntimeSettingsQuery()));
    }

    [HttpPut("runtime-settings")]
    public async Task<IActionResult> UpdateRuntimeSettings(UpdateChatRuntimeSettingsCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("conversation-template")]
    public async Task<IActionResult> CreateConversationTemplate(CreateConversationTemplateCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("conversation-template")]
    public async Task<IActionResult> UpdateConversationTemplate(UpdateConversationTemplateCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("conversation-template")]
    public async Task<IActionResult> DeleteConversationTemplate(DeleteConversationTemplateCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("conversation-template")]
    public async Task<IActionResult> GetConversationTemplate([FromQuery] GetConversationTemplateQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("conversation-template/list")]
    public async Task<IActionResult> GetListConversationTemplates()
    {
        return ReturnResult(await Sender.Send(new GetListConversationTemplatesQuery()));
    }

    [HttpPost("conversation-template/reset-builtins")]
    public async Task<IActionResult> ResetBuiltInConversationTemplates(ResetBuiltInConversationTemplatesCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("approval-policy")]
    public async Task<IActionResult> CreateApprovalPolicy(CreateApprovalPolicyCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("approval-policy")]
    public async Task<IActionResult> UpdateApprovalPolicy(UpdateApprovalPolicyCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("approval-policy")]
    public async Task<IActionResult> DeleteApprovalPolicy(DeleteApprovalPolicyCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("approval-policy")]
    public async Task<IActionResult> GetApprovalPolicy([FromQuery] GetApprovalPolicyQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("approval-policy/list")]
    public async Task<IActionResult> GetListApprovalPolicies()
    {
        return ReturnResult(await Sender.Send(new GetListApprovalPoliciesQuery()));
    }

    [HttpPost("session")]
    public async Task<IActionResult> CreateSession(CreateSessionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("session")]
    public async Task<IActionResult> DeleteSession(DeleteSessionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("session/title")]
    public async Task<IActionResult> RenameSession(RenameSessionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("session")]
    public async Task<IActionResult> GetSession([FromQuery] GetSessionQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("session/list")]
    public async Task<IActionResult> GetListSessions()
    {
        return ReturnResult(await Sender.Send(new GetListSessionsQuery()));
    }

    [HttpPost("agent/task/plan")]
    public async Task<IActionResult> PlanAgentTask(PlanAgentTaskCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/task/approve-plan")]
    public async Task<IActionResult> ApproveAgentTaskPlan(ApproveAgentTaskPlanCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/task/run")]
    public async Task<IActionResult> RunAgentTask(RunAgentTaskCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/task/cancel")]
    public async Task<IActionResult> CancelAgentTask(CancelAgentTaskCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("agent/task")]
    public async Task<IActionResult> GetAgentTask([FromQuery] GetAgentTaskQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("agent/task/by-session")]
    public async Task<IActionResult> GetAgentTasksBySession([FromQuery] GetListAgentTasksBySessionQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("agent/approval/pending")]
    public async Task<IActionResult> GetPendingAgentApprovals()
    {
        return ReturnResult(await Sender.Send(new GetPendingAgentApprovalsQuery()));
    }

    [HttpGet("agent/task/{id:guid}/approvals")]
    public async Task<IActionResult> GetAgentTaskApprovals(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetAgentTaskApprovalsQuery(id)));
    }

    [HttpGet("agent/task/{id:guid}/audit-summary")]
    public async Task<IActionResult> GetAgentTaskAuditSummary(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetAgentTaskAuditSummaryQuery(id)));
    }

    [HttpPost("agent/approval/{id:guid}/approve")]
    public async Task<IActionResult> ApproveAgentApproval(Guid id, AgentApprovalDecisionRequest request)
    {
        return ReturnResult(await Sender.Send(new ApproveAgentApprovalCommand(id, request.Comment)));
    }

    [HttpPost("agent/approval/{id:guid}/reject")]
    public async Task<IActionResult> RejectAgentApproval(Guid id, AgentApprovalDecisionRequest request)
    {
        return ReturnResult(await Sender.Send(new RejectAgentApprovalCommand(id, request.Comment)));
    }

    [HttpPost("upload")]
    [RequestSizeLimit(MaxAiGatewayUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAiGatewayUploadBytes)]
    public async Task<IActionResult> Upload(
        [FromForm] string scope,
        IFormFile? file,
        [FromForm] Guid? sessionId = null,
        [FromForm] Guid? agentTaskId = null,
        [FromForm] Guid? knowledgeBaseId = null)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "File is required." });
        }

        if (file.Length > MaxAiGatewayUploadBytes)
        {
            return BadRequest(new { error = "File exceeds the 50 MB upload limit." });
        }

        await using var stream = file.OpenReadStream();
        var command = new UploadRecordCommand(
            scope,
            new AiGatewayUploadStream(
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                file.Length,
                stream),
            sessionId,
            agentTaskId,
            knowledgeBaseId);
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("upload/list")]
    public async Task<IActionResult> GetUploadRecords([FromQuery] GetListUploadRecordsQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("workspace/{code}")]
    public async Task<IActionResult> GetWorkspace(string code)
    {
        return ReturnResult(await Sender.Send(new GetArtifactWorkspaceQuery(code)));
    }

    [HttpGet("workspace-settings")]
    public async Task<IActionResult> GetWorkspaceSettings()
    {
        return ReturnResult(await Sender.Send(new GetArtifactWorkspaceSettingsQuery()));
    }

    [HttpPost("workspace/{code}/finalize")]
    public async Task<IActionResult> FinalizeWorkspace(string code)
    {
        return ReturnResult(await Sender.Send(new FinalizeArtifactWorkspaceCommand(code)));
    }

    [HttpGet("artifact/{id:guid}/download")]
    public async Task<IActionResult> DownloadArtifact(Guid id)
    {
        var result = await Sender.Send(new DownloadArtifactQuery(id));
        if (!result.IsSuccess || result.Value is null)
        {
            return ReturnResult(result);
        }

        return File(result.Value.Stream, result.Value.MimeType, result.Value.FileName);
    }

    [HttpPut("session/safety-attestation")]
    public async Task<IActionResult> UpdateSessionSafetyAttestation(UpdateSessionSafetyAttestationCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("chat-message/list")]
    public async Task<IActionResult> GetListChatMessages([FromQuery] GetListChatMessageHistoryQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpPost("chat")]
    [EnableRateLimiting("chat")]
    public IResult Chat(ChatStreamRequest request)
    {
        var stream = Sender.CreateStream(request);
        return Results.ServerSentEvents(stream);
    }

    [HttpPost("approval/decision")]
    [EnableRateLimiting("chat")]
    public IResult DecideApproval(ApprovalDecisionStreamRequest request)
    {
        var stream = Sender.CreateStream(request);
        return Results.ServerSentEvents(stream);
    }

    [HttpGet("approval/pending")]
    public async Task<IActionResult> GetPendingApprovals([FromQuery] GetPendingApprovalsQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }
}
