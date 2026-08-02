using System.Text.Json;
using AICopilot.AiRuntime;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
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

    [Fact]
    public async Task ConfiguredMainHarness_ShouldLetModelSwitchMode_AndKeepOneGovernedToolCatalog()
    {
        var governedInvocationCount = 0;
        var governedTool = new AiToolDefinition
        {
            Name = "GovernedDiagnostic",
            Description = "Run a governed diagnostic.",
            InvokeAsync = (_, _) =>
            {
                Interlocked.Increment(ref governedInvocationCount);
                return ValueTask.FromResult<object?>("diagnostic-ok");
            }
        };
        var model = new LanguageModel(
            "OpenAI",
            "test-model",
            "https://example.test/v1",
            null,
            new ModelParameters { MaxTokens = 4096 });
        var template = new ConversationTemplate(
            "maf-native-runtime",
            "MAF native runtime contract",
            "Use the governed tools supplied by the server.",
            model.Id,
            new TemplateSpecification());
        var provider = new ModeSwitchingChatClient();
        using var services = new ServiceCollection().BuildServiceProvider();
        var options = HarnessAgentRuntimeFactory.CreateOptions(
            new AgentRuntimeCreateRequest(
                model,
                template,
                new AiChatOptions { Tools = [governedTool] }),
            new CheckpointingChatHistoryProvider(null));

#pragma warning disable MAAI001
        var agent = new ToolInvocationGuardChatClient(provider).AsHarnessAgent(
            options,
            NullLoggerFactory.Instance,
            services);
#pragma warning restore MAAI001
        var modeProvider = agent.GetService<AgentModeProvider>();
        modeProvider.Should().NotBeNull();
        var runtime = new HarnessRuntimeChatAgent(agent, modeProvider!);
        var session = await runtime.CreateSessionAsync();

        await DrainAsync(runtime.RunStreamingAsync(
            "Switch to execute mode.",
            session));

        (await runtime.GetModeAsync(session)).Should().Be(RuntimeAgentMode.Execute);
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serialized = await runtime.SerializeSessionAsync(session, serializerOptions);
        var restored = await runtime.DeserializeSessionAsync(serialized, serializerOptions);
        (await runtime.GetModeAsync(restored)).Should().Be(RuntimeAgentMode.Execute);

        await DrainAsync(runtime.RunStreamingAsync(
            "Run the governed diagnostic.",
            restored));

        governedInvocationCount.Should().Be(1);
        provider.ToolCatalogs.Should().HaveCount(4);
        provider.ToolCatalogs.Should().AllSatisfy(catalog =>
        {
            catalog.Should().Contain("mode_get");
            catalog.Should().Contain("mode_set");
            catalog.Should().Contain("GovernedDiagnostic");
            catalog.Should().Equal(provider.ToolCatalogs[0]);
        });
        provider.AllowMultipleToolCalls.Should().OnlyContain(value => value == false);
    }

    private static async Task DrainAsync(IAsyncEnumerable<RuntimeAgentUpdate> updates)
    {
        await foreach (var _ in updates)
        {
        }
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

    private sealed class ModeSwitchingChatClient : IChatClient
    {
        private readonly Queue<AIContent[]> responses = new(
        [
            [
                new FunctionCallContent(
                    "mode-call",
                    "mode_set",
                    new Dictionary<string, object?> { ["mode"] = "execute" })
            ],
            [new TextContent("Mode changed.")],
            [
                new FunctionCallContent(
                    "diagnostic-call",
                    "GovernedDiagnostic",
                    new Dictionary<string, object?>())
            ],
            [new TextContent("Diagnostic complete.")]
        ]);

        public List<IReadOnlyList<string>> ToolCatalogs { get; } = [];

        public List<bool?> AllowMultipleToolCalls { get; } = [];

        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CaptureOptions(options);
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, NextResponse())));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            CaptureOptions(options);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, NextResponse());
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        private AIContent[] NextResponse()
        {
            responses.Should().NotBeEmpty("the Harness should use the scripted provider calls");
            return responses.Dequeue();
        }

        private void CaptureOptions(ChatOptions? options)
        {
            AllowMultipleToolCalls.Add(options?.AllowMultipleToolCalls);
            ToolCatalogs.Add(options?.Tools?
                .Select(tool => tool is AIFunction function
                    ? function.Name
                    : tool.GetService<AIFunction>()?.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray() ?? []);
        }
    }
}
