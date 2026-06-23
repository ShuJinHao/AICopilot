using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Commands.Sessions;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.HttpApi.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/aigateway")]
[Authorize]
public class AiGatewaySessionController(ISender sender) : ApiControllerBase(sender)
{
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

    [HttpGet("session/timeline")]
    public async Task<IActionResult> GetSessionTimeline([FromQuery] GetSessionTimelineQuery query)
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
