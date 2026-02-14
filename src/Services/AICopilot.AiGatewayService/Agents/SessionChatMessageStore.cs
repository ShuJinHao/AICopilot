using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Services.Common.Contracts;
using AICopilot.SharedKernel.Repository;
using MediatR;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AICopilot.AiGatewayService.Agents;

public record SessionSoreState(Guid SessionId, int MessageCount = 20);

public class SessionChatMessageStore : ChatMessageStore
{
    private readonly SessionSoreState? _sessionSoreState;
    private readonly IServiceProvider _serviceProvider;

    public SessionChatMessageStore(IServiceProvider serviceProvider, JsonElement storeState)
    {
        _serviceProvider = serviceProvider;

        try
        {
            // 兼容处理：支持解析对象或字符串格式的状态
            if (storeState.ValueKind == JsonValueKind.Object)
            {
                // 如果是嵌套结构 { "storeState": { ... } }
                if (storeState.TryGetProperty("storeState", out var innerState))
                {
                    _sessionSoreState = innerState.Deserialize<SessionSoreState>();
                }
                // 如果是直接对象 { "sessionId": "..." }
                else
                {
                    _sessionSoreState = storeState.Deserialize<SessionSoreState>();
                }
            }
            else if (storeState.ValueKind == JsonValueKind.String)
            {
                var json = storeState.GetString();
                if (!string.IsNullOrEmpty(json))
                {
                    _sessionSoreState = JsonSerializer.Deserialize<SessionSoreState>(json);
                }
            }
        }
        catch
        {
            // 忽略解析错误
        }
    }

    public override async Task<IEnumerable<ChatMessage>> GetMessagesAsync(CancellationToken cancellationToken = new())
    {
        if (_sessionSoreState == null) return [];
        var mediator = _serviceProvider.GetRequiredService<IMediator>();
        var query = new GetListChatMessagesQuery(_sessionSoreState.SessionId, _sessionSoreState.MessageCount);
        var result = await mediator.Send(query, cancellationToken);
        return result.Value!;
    }

    public override async Task AddMessagesAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = new())
    {
        if (_sessionSoreState == null) return;

        using var scope = _serviceProvider.CreateScope();
        // 修正 1: 使用你项目中已有的 IRepository<Session>
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<Session>>();

        // 加载聚合根
        var session = await repo.GetByIdAsync(_sessionSoreState.SessionId, cancellationToken);
        if (session == null) return;

        var hasNewMessage = false;

        foreach (var msg in messages)
        {
            // 修正 2: 使用 .Text 获取内容 (Microsoft.Extensions.AI)
            var content = msg.Text;

            // 过滤逻辑 1: 跳过空消息
            if (string.IsNullOrWhiteSpace(content)) continue;

            // 过滤逻辑 2: 核心过滤，防止包含 <context> 的超长 Prompt 存入数据库
            if (content.Contains("<context>")) continue;

            // 修正 3: 使用 ChatRole 判断是否为工具调用
            if (msg.Role == ChatRole.Tool) continue;

            // 修正 4: 映射 ChatRole 到你的 MessageType 枚举
            MessageType msgType;
            if (msg.Role == ChatRole.User)
            {
                msgType = MessageType.User;
            }
            else if (msg.Role == ChatRole.System)
            {
                msgType = MessageType.System;
            }
            else
            {
                // 默认 Assistant (对应你的 MessageType.Assistant)
                msgType = MessageType.Assistant;
            }

            session.AddMessage(content, msgType);
            hasNewMessage = true;
        }

        if (hasNewMessage)
        {
            repo.Update(session);
            await repo.SaveChangesAsync(cancellationToken);
        }
    }

    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        return JsonSerializer.SerializeToElement(_sessionSoreState);
    }
}