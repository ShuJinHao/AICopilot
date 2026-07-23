using AICopilot.AiGatewayService.Tools;
using AICopilot.HttpApi.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/aigateway")]
[Authorize]
public class AiGatewayToolController(ISender sender) : ApiControllerBase(sender)
{
    [HttpGet("tools")]
    public async Task<IActionResult> GetToolRegistrations()
    {
        return ReturnResult(await Sender.Send(new GetListToolRegistrationsQuery()));
    }

    [HttpGet("tools/catalog")]
    public async Task<IActionResult> GetToolCatalog([FromQuery] GetToolCatalogQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("tools/{toolCode}")]
    public async Task<IActionResult> GetToolRegistration(string toolCode)
    {
        return ReturnResult(await Sender.Send(new GetToolRegistrationQuery(toolCode)));
    }

    [HttpPatch("tools/{toolCode}")]
    public async Task<IActionResult> UpdateToolRegistration(string toolCode, UpdateToolRegistrationRequest request)
    {
        return ReturnResult(await Sender.Send(new UpdateToolRegistrationCommand(
            toolCode,
            request.DisplayName,
            request.Description,
            request.InputSchemaJson,
            request.OutputSchemaJson,
            request.RiskLevel,
            request.RequiredPermission,
            request.RequiresApproval,
            request.IsEnabled,
            request.TimeoutSeconds,
            request.AuditLevel,
            request.Category,
            request.BusinessDomains,
            request.DataBoundary,
            request.IsVisibleToPlanner,
            request.IsExecutableByAgent,
            request.SchemaVersion,
            request.CatalogVersion,
            request.ApprovalPolicy)));
    }

    [HttpPost("tools/definition")]
    public async Task<IActionResult> UpsertToolDefinition(UpsertToolDefinitionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("tools/definition/activate")]
    public async Task<IActionResult> ActivateToolDefinitionVersion(ActivateToolDefinitionVersionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("tools/definition/disable")]
    public async Task<IActionResult> DisableToolDefinition(DisableToolDefinitionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }
}
