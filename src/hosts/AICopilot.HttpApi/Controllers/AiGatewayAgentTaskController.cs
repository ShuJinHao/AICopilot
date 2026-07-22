using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.HttpApi.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/aigateway")]
[Authorize]
public class AiGatewayAgentTaskController(ISender sender) : ApiControllerBase(sender)
{
    [HttpPost("agent/task/plan-stream")]
    [EnableRateLimiting("chat")]
    public IResult PlanAgentTaskStream(PlanAgentTaskStreamRequest request)
    {
        var stream = Sender.CreateStream(request);
        return Results.ServerSentEvents(stream);
    }

    [HttpPost("agent/task/approve-plan")]
    public async Task<IActionResult> ApproveAgentTaskPlan(ApproveAgentTaskPlanCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/task/reject-plan")]
    public async Task<IActionResult> RejectAgentTaskPlan(RejectAgentTaskPlanCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/task/run")]
    public async Task<IActionResult> RunAgentTask(RunAgentTaskCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/task/retry")]
    public async Task<IActionResult> RetryAgentTask(RetryAgentTaskCommand command)
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

    [HttpGet("agent/task/{id:guid}/tool-executions")]
    public async Task<IActionResult> GetAgentTaskToolExecutions(
        Guid id,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? toolCode = null)
    {
        return ReturnResult(await Sender.Send(new GetAgentTaskToolExecutionsQuery(
            id,
            pageIndex,
            pageSize,
            status,
            toolCode)));
    }

    [HttpGet("agent/task/{id:guid}/run-attempts")]
    public async Task<IActionResult> GetAgentTaskRunAttempts(
        Guid id,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20)
    {
        return ReturnResult(await Sender.Send(new GetAgentTaskRunAttemptsQuery(
            id,
            pageIndex,
            pageSize)));
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
}
