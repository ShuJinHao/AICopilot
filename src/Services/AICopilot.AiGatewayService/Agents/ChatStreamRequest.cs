using AICopilot.AiGatewayService.Workflows;
using AICopilot.Core.AiGateway.Aggregates.Sessions; // 引用实体
using AICopilot.Services.Common.Attributes;
using AICopilot.Services.Common.Contracts;
using AICopilot.Services.Common.Helper;
using AICopilot.SharedKernel.Repository; // 引用仓储接口
using MediatR;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AICopilot.AiGatewayService.Agents;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChunkType
{
    Error,
    Text,
    Widget,
    FunctionCall,
    FunctionResult
}

public record ChatChunk(string Source, ChunkType Type, string Content);

[AuthorizeRequirement("AiGateway.Chat")]
public record ChatStreamRequest(Guid SessionId, string Message) : IStreamRequest<object>;

public class ChatStreamHandler(
    IDataQueryService queryService,
    [FromKeyedServices(nameof(IntentWorkflow))] Workflow workflow)
    : IStreamRequestHandler<ChatStreamRequest, object>
{
    public async IAsyncEnumerable<object> Handle(ChatStreamRequest request, CancellationToken cancellationToken)
    {
        if (!queryService.Sessions.Any(session => session.Id == request.SessionId))
        {
            throw new Exception("未找到会话");
        }

        await using var run = await InProcessExecution.StreamAsync(workflow, request, cancellationToken: cancellationToken);
        await foreach (var workflowEvent in run.WatchStreamAsync(cancellationToken))
        {
            switch (workflowEvent)
            {
                case ExecutorFailedEvent evt:
                    yield return new ChatChunk(evt.ExecutorId, ChunkType.Error, evt.Data.Message);
                    break;

                case AgentRunResponseEvent evt:
                    var evtText = $"""

                               ```json
                               {evt.Response.Text}
                               ```

                               """;
                    switch (evt.ExecutorId)
                    {
                        case "IntentRoutingExecutor":
                            yield return new ChatChunk(evt.ExecutorId, ChunkType.Text, evtText);
                            break;

                        case "DataAnalysisExecutor":
                            yield return new ChatChunk(evt.ExecutorId, ChunkType.Widget, evtText);
                            break;
                    }
                    break;

                case AgentRunUpdateEvent evt:
                    foreach (var evtContent in evt.Update.Contents)
                    {
                        switch (evtContent)
                        {
                            case TextContent content:
                                yield return new ChatChunk(evt.ExecutorId, ChunkType.Text, content.Text);
                                break;

                            case FunctionCallContent content:
                                var fun = new
                                {
                                    content.Name,
                                    content.Arguments
                                };
                                yield return new ChatChunk(evt.ExecutorId, ChunkType.FunctionCall,
                                    $"""

                                    ```json
                                    {fun.ToJson()}
                                    ```

                                    """);
                                break;

                            case FunctionResultContent content:
                                yield return new ChatChunk(evt.ExecutorId, ChunkType.FunctionResult,
                                    $"""

                                     ```
                                     {content.Result}
                                     ```

                                     """);
                                break;
                        }
                    }
                    break;
            }
        }
    }
}