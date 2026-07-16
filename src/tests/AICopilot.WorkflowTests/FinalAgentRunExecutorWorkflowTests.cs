using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.WorkflowTests;

public sealed class FinalAgentRunExecutorWorkflowTests
{
    [Fact]
    public async Task ModelChunkTimeout_ShouldCancelOnlyModelWaitAndReturnStableWorkflowError()
    {
        using var callerCancellation = new CancellationTokenSource();
        using var modelTimeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellation.Token);
        await using var enumerator = new BlockingRuntimeUpdateEnumerator(modelTimeoutCancellation.Token);

        Func<Task> action = async () =>
        {
            _ = await FinalAgentRunExecutor.MoveNextModelUpdateAsync(
                enumerator,
                modelTimeoutCancellation,
                callerCancellation.Token,
                TimeSpan.Zero);
        };

        var assertion = await action.Should().ThrowAsync<AgentWorkflowException>();

        assertion.Which.Code.Should().Be(AppProblemCodes.ModelRequestTimeout);
        assertion.Which.UserFacingMessage.Should().Be("模型响应超时，请稍后重试或缩小问题范围。");
        callerCancellation.IsCancellationRequested.Should().BeFalse();
        modelTimeoutCancellation.IsCancellationRequested.Should().BeTrue();
        enumerator.MoveNextCount.Should().Be(1);
        await enumerator.ObservedCancellation.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class BlockingRuntimeUpdateEnumerator(CancellationToken cancellationToken)
        : IAsyncEnumerator<RuntimeAgentUpdate>
    {
        private readonly TaskCompletionSource observedCancellation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int moveNextCount;

        public RuntimeAgentUpdate Current => throw new InvalidOperationException(
            "A blocked model update enumerator does not expose a current value.");

        public int MoveNextCount => Volatile.Read(ref moveNextCount);

        public Task ObservedCancellation => observedCancellation.Task;

        public async ValueTask<bool> MoveNextAsync()
        {
            Interlocked.Increment(ref moveNextCount);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                observedCancellation.TrySetResult();
                throw;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
