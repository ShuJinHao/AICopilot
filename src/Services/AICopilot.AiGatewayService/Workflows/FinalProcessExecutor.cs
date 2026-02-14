using AICopilot.AiGatewayService.Agents;
using AICopilot.Core.AiGateway.Aggregates.Sessions; // 引入实体命名空间
using AICopilot.Services.Common.Contracts;
using AICopilot.SharedKernel.Repository; // 引入仓储接口
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AICopilot.AiGatewayService.Workflows;

/// <summary>
/// 最终处理执行器
/// 职责：利用聚合后的上下文构建 Agent，注入 RAG 提示词，并执行流式生成。
/// </summary>
public class FinalProcessExecutor(
    IRepository<Session> sessionRepo, // 👈 修改 1: 注入仓储，用于保存用户消息
    IDataQueryService queryService,
    ChatAgentFactory agentFactory,
    ILogger<FinalProcessExecutor> logger) :
    ReflectingExecutor<FinalProcessExecutor>("FinalProcessExecutor"),
    IMessageHandler<GenerationContext>
{
    public async ValueTask HandleAsync(
        GenerationContext genContext,
        IWorkflowContext context,
        CancellationToken cancellationToken = new())
    {
        try
        {
            var request = genContext.Request;
            logger.LogInformation("开始最终生成，SessionId: {SessionId}", request.SessionId);

            // 1. 获取会话 (从仓储获取，以便后续更新)
            var session = await sessionRepo.GetByIdAsync(request.SessionId, cancellationToken);
            if (session == null) throw new InvalidOperationException("会话不存在");

            // ========================================================================
            // 👇 修改 2: 在这里手动保存用户的“干净”提问
            // ========================================================================
            // 我们在 Store 里拦截了包含 <context> 的消息，所以必须在这里
            // 把原始的用户输入 (request.Message) 显式存入数据库。
            session.AddMessage(request.Message, MessageType.User);
            sessionRepo.Update(session);
            await sessionRepo.SaveChangesAsync(cancellationToken);
            // ========================================================================

            // 2. 创建基础 Agent 实例
            // 此时 Agent 拥有的是数据库中定义的静态 System Prompt
            var agent = await agentFactory.CreateAgentAsync(session.TemplateId);

            // 3. 构建消息列表
            var inputMessages = new List<ChatMessage>();
            string finalUserPrompt;

            // 检查是否存在 知识库上下文 或 数据分析上下文
            bool hasKnowledge = !string.IsNullOrWhiteSpace(genContext.KnowledgeContext);
            bool hasDataAnalysis = !string.IsNullOrWhiteSpace(genContext.DataAnalysisContext);
            bool hasContext = hasKnowledge || hasDataAnalysis;

            if (hasContext)
            {
                // 构建混合上下文内容
                var contextBuilder = new StringBuilder();

                if (hasDataAnalysis)
                {
                    contextBuilder.AppendLine("数据分析/SQL查询结果：");
                    contextBuilder.AppendLine(genContext.DataAnalysisContext);
                    contextBuilder.AppendLine();
                }

                if (hasKnowledge)
                {
                    contextBuilder.AppendLine("知识库检索参考信息：");
                    contextBuilder.AppendLine(genContext.KnowledgeContext);
                    contextBuilder.AppendLine();
                }

                // 使用 XML 标签 <context> 是一种最佳实践
                // 注意：SessionChatMessageStore 会识别 <context> 标签并拦截不存库，
                // 这正是我们想要的（只存上面手动存的 clean message，不存这个 dirty prompt）
                finalUserPrompt = $"""
                                   请基于以下参考信息（包含数据库查询结果或检索文档）回答我的问题：

                                   <context>
                                   {contextBuilder}
                                   </context>

                                   回答要求：
                                   1. 引用参考信息时，请标注来源 ID（例如 [^1]）。
                                   2. 针对数据分析结果，请结合用户问题进行自然语言解释，不要直接展示原始数据结构，除非用户要求。
                                   3. 在回答结尾，如果引用了知识库文档，请生成“参考资料”列表。
                                   4. 如果参考信息不足以回答问题，请直接说明，严禁编造。
                                   5. 保持回答专业、简洁。

                                   用户问题：
                                   {request.Message}
                                   """;

                logger.LogDebug("增强模式激活：注入知识({KSize})，注入数据({DSize})。",
                    genContext.KnowledgeContext?.Length ?? 0,
                    genContext.DataAnalysisContext?.Length ?? 0);
            }
            else
            {
                // 无上下文模式：直接透传用户问题
                finalUserPrompt = request.Message;
                logger.LogDebug("增强模式未激活：仅使用用户原始输入。");
            }

            inputMessages.Add(new ChatMessage(ChatRole.User, finalUserPrompt));

            // 4. 准备执行参数 (ChatOptions)
            var runOptions = new ChatClientAgentRunOptions
            {
                ChatOptions = new ChatOptions
                {
                    Tools = genContext.Tools,
                    Temperature = !string.IsNullOrWhiteSpace(genContext.KnowledgeContext) ? 0.3f : 0.7f,
                }
            };

            // 5. 恢复会话状态 (Thread)
            var storeThread = new { storeState = new SessionSoreState(request.SessionId) };
            var agentThread = agent.DeserializeThread(JsonSerializer.SerializeToElement(storeThread));

            // 6. 执行流式生成
            // Agent 会生成回答，并且 SessionChatMessageStore 会自动把 Assistant 的回答存入数据库
            await foreach (var update in agent.RunStreamingAsync(
                               inputMessages,
                               agentThread,
                               runOptions,
                               cancellationToken))
            {
                await context.AddEventAsync(new AgentRunUpdateEvent(Id, update), cancellationToken);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "最终生成阶段发生错误");
            await context.AddEventAsync(new ExecutorFailedEvent(Id, e), cancellationToken);
            throw;
        }
    }
}