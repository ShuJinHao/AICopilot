using System.ComponentModel;
using System.Text.Json;
using AICopilot.AgentPlugin;
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

public sealed class AiRuntimeAdapterTests
{
    [Fact]
    public async Task RuntimeToolAdapter_ShouldAdaptPluginMethodTool_AndInvokeThroughAiFunction()
    {
        var plugin = new EchoPlugin();
        var tool = plugin.GetTools()!.Single();

        var chatOptions = RuntimeToolAdapter.ToChatOptions(new AiChatOptions { Tools = [tool] });
        var function = chatOptions.Tools!.OfType<AIFunction>().Single();

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["value"] = "line-1" }),
            CancellationToken.None);

        function.Name.Should().Be(nameof(EchoPlugin.Echo));
        result?.ToString().Should().Be("echo:line-1");
    }

    [Fact]
    public void RuntimeToolAdapter_ShouldWrapApprovalRequiredTools()
    {
        var plugin = new EchoPlugin();
        var tool = plugin.GetTools()!.Single().WithRequiresApproval(true);

        var chatOptions = RuntimeToolAdapter.ToChatOptions(new AiChatOptions { Tools = [tool] });
        var adaptedTool = chatOptions.Tools!.Single();

        adaptedTool.Name.Should().Be(nameof(EchoPlugin.Echo));
        adaptedTool.GetType().Name.Should().Contain("ApprovalRequired");
    }

    [Fact]
    public void HarnessRuntimeFactory_ShouldRequireSingleToolCallsWithoutStandingApprovalRules()
    {
        var model = new LanguageModel(
            "OpenAI",
            "test-model",
            "https://example.test/v1",
            null,
            new ModelParameters { MaxTokens = 4096 });
        var template = new ConversationTemplate(
            "test-template",
            "test",
            "system prompt",
            model.Id,
            new TemplateSpecification());

        var options = HarnessAgentRuntimeFactory.CreateOptions(
            new AgentRuntimeCreateRequest(model, template, new AiChatOptions()),
            new CheckpointingChatHistoryProvider(null));

        options.DisableToolAutoApproval.Should().BeTrue();
        options.ToolApprovalAgentOptions.Should().BeNull();
        options.ChatOptions.Should().NotBeNull();
        options.ChatOptions!.AllowMultipleToolCalls.Should().BeFalse();
        options.AgentModeProviderOptions.Should().BeNull();
        options.DisableFileMemory.Should().BeTrue();
#pragma warning disable MAAI001
        options.FileMemoryStore.Should().BeNull();
        options.FileAccessStore.Should().BeNull();
#pragma warning restore MAAI001
        options.DisableWebSearch.Should().BeTrue();
        options.DisableAgentSkillsProvider.Should().BeTrue();
        options.AgentSkillsSource.Should().BeNull();
#pragma warning disable MAAI001
        options.BackgroundAgents.Should().BeEmpty();
        options.LoopEvaluators.Should().BeEmpty();
        options.DisableCompaction.Should().BeTrue();
#pragma warning restore MAAI001
    }

    [Fact]
    public void RuntimeContentMapper_ShouldMapSdkUpdatesToOwnRuntimeContents()
    {
        var call = new FunctionCallContent(
            "call-1",
            "Echo",
            new Dictionary<string, object?> { ["value"] = "line-1" });
        var approval = new ToolApprovalRequestContent("approval-1", call);
        var usage = new UsageContent(new UsageDetails
        {
            InputTokenCount = 3,
            OutputTokenCount = 5,
            TotalTokenCount = 8
        });

        var contents = RuntimeContentMapper.ToRuntimeContents(
        [
            new TextContent("hello"),
            call,
            new FunctionResultContent("call-1", "ok"),
            approval,
            usage
        ]);

        contents.OfType<AiTextContent>().Single().Text.Should().Be("hello");
        contents.OfType<AiToolCallContent>().Single().ToolCall.Name.Should().Be("Echo");
        contents.OfType<AiFunctionResultContent>().Single().Result.Should().Be("ok");
        contents.OfType<AiToolApprovalRequestContent>().Single().Request.RequestId.Should().Be("approval-1");
        contents.OfType<AiUsageContent>().Single().Details.TotalTokenCount.Should().Be(8);
    }

    [Fact]
    public async Task ToolInvocationGuard_ShouldPreserveEffectiveToolSetAndOrder()
    {
        var inner = new CapturingChatClient();
        var guarded = new ToolInvocationGuardChatClient(inner);
        var options = RuntimeToolAdapter.ToChatOptions(new AiChatOptions
        {
            Tools =
            [
                CreateTool("BusinessQuery"),
                CreateTool("KnowledgeQuery"),
                CreateTool("mode_get"),
                CreateTool("mode_set"),
                CreateTool("todos_add"),
                CreateTool("ApplicationDiagnostic")
            ]
        });

        _ = await guarded.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "invoke")],
            options);

        inner.LastToolNames.Should().Equal(
            "BusinessQuery",
            "KnowledgeQuery",
            "mode_get",
            "mode_set",
            "todos_add",
            "ApplicationDiagnostic");
        inner.LastTools.Should().Equal(options.Tools!);
        inner.LastAllowMultipleToolCalls.Should().BeFalse();
    }

    [Fact]
    public async Task ToolInvocationGuard_ShouldFailClosedForUnexposedToolCall()
    {
        var inner = new CapturingChatClient(
            new FunctionCallContent(
                "call-1",
                "ForgedTool",
                new Dictionary<string, object?>()));
        var guarded = new ToolInvocationGuardChatClient(inner);
        var options = RuntimeToolAdapter.ToChatOptions(new AiChatOptions
        {
            Tools = [CreateTool("BusinessQuery")]
        });

        var act = () => guarded.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "forge")],
            options);

        await act.Should().ThrowAsync<HarnessToolInvocationViolationException>();
    }

    [Theory]
    [InlineData("call-2", "BusinessQuery")]
    [InlineData("call-1", "KnowledgeQuery")]
    public async Task ToolInvocationGuard_ShouldRejectMultipleToolCallsFromOneProviderResponse(
        string secondCallId,
        string secondToolName)
    {
        var inner = new CapturingChatClient(
            new FunctionCallContent(
                "call-1",
                "BusinessQuery",
                new Dictionary<string, object?>()),
            new FunctionCallContent(
                secondCallId,
                secondToolName,
                new Dictionary<string, object?>()));
        var guarded = new ToolInvocationGuardChatClient(inner);
        var options = RuntimeToolAdapter.ToChatOptions(new AiChatOptions
        {
            Tools = [CreateTool("BusinessQuery"), CreateTool("KnowledgeQuery")]
        });

        var act = () => guarded.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "return two calls")],
            options);

        await act.Should().ThrowAsync<AgentRuntimeMultipleToolCallsException>();
    }

    [Fact]
    public async Task ToolInvocationGuard_ShouldRejectAlwaysApproveResponsesBeforeProviderDispatch()
    {
        var inner = new CapturingChatClient();
        var guarded = new ToolInvocationGuardChatClient(inner);
        var approval = new ToolApprovalRequestContent(
            "approval-1",
            new FunctionCallContent(
                "call-1",
                "BusinessQuery",
                new Dictionary<string, object?>()));
        var message = new ChatMessage(
            ChatRole.User,
            [approval.CreateAlwaysApproveToolResponse()]);

        var act = () => guarded.GetResponseAsync([message]);

        await act.Should().ThrowAsync<HarnessAlwaysApproveRejectedException>();
        inner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task HarnessSession_ShouldInitializePlanMode_AndRoundTrip()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
#pragma warning disable MAAI001
        var agent = new CapturingChatClient().AsHarnessAgent(
            new HarnessAgentOptions
            {
                Name = "session-test",
                ChatHistoryProvider = new CheckpointingChatHistoryProvider(null),
                MaximumIterationsPerRequest = 8,
                DisableToolAutoApproval = true,
                DisableApprovalNotRequiredFunctionBypassing = false,
                DisableApprovalResponseBinding = false,
                DisableFileMemory = true,
                DisableWebSearch = true,
                DisableTodoProvider = false,
                DisableAgentModeProvider = false,
                AgentModeProviderOptions = null,
                DisableAgentSkillsProvider = true,
                BackgroundAgents = Array.Empty<AIAgent>(),
                LoopEvaluators = Array.Empty<LoopEvaluator>(),
                DisableCompaction = true
            },
            NullLoggerFactory.Instance,
            services);
#pragma warning restore MAAI001
        var modeProvider = agent.GetService<AgentModeProvider>();
        modeProvider.Should().NotBeNull();
        var runtimeAgent = new MicrosoftAgentRuntimeChatAgent(agent);

        var session = await runtimeAgent.CreateSessionAsync();
        var mode = await modeProvider!.GetModeAsync(
            MicrosoftAgentRuntimeChatAgent.UnwrapSession(session));
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serialized = await runtimeAgent.SerializeSessionAsync(session, serializerOptions);
        var restored = await runtimeAgent.DeserializeSessionAsync(serialized, serializerOptions);

        mode.Should().Be("plan");
        (await modeProvider.GetModeAsync(
            MicrosoftAgentRuntimeChatAgent.UnwrapSession(restored))).Should().Be("plan");
    }

    private static AiToolDefinition CreateTool(string name) =>
        new()
        {
            Name = name,
            Description = "test tool",
            InvokeAsync = (_, _) => ValueTask.FromResult<object?>(null)
        };

    private sealed class EchoPlugin : AgentPluginBase
    {
        [Description("Echo a value.")]
        public string Echo(string value)
        {
            return $"echo:{value}";
        }
    }

    private sealed class CapturingChatClient(params AIContent[] responseContents) : IChatClient
    {
        public IReadOnlyList<string> LastToolNames { get; private set; } = [];

        public IReadOnlyList<AITool> LastTools { get; private set; } = [];

        public int CallCount { get; private set; }

        public bool? LastAllowMultipleToolCalls { get; private set; }

        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastAllowMultipleToolCalls = options?.AllowMultipleToolCalls;
            LastTools = options?.Tools?.ToArray() ?? [];
            LastToolNames = options?.Tools?
                .Select(tool => tool is AIFunction function
                    ? function.Name
                    : tool.GetService<AIFunction>()?.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray() ?? [];
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, responseContents)));
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
