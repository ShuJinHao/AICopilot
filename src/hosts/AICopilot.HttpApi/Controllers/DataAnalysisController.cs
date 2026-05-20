using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.DataAnalysisService.SimulationBusiness;
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

    [HttpPost("business-database/query-readonly")]
    public async Task<IActionResult> ExecuteBusinessDatabaseReadonlyQuery(
        ExecuteBusinessDatabaseReadonlyQueryCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("business-database/text-to-sql/draft")]
    public async Task<IActionResult> GenerateBusinessTextToSql(
        GenerateBusinessTextToSqlCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("business-database/text-to-sql/execute")]
    public async Task<IActionResult> ExecuteBusinessTextToSql(
        ExecuteBusinessTextToSqlCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("simulation-business/seed-plan")]
    public async Task<IActionResult> GetSimulationBusinessSeedPlan(
        [FromQuery] GetSimulationBusinessSeedPlanQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("semantic-source/status")]
    public async Task<IActionResult> GetSemanticSourceStatus()
    {
        return ReturnResult(await Sender.Send(new GetSemanticSourceStatusQuery()));
    }
}
