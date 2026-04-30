using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.ApprovalPolicies;
using AICopilot.AiGatewayService.Commands.ConversationTemplates;
using AICopilot.AiGatewayService.Commands.LanguageModels;
using AICopilot.AiGatewayService.Commands.Sessions;
using AICopilot.AiGatewayService.Queries.ConversationTemplates;
using AICopilot.AiGatewayService.Queries.LanguageModels;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.HttpApi.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/aigateway")]
[Authorize]
public class AiGatewayController(ISender sender) : ApiControllerBase(sender)
{
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
