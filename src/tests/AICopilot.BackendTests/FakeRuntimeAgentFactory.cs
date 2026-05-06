using System.Runtime.CompilerServices;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.BackendTests;

internal sealed class FakeRuntimeAgentFactory : IAgentRuntimeFactory
{
    public const string ProviderName = "FakeEval";

    private readonly Queue<IReadOnlyList<RuntimeAgentUpdate>> scripts = [];

    public AgentRuntimeCreateRequest? LastCreateRequest { get; private set; }

    public FakeRuntimeChatAgent? LastAgent { get; private set; }

    public FakeRuntimeAgentRun? LastRun => LastAgent?.LastRun;

    public bool CanCreate(string providerName)
    {
        return string.Equals(providerName, ProviderName, StringComparison.OrdinalIgnoreCase);
    }

    public ScopedRuntimeAgent Create(AgentRuntimeCreateRequest request)
    {
        LastCreateRequest = request;
        var script = scripts.Count == 0
            ? [new RuntimeAgentUpdate([new AiTextContent("fake runtime completed")])]
            : scripts.Dequeue();
        LastAgent = new FakeRuntimeChatAgent(script);
        return new ScopedRuntimeAgent(LastAgent, NoopAsyncDisposable.Instance);
    }

    public void EnqueueScript(params RuntimeAgentUpdate[] updates)
    {
        scripts.Enqueue(updates);
    }

    public static LanguageModel CreateModel()
    {
        return new LanguageModel(
            ProviderName,
            "fake-eval-model",
            "http://localhost/fake-eval",
            "fake-key",
            new ModelParameters { MaxTokens = 4096, Temperature = 0.2f });
    }

    public static ConversationTemplate CreateTemplate(LanguageModel model)
    {
        return new ConversationTemplate(
            "fake-eval-template",
            "fake eval template",
            "You are a controlled manufacturing assistant.",
            model.Id,
            new TemplateSpecification { MaxTokens = 512, Temperature = 0.2f });
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static NoopAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed record FakeRuntimeAgentRun(
    IReadOnlyList<AiChatMessage> Messages,
    string InputText,
    RuntimeAgentRunOptions? Options);

internal sealed class FakeRuntimeAgentSession(Guid id) : IRuntimeAgentSession
{
    public Guid Id { get; } = id;
}

internal sealed class FakeRuntimeChatAgent(IReadOnlyList<RuntimeAgentUpdate> script) : IRuntimeChatAgent
{
    public FakeRuntimeAgentRun? LastRun { get; private set; }

    public Task<IRuntimeAgentSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IRuntimeAgentSession>(new FakeRuntimeAgentSession(Guid.NewGuid()));
    }

    public Task<string> SerializeSessionAsync(
        IRuntimeAgentSession session,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default)
    {
        var id = session is FakeRuntimeAgentSession fakeSession ? fakeSession.Id : Guid.Empty;
        return Task.FromResult(JsonSerializer.Serialize(id, serializerOptions));
    }

    public Task<IRuntimeAgentSession> DeserializeSessionAsync(
        string serializedSessionState,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default)
    {
        var id = JsonSerializer.Deserialize<Guid>(serializedSessionState, serializerOptions);
        return Task.FromResult<IRuntimeAgentSession>(new FakeRuntimeAgentSession(id));
    }

    public Task<StructuredAgentResponse<T>> RunStructuredAsync<T>(
        IEnumerable<AiChatMessage> messages,
        IRuntimeAgentSession? session,
        JsonSerializerOptions serializerOptions,
        RuntimeAgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToArray();
        LastRun = new FakeRuntimeAgentRun(messageList, ExtractInputText(messageList), options);
        return Task.FromResult(new StructuredAgentResponse<T>("fake structured response", default));
    }

    public async IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
        IEnumerable<AiChatMessage> messages,
        IRuntimeAgentSession session,
        RuntimeAgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToArray();
        LastRun = new FakeRuntimeAgentRun(messageList, ExtractInputText(messageList), options);
        foreach (var update in script)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return update;
        }
    }

    public async IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
        string input,
        IRuntimeAgentSession session,
        RuntimeAgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastRun = new FakeRuntimeAgentRun([new AiChatMessage(AiChatRole.User, input)], input, options);
        foreach (var update in script)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return update;
        }
    }

    private static string ExtractInputText(IReadOnlyList<AiChatMessage> messages)
    {
        return messages.LastOrDefault(message => message.Role == AiChatRole.User)?.Text ?? string.Empty;
    }
}
