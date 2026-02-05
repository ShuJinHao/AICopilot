using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Services.Common.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Sessions;

public record GetListChatMessagesQuery(Guid SessionId, int Count, bool IsDesc = true) : IQuery<Result<List<ChatMessage>>>;

public class GetListChatMessagesQueryHandler(IDataQueryService queryService)
    : IQueryHandler<GetListChatMessagesQuery, Result<List<ChatMessage>>>
{
    public async Task<Result<List<ChatMessage>>> Handle(GetListChatMessagesQuery request, CancellationToken cancellationToken)
    {
        // 1. 基础查询
        var query = queryService.Messages.Where(m => m.SessionId == request.SessionId);

        // 2. 排序策略 [关键步骤]
        // 必须先按时间倒序 (OrderByDescending)，这样 .Take() 才能拿到“最新的”N条
        query = request.IsDesc
            ? query.OrderByDescending(m => m.CreatedAt)
            : query.OrderBy(m => m.CreatedAt);

        // 3. 执行数据库查询 (先排序后截取)
        // [修复] 你的接口 ToListAsync 只接受一个参数，不接受 cancellationToken
        var entities = await queryService.ToListAsync(query.Take(request.Count));

        // 4. 内存重排
        // 数据库为了分页用了倒序，但发给 AI 的上下文必须按时间正序排列
        // 比如数据库查出来是 [10:05, 10:04, 10:03]，这里要转成 [10:03, 10:04, 10:05]
        var chatMessages = entities
            .OrderBy(m => m.CreatedAt)
            .Select(msg => new ChatMessage(MapRole(msg.Type), msg.Content))
            .ToList();

        return Result.Success(chatMessages);
    }

    /// <summary>
    /// 角色映射辅助方法
    /// </summary>
    private static ChatRole MapRole(MessageType type) => type switch
    {
        MessageType.User => ChatRole.User,
        MessageType.Assistant => ChatRole.Assistant,
        MessageType.System => ChatRole.System,
        _ => ChatRole.User
    };
}