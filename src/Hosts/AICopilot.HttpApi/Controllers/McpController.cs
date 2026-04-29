using AICopilot.HttpApi.Infrastructure;
using AICopilot.McpService.McpServers;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/mcp")]
[Authorize]
public class McpController(ISender sender) : ApiControllerBase(sender)
{
    [HttpPost("server")]
    public async Task<IActionResult> CreateServer(CreateMcpServerCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("server")]
    public async Task<IActionResult> UpdateServer(UpdateMcpServerCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("server")]
    public async Task<IActionResult> DeleteServer(DeleteMcpServerCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("server")]
    public async Task<IActionResult> GetServer([FromQuery] GetMcpServerQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("server/list")]
    public async Task<IActionResult> GetListServers()
    {
        return ReturnResult(await Sender.Send(new GetListMcpServersQuery()));
    }
}
