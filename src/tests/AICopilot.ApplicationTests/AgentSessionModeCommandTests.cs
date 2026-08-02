using System.Runtime.CompilerServices;
using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Commands.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.ApplicationTests;

public sealed class AgentSessionModeCommandTests
{
    [Fact]
    public async Task ModeCommand_ShouldUseOfficialHarnessModeApiAndOptimisticVersion()
    {
        var ownerId = Guid.NewGuid();
        var (handler, stateStore, harnessFactory, session) = CreateHandler(
            ownerId,
            CreateSnapshot(ownerId));

        var result = await handler.Handle(
            new UpdateAgentSessionModeCommand(session.Id.Value, "execute", 1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new AgentSessionModeDto(
            session.Id.Value,
            "execute",
            2));
        harnessFactory.CreateCount.Should().Be(1);
        harnessFactory.LastRequest!.Runtime.Options.Tools.Should().BeEmpty();
        harnessFactory.LastAgent!.SetModeCount.Should().Be(1);
        stateStore.PersistModeChangeCount.Should().Be(1);
        stateStore.LastExpectedVersion.Should().Be(1);
        stateStore.LastSerializedState.Should().Contain("\"mode\":\"execute\"");
    }

    [Fact]
    public async Task ModeCommand_ShouldHideForeignSessionBeforeLoadingAgentState()
    {
        var ownerId = Guid.NewGuid();
        var (handler, stateStore, harnessFactory, session) = CreateHandler(
            Guid.NewGuid(),
            CreateSnapshot(ownerId),
            sessionOwnerId: ownerId);

        var result = await handler.Handle(
            new UpdateAgentSessionModeCommand(session.Id.Value, "execute", 1),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        stateStore.LoadCount.Should().Be(0);
        harnessFactory.CreateCount.Should().Be(0);
    }

    [Theory]
    [InlineData("running", AppProblemCodes.AgentSessionVersionConflict)]
    [InlineData("version", AppProblemCodes.AgentSessionVersionConflict)]
    [InlineData("pending", AppProblemCodes.ApprovalPending)]
    public async Task ModeCommand_ShouldRejectActiveStaleAndPendingSessions(
        string stateCase,
        string expectedProblemCode)
    {
        var ownerId = Guid.NewGuid();
        var snapshot = stateCase switch
        {
            "running" => CreateSnapshot(
                ownerId,
                status: AgentSessionRuntimeStatus.Running,
                activeTurnId: Guid.NewGuid()),
            "version" => CreateSnapshot(ownerId, version: 2),
            "pending" => CreateSnapshot(
                ownerId,
                pendingApprovals: [CreateApprovalBinding(ownerId)]),
            _ => throw new ArgumentOutOfRangeException(nameof(stateCase))
        };
        var (handler, stateStore, harnessFactory, session) = CreateHandler(
            ownerId,
            snapshot);

        var result = await handler.Handle(
            new UpdateAgentSessionModeCommand(session.Id.Value, "execute", 1),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Conflict);
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<ApiProblemDescriptor>()
            .Which.Code.Should().Be(expectedProblemCode);
        stateStore.PersistModeChangeCount.Should().Be(0);
        harnessFactory.CreateCount.Should().Be(0);
    }

    [Fact]
    public async Task ModeCommand_ShouldRejectInterruptedSessionWithoutReplay()
    {
        var ownerId = Guid.NewGuid();
        var (handler, stateStore, harnessFactory, session) = CreateHandler(
            ownerId,
            CreateSnapshot(
                ownerId,
                status: AgentSessionRuntimeStatus.Interrupted));

        var result = await handler.Handle(
            new UpdateAgentSessionModeCommand(session.Id.Value, "execute", 1),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<ApiProblemDescriptor>()
            .Which.Code.Should().Be(AppProblemCodes.AgentSessionInterrupted);
        stateStore.PersistModeChangeCount.Should().Be(0);
        harnessFactory.CreateCount.Should().Be(0);
    }

    private static (
        UpdateAgentSessionModeCommandHandler Handler,
        RecordingAgentSessionStateStore StateStore,
        RecordingHarnessRuntimeFactory HarnessFactory,
        Session Session) CreateHandler(
        Guid currentUserId,
        AgentSessionStateSnapshot snapshot,
        Guid? sessionOwnerId = null)
    {
        var model = FakeRuntimeAgentFactory.CreateModel();
        var template = FakeRuntimeAgentFactory.CreateTemplate(model);
        var session = new Session(sessionOwnerId ?? currentUserId, template.Id);
        var currentUser = new TestCurrentUser(currentUserId);
        var stateStore = new RecordingAgentSessionStateStore(
            snapshot with
            {
                SessionId = session.Id.Value,
                UserId = sessionOwnerId ?? currentUserId
            });
        var harnessFactory = new RecordingHarnessRuntimeFactory();
        var configuredFactory = new ConfiguredAgentRuntimeFactory(
            new InMemoryReadRepository<AICopilot.Core.AiGateway.Aggregates.ConversationTemplate.ConversationTemplate>(
                [template]),
            new InMemoryReadRepository<AICopilot.Core.AiGateway.Aggregates.LanguageModel.LanguageModel>(
                [model]),
            new FakeRuntimeAgentFactory(),
            harnessFactory,
            currentUser);
        var handler = new UpdateAgentSessionModeCommandHandler(
            new InMemoryReadRepository<Session>([session]),
            currentUser,
            new InMemorySessionExecutionLock(),
            stateStore,
            configuredFactory);
        return (handler, stateStore, harnessFactory, session);
    }

    private static AgentSessionStateSnapshot CreateSnapshot(
        Guid userId,
        AgentSessionRuntimeStatus status = AgentSessionRuntimeStatus.Ready,
        Guid? activeTurnId = null,
        long version = 1,
        IReadOnlyList<AgentApprovalBinding>? pendingApprovals = null) =>
        new(
            Guid.NewGuid(),
            userId,
            TenantId: null,
            AgentSchemaVersion: 1,
            SerializedSessionState: """{"mode":"plan"}""",
            status,
            activeTurnId,
            version,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30),
            pendingApprovals ?? []);

    private static AgentApprovalBinding CreateApprovalBinding(Guid userId) =>
        new(
            Guid.NewGuid(),
            userId,
            TenantId: null,
            RequestId: "request-1",
            ToolCallId: "call-1",
            ToolName: "BusinessQuery",
            ToolKind: AiToolCallKind.Function,
            ServerName: null,
            TargetType: AiToolTargetType.Plugin,
            TargetName: "main-chat",
            CanonicalToolName: "BusinessQuery",
            Arguments: new Dictionary<string, object?>(),
            ToolSchemaVersion: 1,
            CanonicalArgumentsDigest: new string('a', 64));

    private sealed class RecordingAgentSessionStateStore(
        AgentSessionStateSnapshot snapshot) : IAgentSessionStateStore
    {
        public int LoadCount { get; private set; }

        public int PersistModeChangeCount { get; private set; }

        public long? LastExpectedVersion { get; private set; }

        public string? LastSerializedState { get; private set; }

        public void AddNew(
            Guid sessionId,
            Guid userId,
            string? tenantId,
            string serializedSessionState) =>
            throw new NotSupportedException();

        public Task<AgentSessionStateSnapshot> LoadOwnedAsync(
            Guid sessionId,
            Guid userId,
            string? tenantId,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(snapshot);
        }

        public Task<AgentSessionStateSnapshot> PersistModeChangeAsync(
            Guid sessionId,
            Guid userId,
            string? tenantId,
            long expectedVersion,
            string serializedSessionState,
            CancellationToken cancellationToken = default)
        {
            PersistModeChangeCount++;
            LastExpectedVersion = expectedVersion;
            LastSerializedState = serializedSessionState;
            return Task.FromResult(snapshot with
            {
                SerializedSessionState = serializedSessionState,
                Version = snapshot.Version + 1
            });
        }

        public Task<AgentSessionStateSnapshot> BeginTurnAsync(
            Guid sessionId,
            Guid userId,
            string? tenantId,
            Guid turnId,
            bool approvalContinuation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentSessionStateSnapshot> PersistCheckpointAsync(
            Guid sessionId,
            Guid userId,
            string? tenantId,
            Guid turnId,
            string serializedSessionState,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentSessionStateSnapshot> CompleteTurnAsync(
            Guid sessionId,
            Guid userId,
            string? tenantId,
            Guid turnId,
            string serializedSessionState,
            IReadOnlyCollection<AgentApprovalBinding> pendingApprovals,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task InterruptTurnAsync(
            Guid sessionId,
            Guid userId,
            string? tenantId,
            Guid turnId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingHarnessRuntimeFactory : IHarnessAgentRuntimeFactory
    {
        public int CreateCount { get; private set; }

        public HarnessAgentRuntimeCreateRequest? LastRequest { get; private set; }

        public RecordingHarnessAgent? LastAgent { get; private set; }

        public bool CanCreate(string providerName) =>
            string.Equals(
                providerName,
                FakeRuntimeAgentFactory.ProviderName,
                StringComparison.OrdinalIgnoreCase);

        public ScopedRuntimeAgent Create(HarnessAgentRuntimeCreateRequest request)
        {
            CreateCount++;
            LastRequest = request;
            LastAgent = new RecordingHarnessAgent();
            return new ScopedRuntimeAgent(
                LastAgent,
                NoopAsyncDisposable.Instance);
        }
    }

    private sealed class RecordingHarnessAgent : IHarnessRuntimeChatAgent
    {
        public int SetModeCount { get; private set; }

        public Task<IRuntimeAgentSession> CreateSessionAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IRuntimeAgentSession>(
                new RecordingHarnessSession(RuntimeAgentMode.Plan));

        public Task<string> SerializeSessionAsync(
            IRuntimeAgentSession session,
            JsonSerializerOptions serializerOptions,
            CancellationToken cancellationToken = default)
        {
            var harnessSession = (RecordingHarnessSession)session;
            return Task.FromResult(JsonSerializer.Serialize(
                new
                {
                    mode = harnessSession.Mode == RuntimeAgentMode.Execute
                        ? "execute"
                        : "plan"
                },
                serializerOptions));
        }

        public Task<IRuntimeAgentSession> DeserializeSessionAsync(
            string serializedSessionState,
            JsonSerializerOptions serializerOptions,
            CancellationToken cancellationToken = default)
        {
            using var document = JsonDocument.Parse(serializedSessionState);
            var mode = document.RootElement.GetProperty("mode").GetString() == "execute"
                ? RuntimeAgentMode.Execute
                : RuntimeAgentMode.Plan;
            return Task.FromResult<IRuntimeAgentSession>(
                new RecordingHarnessSession(mode));
        }

        public Task<RuntimeAgentMode> GetModeAsync(
            IRuntimeAgentSession session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(((RecordingHarnessSession)session).Mode);

        public Task SetModeAsync(
            IRuntimeAgentSession session,
            RuntimeAgentMode mode,
            CancellationToken cancellationToken = default)
        {
            SetModeCount++;
            ((RecordingHarnessSession)session).Mode = mode;
            return Task.CompletedTask;
        }

        public Task<StructuredAgentResponse<T>> RunStructuredAsync<T>(
            IEnumerable<AiChatMessage> messages,
            IRuntimeAgentSession? session,
            JsonSerializerOptions serializerOptions,
            RuntimeAgentRunOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
            IEnumerable<AiChatMessage> messages,
            IRuntimeAgentSession session,
            RuntimeAgentRunOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
            string input,
            IRuntimeAgentSession session,
            RuntimeAgentRunOptions? options = null,
            CancellationToken cancellationToken = default) =>
            RunStreamingAsync(
                [new AiChatMessage(AiChatRole.User, input)],
                session,
                options,
                cancellationToken);
    }

    private sealed class RecordingHarnessSession(
        RuntimeAgentMode mode) : IRuntimeAgentSession
    {
        public RuntimeAgentMode Mode { get; set; } = mode;
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static NoopAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
