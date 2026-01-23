using AICopilot.AiGatewayService.Commands.ConversationTemplates;
using AICopilot.AiGatewayService.Commands.LanguageModels;
using AICopilot.AiGatewayService.Commands.Sessions;
using AICopilot.AiGatewayService.Queries.ConversationTemplates;
using AICopilot.AiGatewayService.Queries.LanguageModels;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.HttpApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/aigateway")]
public class AiGatewayController : ApiControllerBase
{
    [HttpPost("language-model")]
    public async Task<IActionResult> CreateLanguageModel(CreateLanguageModelCommand command)
    {
        var result = await Sender.Send(command);

        return ReturnResult(result);
    }

    [HttpDelete("language-model")]
    public async Task<IActionResult> DeleteLanguageModel(DeleteLanguageModelCommand command)
    {
        var result = await Sender.Send(command);

        return ReturnResult(result);
    }

    [HttpGet("language-model/list")]
    public async Task<IActionResult> GetListLanguageModels()
    {
        var result = await Sender.Send(new GetListLanguageModelsQuery());
        return ReturnResult(result);
    }

    [HttpPost("conversation-template")]
    public async Task<IActionResult> CreateConversationTemplate(CreateConversationTemplateCommand command)
    {
        var result = await Sender.Send(command);
        return ReturnResult(result);
    }

    [HttpDelete("conversation-template")]
    public async Task<IActionResult> DeleteConversationTemplate(DeleteConversationTemplateCommand command)
    {
        var result = await Sender.Send(command);
        return ReturnResult(result);
    }

    [HttpGet("conversation-template")]
    public async Task<IActionResult> GetConversationTemplate(GetConversationTemplateQuery query)
    {
        var result = await Sender.Send(query);
        return ReturnResult(result);
    }

    [HttpGet("conversation-template/list")]
    public async Task<IActionResult> GetListConversationTemplates()
    {
        var result = await Sender.Send(new GetListConversationTemplatesQuery());
        return ReturnResult(result);
    }

    [HttpPost("session")]
    public async Task<IActionResult> CreateSession(CreateSessionCommand command)
    {
        var result = await Sender.Send(command);
        return ReturnResult(result);
    }

    [HttpDelete("session")]
    public async Task<IActionResult> DeleteSession(DeleteSessionCommand command)
    {
        var result = await Sender.Send(command);
        return ReturnResult(result);
    }

    [HttpGet("session/list")]
    public async Task<IActionResult> GetListSessions()
    {
        var result = await Sender.Send(new GetListSessionsQuery());
        return ReturnResult(result);
    }

    [HttpPost("session/SendUserMessages")]
    public async Task SendUserMessages(SendUserMessageCommand command)
    {
        var stream = await Sender.Send(command);

        Response.StatusCode = 200;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        await foreach (var token in stream)
        {
            var chunk = new
            {
                content = token
            };

            var json = JsonSerializer.Serialize(chunk);

            await Response.WriteAsync($"data: {json}\n\n");
            await Response.Body.FlushAsync();
        }
    }
}