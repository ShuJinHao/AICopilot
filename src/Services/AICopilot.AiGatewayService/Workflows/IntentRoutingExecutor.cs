using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Queries.Sessions;
using MediatR;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AICopilot.AiGatewayService.Workflows;

public class IntentRoutingExecutor(IntentRoutingAgentBuilder agentBuilder, IServiceProvider serviceProvider) :
    ReflectingExecutor<IntentRoutingExecutor>("IntentRoutingExecutor"),
    IMessageHandler<ChatStreamRequest, List<IntentResult>>
{
    public async ValueTask<List<IntentResult>> HandleAsync(ChatStreamRequest request, IWorkflowContext context,
        CancellationToken cancellationToken = new())
    {
        try
        {
            await context.QueueStateUpdateAsync("ChatStreamRequest", request, "Chat", cancellationToken: cancellationToken);

            var scope = serviceProvider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var result = await mediator.Send(new GetListChatMessagesQuery(request.SessionId, 4), cancellationToken);
            var history = result.Value!;

            history.Add(new ChatMessage(ChatRole.User, request.Message));

            var agent = await agentBuilder.BuildAsync();
            var response = await agent.RunAsync(
                history,
                cancellationToken: cancellationToken);

            await context.AddEventAsync(new AgentRunResponseEvent(Id, response), cancellationToken);

            var intentResults = response.Deserialize<List<IntentResult>>(JsonSerializerOptions.Web);
            return intentResults;
        }
        catch (Exception e)
        {
            await context.AddEventAsync(new ExecutorFailedEvent(Id, e), cancellationToken);
            throw;
        }
    }
}