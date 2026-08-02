using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.UnitTests;

public sealed class MafNativeModeContractTests
{
    [Fact]
    public async Task UnfilteredHarnessAgent_ShouldExposeOfficialModeTools_AndPersistDefaultModes()
    {
        var chatClient = new ToolCatalogCapturingChatClient();
        using var services = new ServiceCollection().BuildServiceProvider();

#pragma warning disable MAAI001
        var agent = new HarnessAgent(
            chatClient,
            new HarnessAgentOptions
            {
                Name = "maf-native-mode-contract",
                MaximumIterationsPerRequest = 2,
                DisableToolAutoApproval = true,
                DisableFileMemory = true,
                DisableWebSearch = true,
                DisableTodoProvider = true,
                DisableAgentModeProvider = false,
                AgentModeProviderOptions = null,
                DisableAgentSkillsProvider = true,
                BackgroundAgents = Array.Empty<AIAgent>(),
                LoopEvaluators = Array.Empty<LoopEvaluator>(),
                DisableCompaction = true,
            },
            NullLoggerFactory.Instance,
            services);
#pragma warning restore MAAI001

        var modeProvider = agent.GetService<AgentModeProvider>();
        modeProvider.Should().NotBeNull();

        var session = await agent.CreateSessionAsync();
        (await modeProvider!.GetModeAsync(session)).Should().Be("plan");

        _ = await agent.RunAsync("Inspect the current operating mode.", session);

        chatClient.LastToolNames.Should().Contain("mode_get");
        chatClient.LastToolNames.Should().Contain("mode_set");

        await modeProvider.SetModeAsync(session, "execute");
        _ = await agent.RunAsync("Continue with the same session.", session);

        (await modeProvider.GetModeAsync(session)).Should().Be("execute");
    }

    private sealed class ToolCatalogCapturingChatClient : IChatClient
    {
        public IReadOnlyList<string> LastToolNames { get; private set; } = [];

        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastToolNames = options?.Tools?
                .Select(tool => tool is AIFunction function
                    ? function.Name
                    : tool.GetService<AIFunction>()?.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray() ?? [];

            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;
    }
}
