using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.HttpApi.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/data-analysis")]
[Authorize]
public class DataAnalysisController(ISender sender) : ApiControllerBase(sender)
{
    [HttpPost("business-database")]
    public async Task<IActionResult> CreateBusinessDatabase(CreateBusinessDatabaseCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("business-database")]
    public async Task<IActionResult> UpdateBusinessDatabase(UpdateBusinessDatabaseCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("business-database")]
    public async Task<IActionResult> DeleteBusinessDatabase(DeleteBusinessDatabaseCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("business-database")]
    public async Task<IActionResult> GetBusinessDatabase([FromQuery] GetBusinessDatabaseQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("business-database/list")]
    public async Task<IActionResult> GetListBusinessDatabases()
    {
        return ReturnResult(await Sender.Send(new GetListBusinessDatabasesQuery()));
    }

    [HttpGet("semantic-source/status")]
    public async Task<IActionResult> GetSemanticSourceStatus()
    {
        return ReturnResult(await Sender.Send(new GetSemanticSourceStatusQuery()));
    }
}
