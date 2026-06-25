using AICopilot.AiGatewayService.ApprovalPolicies;
using AICopilot.AiGatewayService.Commands.ConversationTemplates;
using AICopilot.AiGatewayService.Commands.LanguageModels;
using AICopilot.AiGatewayService.Queries.ConversationTemplates;
using AICopilot.AiGatewayService.Queries.LanguageModels;
using AICopilot.AiGatewayService.Queries.Runtime;
using AICopilot.AiGatewayService.RoutingModels;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.HttpApi.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;

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

    [HttpGet("model-pools")]
    public async Task<IActionResult> GetModelPools()
    {
        return ReturnResult(await Sender.Send(new GetModelPoolsQuery()));
    }

    [HttpGet("runtime-settings")]
    public async Task<IActionResult> GetRuntimeSettings()
    {
        return ReturnResult(await Sender.Send(new GetChatRuntimeSettingsQuery()));
    }

    [HttpGet("cloud-readonly/status")]
    public async Task<IActionResult> GetCloudReadonlyStatus()
    {
        return ReturnResult(await Sender.Send(new GetCloudReadonlyStatusQuery()));
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
}
