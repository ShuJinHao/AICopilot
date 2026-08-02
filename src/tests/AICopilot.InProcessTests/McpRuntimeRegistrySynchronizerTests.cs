using AICopilot.AgentPlugin;
using AICopilot.Infrastructure.Mcp;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.InProcessTests;

public sealed class McpRuntimeRegistrySynchronizerTests
{
    [Fact]
    public async Task ReconcileAsync_ShouldUnregisterAndDispose_WhenCandidateDisappears()
    {
        var server = CreateState("runtime-mcp", 1);
        var client = new TrackingAsyncDisposable();
        var provider = new FakeRuntimeRegistrationProvider
        {
            CandidateServers = [server],
            Create = state => CreateRegistration(state, client)
        };
        var loader = CreateLoader();
        await using var synchronizer = CreateSynchronizer(loader);

        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        loader.GetPlugin("runtime-mcp").Should().NotBeNull();
        client.DisposeCount.Should().Be(0);

        provider.CandidateServers = [];
        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        loader.GetPlugin("runtime-mcp").Should().BeNull();
        loader.GetPluginTools("runtime-mcp").Should().BeEmpty();
        client.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReconcileAsync_ShouldProbeUnchangedCandidate_KeepExistingAndReplaceFingerprintDrift()
    {
        var serverId = Guid.NewGuid();
        var firstState = new McpRuntimeServerState(serverId, "runtime-mcp", 1);
        var firstClient = new TrackingAsyncDisposable();
        var identicalProbeClient = new TrackingAsyncDisposable();
        var driftedClient = new TrackingAsyncDisposable();
        var createCount = 0;
        var provider = new FakeRuntimeRegistrationProvider
        {
            CandidateServers = [firstState],
            Create = state =>
            {
                createCount++;
                return createCount switch
                {
                    1 => CreateRegistration(state, firstClient, "schema-a"),
                    2 => CreateRegistration(state, identicalProbeClient, "schema-a"),
                    _ => CreateRegistration(state, driftedClient, "schema-b")
                };
            }
        };
        var loader = CreateLoader();
        await using var synchronizer = CreateSynchronizer(loader);

        await synchronizer.ReconcileAsync(provider, CancellationToken.None);
        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        createCount.Should().Be(2, "remote discovery is probed on every refresh");
        firstClient.DisposeCount.Should().Be(0);
        identicalProbeClient.DisposeCount.Should().Be(1, "an identical fresh probe is not kept alive");

        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        createCount.Should().Be(3);
        firstClient.DisposeCount.Should().Be(1);
        driftedClient.DisposeCount.Should().Be(0);
        loader.GetPlugin("runtime-mcp").Should().NotBeNull();
    }

    [Fact]
    public async Task ReconcileAsync_ShouldReplaceRegistration_WhenDatabaseRowVersionChanges()
    {
        var firstState = CreateState("runtime-mcp", 1);
        var secondState = firstState with { RowVersion = 2 };
        var firstClient = new TrackingAsyncDisposable();
        var secondClient = new TrackingAsyncDisposable();
        var provider = new FakeRuntimeRegistrationProvider
        {
            CandidateServers = [firstState],
            Create = state => state.RowVersion == 1
                ? CreateRegistration(state, firstClient)
                : CreateRegistration(state, secondClient)
        };
        var loader = CreateLoader();
        await using var synchronizer = CreateSynchronizer(loader);

        await synchronizer.ReconcileAsync(provider, CancellationToken.None);
        provider.CandidateServers = [secondState];
        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        firstClient.DisposeCount.Should().Be(1);
        secondClient.DisposeCount.Should().Be(0);
        loader.GetPlugin("runtime-mcp").Should().NotBeNull();
    }

    [Fact]
    public async Task ReconcileAsync_ShouldUnregister_WhenCandidateCannotProduceRegistration()
    {
        var firstState = CreateState("runtime-mcp", 1);
        var client = new TrackingAsyncDisposable();
        var provider = new FakeRuntimeRegistrationProvider
        {
            CandidateServers = [firstState],
            Create = state => CreateRegistration(state, client)
        };
        var loader = CreateLoader();
        await using var synchronizer = CreateSynchronizer(loader);

        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        provider.Create = _ => null;
        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        loader.GetPlugin("runtime-mcp").Should().BeNull();
        client.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReconcileAsync_ShouldWithdrawStaleRegistration_WhenDiscoveryThrows()
    {
        var state = CreateState("runtime-mcp", 1);
        var client = new TrackingAsyncDisposable();
        var provider = new FakeRuntimeRegistrationProvider
        {
            CandidateServers = [state],
            Create = current => CreateRegistration(current, client)
        };
        var loader = CreateLoader();
        await using var synchronizer = CreateSynchronizer(loader);

        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        provider.Create = _ => throw new InvalidOperationException("remote discovery failed");
        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        loader.GetPlugin("runtime-mcp").Should().BeNull();
        loader.GetPluginTools("runtime-mcp").Should().BeEmpty();
        client.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReconcileAsync_ShouldDeadlineEachServer_ContinueAndDisposeLateRegistration()
    {
        var slowState = CreateState("slow-mcp", 1);
        var healthyState = CreateState("healthy-mcp", 1);
        var slowExistingClient = new TrackingAsyncDisposable();
        var healthyExistingClient = new TrackingAsyncDisposable();
        var healthyReplacementClient = new TrackingAsyncDisposable();
        var lateClient = new TrackingAsyncDisposable();
        var lateCompletion = new TaskCompletionSource<McpRuntimeRegistration?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        McpRuntimeRegistration? slowExistingRegistration = null;
        var provider = new FakeRuntimeRegistrationProvider
        {
            CandidateServers = [slowState, healthyState],
            Create = state => state.Name == slowState.Name
                ? slowExistingRegistration = CreateRegistration(state, slowExistingClient, "schema-a")
                : CreateRegistration(state, healthyExistingClient, "schema-a")
        };
        var loader = CreateLoader();
        await using var synchronizer = CreateSynchronizer(
            loader,
            TimeSpan.FromMilliseconds(50));

        await synchronizer.ReconcileAsync(provider, CancellationToken.None);
        using var activeSlowInvocation = slowExistingRegistration!.ClientHandle.AcquireInvocation();

        provider.CreateAsync = (state, _) => state.Name == slowState.Name
            ? lateCompletion.Task
            : Task.FromResult<McpRuntimeRegistration?>(
                CreateRegistration(state, healthyReplacementClient, "schema-b"));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        loader.GetPlugin(slowState.Name).Should().BeNull();
        slowExistingClient.DisposeCount.Should().Be(0,
            "withdrawal must continue while an existing invocation delays client disposal");
        provider.QuarantinedServers.Should().ContainSingle().Which.Should().Be(slowState.Name);
        loader.GetPlugin(healthyState.Name).Should().NotBeNull(
            "one unresponsive server must not block later discovery");
        healthyExistingClient.DisposeCount.Should().Be(1);
        healthyReplacementClient.DisposeCount.Should().Be(0);

        lateCompletion.SetResult(CreateRegistration(slowState, lateClient, "late-schema"));
        await lateClient.Disposed.WaitAsync(TimeSpan.FromSeconds(2));

        lateClient.DisposeCount.Should().Be(1);
        loader.GetPlugin(slowState.Name).Should().BeNull(
            "a late registration must only be observed and disposed, never registered");

        activeSlowInvocation.Dispose();
        await slowExistingClient.Disposed.WaitAsync(TimeSpan.FromSeconds(2));
        slowExistingClient.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ClientHandle_ShouldWaitForActiveInvocationBeforeDisposing()
    {
        var client = new TrackingAsyncDisposable();
        var handle = new McpRuntimeClientHandle(client);
        using var invocation = handle.AcquireInvocation();

        var disposeTask = handle.DisposeAsync().AsTask();

        disposeTask.IsCompleted.Should().BeFalse();
        client.DisposeCount.Should().Be(0);

        invocation.Dispose();
        await disposeTask;

        client.DisposeCount.Should().Be(1);
        var acquireAfterDispose = () => handle.AcquireInvocation();
        acquireAfterDispose.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(30, 30)]
    [InlineData(1000, 300)]
    public void McpRuntimeOptions_ShouldClampRefreshInterval(int configuredSeconds, int expectedSeconds)
    {
        var options = new McpRuntimeOptions { RefreshIntervalSeconds = configuredSeconds };

        options.RefreshInterval.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    private static McpRuntimeRegistrySynchronizer CreateSynchronizer(
        IAgentPluginRegistry registry,
        TimeSpan? discoveryDeadline = null)
    {
        return new McpRuntimeRegistrySynchronizer(
            registry,
            NullLogger<McpRuntimeRegistrySynchronizer>.Instance)
        {
            DiscoveryDeadline = discoveryDeadline ??
                                TimeSpan.FromSeconds(McpRuntimeOptions.DiscoveryDeadlineSeconds)
        };
    }

    private static AgentPluginLoader CreateLoader()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        return new AgentPluginLoader([], provider);
    }

    private static McpRuntimeServerState CreateState(string name, uint rowVersion)
    {
        return new McpRuntimeServerState(Guid.NewGuid(), name, rowVersion);
    }

    private static McpRuntimeRegistration CreateRegistration(
        McpRuntimeServerState state,
        TrackingAsyncDisposable client,
        string toolSchemaFingerprint = "schema-a")
    {
        var plugin = new GenericBridgePlugin
        {
            Name = state.Name,
            Description = "runtime test plugin",
            ChatExposureMode = ChatExposureMode.Advisory,
            Tools =
            [
                new AiToolDefinition
                {
                    Name = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, state.Name, "Echo"),
                    ToolName = "Echo",
                    Kind = AiToolCallKind.Mcp,
                    TargetType = AiToolTargetType.McpServer,
                    TargetName = state.Name
                }
            ]
        };

        return new McpRuntimeRegistration(
            state.ServerId,
            state.Name,
            state.RowVersion,
            toolSchemaFingerprint,
            plugin,
            new McpRuntimeClientHandle(client));
    }

    private sealed class FakeRuntimeRegistrationProvider : IMcpRuntimeRegistrationProvider
    {
        public IReadOnlyList<McpRuntimeServerState> CandidateServers { get; set; } = [];

        public Func<McpRuntimeServerState, McpRuntimeRegistration?> Create { get; set; } = _ => null;

        public Func<McpRuntimeServerState, CancellationToken, Task<McpRuntimeRegistration?>>? CreateAsync { get; set; }

        public List<string> QuarantinedServers { get; } = [];

        public Task<IReadOnlyList<McpRuntimeServerState>> ListCandidateServersAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CandidateServers);
        }

        public Task<McpRuntimeRegistration?> CreateRegistrationAsync(
            McpRuntimeServerState server,
            CancellationToken cancellationToken)
        {
            return CreateAsync?.Invoke(server, cancellationToken) ?? Task.FromResult(Create(server));
        }

        public Task QuarantineServerAsync(
            McpRuntimeServerState server,
            CancellationToken cancellationToken)
        {
            QuarantinedServers.Add(server.Name);
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        private readonly TaskCompletionSource disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public Task Disposed => disposed.Task;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
