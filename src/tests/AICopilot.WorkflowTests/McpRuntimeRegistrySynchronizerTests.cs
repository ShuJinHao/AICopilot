using AICopilot.AgentPlugin;
using AICopilot.Infrastructure.Mcp;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.WorkflowTests;

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
    public async Task ReconcileAsync_ShouldSkipUnchangedCandidate_AndReplaceChangedRowVersion()
    {
        var serverId = Guid.NewGuid();
        var firstState = new McpRuntimeServerState(serverId, "runtime-mcp", 1);
        var secondState = firstState with { RowVersion = 2 };
        var firstClient = new TrackingAsyncDisposable();
        var secondClient = new TrackingAsyncDisposable();
        var createCount = 0;
        var provider = new FakeRuntimeRegistrationProvider
        {
            CandidateServers = [firstState],
            Create = state =>
            {
                createCount++;
                return state.RowVersion == 1
                    ? CreateRegistration(state, firstClient)
                    : CreateRegistration(state, secondClient);
            }
        };
        var loader = CreateLoader();
        await using var synchronizer = CreateSynchronizer(loader);

        await synchronizer.ReconcileAsync(provider, CancellationToken.None);
        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        createCount.Should().Be(1);
        firstClient.DisposeCount.Should().Be(0);

        provider.CandidateServers = [secondState];
        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        createCount.Should().Be(2);
        firstClient.DisposeCount.Should().Be(1);
        secondClient.DisposeCount.Should().Be(0);
        loader.GetPlugin("runtime-mcp").Should().NotBeNull();
    }

    [Fact]
    public async Task ReconcileAsync_ShouldUnregister_WhenCandidateCannotProduceRegistration()
    {
        var firstState = CreateState("runtime-mcp", 1);
        var secondState = firstState with { RowVersion = 2 };
        var client = new TrackingAsyncDisposable();
        var provider = new FakeRuntimeRegistrationProvider
        {
            CandidateServers = [firstState],
            Create = state => CreateRegistration(state, client)
        };
        var loader = CreateLoader();
        await using var synchronizer = CreateSynchronizer(loader);

        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        provider.CandidateServers = [secondState];
        provider.Create = _ => null;
        await synchronizer.ReconcileAsync(provider, CancellationToken.None);

        loader.GetPlugin("runtime-mcp").Should().BeNull();
        client.DisposeCount.Should().Be(1);
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

    private static McpRuntimeRegistrySynchronizer CreateSynchronizer(IAgentPluginRegistry registry)
    {
        return new McpRuntimeRegistrySynchronizer(
            registry,
            NullLogger<McpRuntimeRegistrySynchronizer>.Instance);
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
        TrackingAsyncDisposable client)
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
            plugin,
            new McpRuntimeClientHandle(client));
    }

    private sealed class FakeRuntimeRegistrationProvider : IMcpRuntimeRegistrationProvider
    {
        public IReadOnlyList<McpRuntimeServerState> CandidateServers { get; set; } = [];

        public Func<McpRuntimeServerState, McpRuntimeRegistration?> Create { get; set; } = _ => null;

        public Task<IReadOnlyList<McpRuntimeServerState>> ListCandidateServersAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CandidateServers);
        }

        public Task<McpRuntimeRegistration?> CreateRegistrationAsync(
            McpRuntimeServerState server,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Create(server));
        }
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
