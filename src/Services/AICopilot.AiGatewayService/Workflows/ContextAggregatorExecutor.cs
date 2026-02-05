using AICopilot.AiGatewayService.Agents;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows;

/// <summary>
/// 上下文聚合执行器
/// 职责：作为 Fan-in 节点，接收来自所有并行分支的 BranchResult。
/// 只有当接收到的结果数量达到预期（2个）时，才进行合并并触发下游。
/// </summary>
public class ContextAggregatorExecutor(ILogger<ContextAggregatorExecutor> logger)
    : ReflectingExecutor<ContextAggregatorExecutor>("ContextAggregatorExecutor"),
        IMessageHandler<BranchResult>
{
    // 内部状态：用于跨方法调用累积结果
    private readonly List<BranchResult> _accumulatedResults = [];

    // 硬编码预期分支数：Tools + Knowledge = 2
    private const int ExpectedBranchCount = 2;

    // [新增] 锁对象：用于保护 _accumulatedResults 的并发读写安全
    private readonly object _lock = new();

    public async ValueTask HandleAsync(
        BranchResult branchResult,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // 用于临时存储需要处理的一批数据
        List<BranchResult>? batchToProcess = null;

        // 1. [原子操作区域] 累积状态并检查
        lock (_lock)
        {
            _accumulatedResults.Add(branchResult);

            if (_accumulatedResults.Count >= ExpectedBranchCount)
            {
                // 如果满足条件，将当前结果复制出来处理，并立即清空原始列表
                // 这样既防止了并发写入导致的计数错误，也为下一轮做好了准备
                batchToProcess = new List<BranchResult>(_accumulatedResults);
                _accumulatedResults.Clear();
            }
            else
            {
                // 如果未满足条件，仅记录日志，当前线程结束任务
                logger.LogDebug("聚合进度: {Current}/{Total}，等待其他分支...", _accumulatedResults.Count, ExpectedBranchCount);
            }
        }

        // 2. [异步处理区域] 只有拿到 batchToProcess 的那个线程才会执行后续逻辑
        // 注意：await 操作必须在 lock 块外部执行
        if (batchToProcess != null)
        {
            logger.LogInformation("并行分支汇聚完成，开始合并上下文。");

            // 3. 恢复原始请求 (从全局状态中读取)
            var request = await context.ReadStateAsync<ChatStreamRequest>("ChatStreamRequest", "Chat", cancellationToken);
            if (request == null) throw new InvalidOperationException("无法获取原始会话请求");

            var genContext = new GenerationContext { Request = request };

            // 4. 合并数据 (使用安全的局部变量 batchToProcess)
            foreach (var result in batchToProcess)
            {
                switch (result.Type)
                {
                    case BranchType.Tools when result.Tools != null:
                        genContext.Tools = result.Tools;
                        break;

                    case BranchType.Knowledge when !string.IsNullOrWhiteSpace(result.Knowledge):
                        genContext.KnowledgeContext = result.Knowledge;
                        break;
                }
            }

            // 5. 手动发送聚合结果消息，触发下游
            await context.SendMessageAsync(genContext, cancellationToken);
        }
    }
}