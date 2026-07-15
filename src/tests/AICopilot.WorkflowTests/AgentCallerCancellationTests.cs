using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Observability;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.WorkflowTests;

public sealed class AgentCallerCancellationTests
{
    [Fact]
    public async Task BranchCallerCancellation_ShouldPropagateInsteadOfBecomingFailedResult()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = async () => await AgentWorkflowPipeline.RunBranchSafelyAsync(
            BranchType.DataAnalysis,
            isRequired: true,
            () => Task.FromCanceled<BranchResult>(cancellation.Token),
            NullLogger.Instance,
            cancellation.Token);

        var assertion = await action.Should().ThrowAsync<OperationCanceledException>();
        assertion.Which.CancellationToken.Should().Be(cancellation.Token);

        using var workflowCancellation = new CancellationTokenSource();
        var controlledPipeline = new CancellationBarrierPipeline();
        var drainTask = DrainAsync(controlledPipeline.RunIntentWorkflowAsync(
            new ChatStreamRequest(Guid.NewGuid(), "test cancellation barrier"),
            session: null,
            new StringBuilder(),
            workflowCancellation.Token));

        await controlledPipeline.AllBranchesStarted.WaitAsync(TimeSpan.FromSeconds(5));
        workflowCancellation.Cancel();
        await controlledPipeline.AllBranchesObservedCancellation.WaitAsync(TimeSpan.FromSeconds(5));
        drainTask.IsCompleted.Should().BeFalse(
            "the workflow must not return before every parallel branch has reached quiescence");
        controlledPipeline.ReleaseBranches();

        var drainAction = async () => await drainTask;
        await drainAction.Should().ThrowAsync<OperationCanceledException>();
        controlledPipeline.LateSideEffectCount.Should().Be(
            AgentWorkflowTopology.ParallelBranches.Count,
            "all late branch work must be observed before cancellation returns to the caller");
    }

    [Fact]
    public async Task CallerCancellation_WhenCleanupFails_ShouldPreserveCancellationAndCompensateOnce()
    {
        using var cancellation = new CancellationTokenSource();
        var agent = new ControlledRuntimeAgent(cancellation);
        var store = new RecordingFinalAgentContextStore { ThrowOnRemove = true };
        var pipeline = CreatePipeline(
            store,
            new RecordingFinalAgentContextSerializer());
        await using var context = CreateContext(agent);

        var action = () => DrainAsync(
            pipeline.ResumeFinalAgentAsync(
                context,
                session: null,
                new StringBuilder(),
                cancellation.Token));

        await action.Should().ThrowAsync<OperationCanceledException>();
        store.RemoveCount.Should().Be(1);
        store.LastRemovalToken.CanBeCanceled.Should().BeFalse();
    }

    [Fact]
    public async Task BusinessFailure_WhenCleanupFails_ShouldPreservePrimaryExceptionAndCompensateOnce()
    {
        var primaryFailure = new InvalidOperationException("primary workflow failure");
        var agent = new ControlledRuntimeAgent(failure: primaryFailure);
        var store = new RecordingFinalAgentContextStore { ThrowOnRemove = true };
        var pipeline = CreatePipeline(
            store,
            new RecordingFinalAgentContextSerializer());
        await using var context = CreateContext(agent);

        var action = () => DrainAsync(pipeline.ResumeFinalAgentAsync(
            context,
            session: null,
            new StringBuilder(),
            CancellationToken.None));

        var assertion = await action.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(primaryFailure);
        store.RemoveCount.Should().Be(1);
        store.LastRemovalToken.CanBeCanceled.Should().BeFalse();
    }

    [Fact]
    public async Task CompletedApprovalContext_ShouldStoreWithoutCompensationRemoval()
    {
        var agent = new ControlledRuntimeAgent();
        var store = new RecordingFinalAgentContextStore();
        var serializer = new RecordingFinalAgentContextSerializer();
        var pipeline = CreatePipeline(store, serializer);
        await using var context = CreateContext(agent);
        context.FunctionApprovalRequestContents.Add(new AiToolApprovalRequest(
            "request-1",
            new AiToolCall(
                "call-1",
                "test-tool",
                default,
                null,
                new Dictionary<string, object?>())));

        await DrainAsync(pipeline.ResumeFinalAgentAsync(
            context,
            session: null,
            new StringBuilder(),
            CancellationToken.None));

        serializer.CreateSnapshotCount.Should().Be(1);
        store.SetCount.Should().Be(1);
        store.RemoveCount.Should().Be(0);
    }

    [Fact]
    public async Task ApprovalContext_WhenSetAndCompensationFail_ShouldPreserveSetFailureAndRemoveOnce()
    {
        var setFailure = new InvalidOperationException("set failed");
        var store = new RecordingFinalAgentContextStore
        {
            SetFailure = setFailure,
            RemoveFailure = new ApplicationException("compensation failed")
        };
        var pipeline = CreatePipeline(store, new RecordingFinalAgentContextSerializer());
        await using var context = CreateContext(new ControlledRuntimeAgent());
        context.FunctionApprovalRequestContents.Add(new AiToolApprovalRequest(
            "request-1",
            new AiToolCall(
                "call-1",
                "test-tool",
                default,
                null,
                new Dictionary<string, object?>())));

        var action = () => DrainAsync(pipeline.ResumeFinalAgentAsync(
            context,
            session: null,
            new StringBuilder(),
            CancellationToken.None));

        var assertion = await action.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(setFailure);
        store.SetCount.Should().Be(1);
        store.RemoveCount.Should().Be(1);
        store.LastRemovalToken.CanBeCanceled.Should().BeFalse();
    }

    [Fact]
    public async Task ApprovalContext_WhenSetObservesCallerCancellationAndCompensationFails_ShouldPreserveSameCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var setCancellation = new OperationCanceledException(
            "caller cancelled while storing approval context",
            cancellation.Token);
        var store = new RecordingFinalAgentContextStore
        {
            SetAsyncBehavior = async token =>
            {
                token.Should().Be(cancellation.Token);
                await Task.Yield();
                cancellation.Cancel();
                throw setCancellation;
            },
            RemoveFailure = new ApplicationException("compensation failed")
        };
        var pipeline = CreatePipeline(store, new RecordingFinalAgentContextSerializer());
        await using var context = CreateContext(new ControlledRuntimeAgent());
        context.FunctionApprovalRequestContents.Add(new AiToolApprovalRequest(
            "request-1",
            new AiToolCall(
                "call-1",
                "test-tool",
                default,
                null,
                new Dictionary<string, object?>())));

        var action = () => DrainAsync(pipeline.ResumeFinalAgentAsync(
            context,
            session: null,
            new StringBuilder(),
            cancellation.Token));

        var assertion = await action.Should().ThrowAsync<OperationCanceledException>();
        assertion.Which.Should().BeSameAs(setCancellation);
        store.SetCount.Should().Be(1);
        store.RemoveCount.Should().Be(1);
        store.LastRemovalToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task CompletedNonApprovalContext_WhenRemoveFails_ShouldPropagateCleanupFailureOnce()
    {
        var removeFailure = new InvalidOperationException("remove failed");
        var store = new RecordingFinalAgentContextStore { RemoveFailure = removeFailure };
        var pipeline = CreatePipeline(store, new RecordingFinalAgentContextSerializer());
        await using var context = CreateContext(new ControlledRuntimeAgent());

        var action = () => DrainAsync(pipeline.ResumeFinalAgentAsync(
            context,
            session: null,
            new StringBuilder(),
            CancellationToken.None));

        var assertion = await action.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(removeFailure);
        store.SetCount.Should().Be(0);
        store.RemoveCount.Should().Be(1);
        store.LastRemovalToken.CanBeCanceled.Should().BeFalse();
    }

    [Fact]
    public async Task Compensation_ShouldRemainIdempotentAcrossRepeatedDisposal()
    {
        var store = new RecordingFinalAgentContextStore();
        var compensation = new FinalAgentContextCompensation(
            store,
            Guid.NewGuid(),
            NullLogger.Instance);

        await compensation.DisposeAsync();
        await compensation.DisposeAsync();
        await compensation.RemoveAndCompleteAsync();

        store.RemoveCount.Should().Be(1);
    }

    private static AgentWorkflowPipeline CreatePipeline(
        IFinalAgentContextStore store,
        IFinalAgentContextSerializer serializer)
    {
        var audit = new NoopAuditLogWriter();
        var agentRun = new FinalAgentRunExecutor(
            NullLogger<FinalAgentRunExecutor>.Instance,
            audit,
            new ZeroTokenEstimator(),
            new NoopChatTokenTelemetry(),
            new ToolExecutionAuditRecorder(audit),
            null!);
        return new AgentWorkflowPipeline(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            agentRun,
            store,
            serializer,
            NullLogger<AgentWorkflowPipeline>.Instance);
    }

    private static FinalAgentContext CreateContext(IRuntimeChatAgent agent)
    {
        return new FinalAgentContext
        {
            ScopedAgent = new ScopedRuntimeAgent(agent, new NoopAsyncDisposable()),
            Thread = new RuntimeAgentSession(),
            InputText = "test",
            InputMessages = [],
            RunOptions = new RuntimeAgentRunOptions(new AiChatOptions()),
            SessionId = Guid.NewGuid(),
            TokenTelemetryContext = new ChatTokenTelemetryContext(
                null,
                "test-model",
                "test-template",
                1024,
                128)
        };
    }

    private static async Task DrainAsync(IAsyncEnumerable<ChatChunk> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private sealed class ControlledRuntimeAgent(
        CancellationTokenSource? cancellation = null,
        Exception? failure = null)
        : IRuntimeChatAgent
    {
        public Task<IRuntimeAgentSession> CreateSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IRuntimeAgentSession>(new RuntimeAgentSession());

        public Task<string> SerializeSessionAsync(
            IRuntimeAgentSession session,
            JsonSerializerOptions serializerOptions,
            CancellationToken cancellationToken = default) => Task.FromResult("{}");

        public Task<IRuntimeAgentSession> DeserializeSessionAsync(
            string serializedSessionState,
            JsonSerializerOptions serializerOptions,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IRuntimeAgentSession>(new RuntimeAgentSession());

        public Task<StructuredAgentResponse<T>> RunStructuredAsync<T>(
            IEnumerable<AiChatMessage> messages,
            IRuntimeAgentSession? session,
            JsonSerializerOptions serializerOptions,
            RuntimeAgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
            IEnumerable<AiChatMessage> messages,
            IRuntimeAgentSession session,
            RuntimeAgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => RunCoreAsync(cancellationToken);

        public IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
            string input,
            IRuntimeAgentSession session,
            RuntimeAgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => RunCoreAsync(cancellationToken);

        private async IAsyncEnumerable<RuntimeAgentUpdate> RunCoreAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (failure is not null)
            {
                await Task.Yield();
                throw failure;
            }

            if (cancellation is not null)
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            yield break;
        }
    }

    private sealed class CancellationBarrierPipeline : AgentWorkflowPipeline
    {
        private readonly TaskCompletionSource allBranchesStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource allBranchesObservedCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseBranches = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int startedBranchCount;
        private int cancelledBranchCount;
        private int lateSideEffectCount;

        public CancellationBarrierPipeline()
            : base(
                new FixedIntentRoutingExecutor(),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                new RecordingFinalAgentContextStore(),
                new RecordingFinalAgentContextSerializer(),
                NullLogger<AgentWorkflowPipeline>.Instance)
        {
        }

        public Task AllBranchesStarted => allBranchesStarted.Task;

        public Task AllBranchesObservedCancellation => allBranchesObservedCancellation.Task;

        public int LateSideEffectCount => Volatile.Read(ref lateSideEffectCount);

        public void ReleaseBranches() => releaseBranches.TrySetResult();

        protected override bool IsBranchRequired(
            BranchType branchType,
            IReadOnlyCollection<IntentResult> intents) => false;

        protected override async Task<BranchResult> ExecuteBranchAsync(
            BranchType branchType,
            List<IntentResult> intents,
            string message,
            AgentWorkflowSink sink,
            SessionRuntimeSnapshot? session,
            CancellationToken ct)
        {
            if (Interlocked.Increment(ref startedBranchCount) == AgentWorkflowTopology.ParallelBranches.Count)
            {
                allBranchesStarted.TrySetResult();
            }

            // Deliberately ignore ct to prove the pipeline itself provides a quiescence barrier.
            using var cancellationRegistration = ct.Register(() =>
            {
                if (Interlocked.Increment(ref cancelledBranchCount) == AgentWorkflowTopology.ParallelBranches.Count)
                {
                    allBranchesObservedCancellation.TrySetResult();
                }
            });
            await releaseBranches.Task.ConfigureAwait(false);
            Interlocked.Increment(ref lateSideEffectCount);
            if (branchType == BranchType.BusinessPolicy)
            {
                throw new InvalidOperationException("simulated late branch failure");
            }

            return BranchResult.Empty(branchType);
        }
    }

    private sealed class FixedIntentRoutingExecutor : IntentRoutingExecutor
    {
        public FixedIntentRoutingExecutor()
            : base(null!, null!, null!, null!, null!, NullLogger<IntentRoutingExecutor>.Instance)
        {
        }

        public override Task<IntentRoutingStepResult> ExecuteAsync(
            ChatStreamRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(new IntentRoutingStepResult(
                [],
                ManufacturingSceneType.FallbackToExistingRouting,
                null,
                new ChatExecutionMetadataSnapshot()));
    }

    private sealed class RuntimeAgentSession : IRuntimeAgentSession;

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingFinalAgentContextStore : IFinalAgentContextStore
    {
        public bool ThrowOnRemove { get; init; }
        public Exception? SetFailure { get; init; }
        public Exception? RemoveFailure { get; init; }
        public Func<CancellationToken, Task>? SetAsyncBehavior { get; init; }
        public int SetCount { get; private set; }
        public int RemoveCount { get; private set; }
        public CancellationToken LastRemovalToken { get; private set; }

        public Task<StoredFinalAgentContext?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<StoredFinalAgentContext?>(null);

        public Task SetAsync(Guid sessionId, StoredFinalAgentContext context, CancellationToken cancellationToken = default)
        {
            SetCount++;
            if (SetAsyncBehavior is not null)
            {
                return SetAsyncBehavior(cancellationToken);
            }

            return SetFailure is null
                ? Task.CompletedTask
                : Task.FromException(SetFailure);
        }

        public Task RemoveAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            RemoveCount++;
            LastRemovalToken = cancellationToken;
            var failure = RemoveFailure ?? (ThrowOnRemove
                ? new InvalidOperationException("simulated cleanup failure")
                : null);
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    private sealed class RecordingFinalAgentContextSerializer : IFinalAgentContextSerializer
    {
        public int CreateSnapshotCount { get; private set; }

        public Task<StoredFinalAgentContext> CreateSnapshotAsync(
            FinalAgentContext agentContext,
            CancellationToken cancellationToken = default)
        {
            CreateSnapshotCount++;
            return Task.FromResult(new StoredFinalAgentContext(
                agentContext.SessionId,
                agentContext.InputText,
                0,
                0,
                agentContext.TokenTelemetryContext,
                null,
                null,
                [],
                "{}",
                []));
        }

        public Task<FinalAgentContext> RestoreAsync(
            StoredFinalAgentContext storedContext,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoopAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class ZeroTokenEstimator : ITextTokenEstimator
    {
        public int CountTokens(string? text) => 0;
    }

    private sealed class NoopChatTokenTelemetry : IChatTokenTelemetry
    {
        public void RecordUsage(
            ChatTokenTelemetryContext context,
            AiUsageDetails usage,
            int estimatedInputTokens,
            bool isEstimated)
        {
        }
    }
}
