using AICopilot.Services.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiRuntime;

internal sealed class HarnessAgentRuntimeFactory(
    IServiceScopeFactory serviceScopeFactory,
    ModelChatClientFactory chatClientFactory,
    ILoggerFactory loggerFactory) : IHarnessAgentRuntimeFactory
{
    public ScopedRuntimeAgent Create(HarnessAgentRuntimeCreateRequest request)
    {
        var scope = serviceScopeFactory.CreateScope();
        try
        {
            HarnessAgent? agent = null;
            var historyProvider = new CheckpointingChatHistoryProvider(
                request.CheckpointSink is null
                    ? null
                    : async (session, cancellationToken) =>
                    {
                        if (agent is null)
                        {
                            throw new InvalidOperationException(
                                "Harness agent was not initialized before checkpoint persistence.");
                        }

                        var serialized = await agent.SerializeSessionAsync(
                            session,
                            jsonSerializerOptions: null,
                            cancellationToken);
                        await request.CheckpointSink.PersistAsync(
                            serialized.GetRawText(),
                            cancellationToken);
                    });
            var modelClient = chatClientFactory.Create(
                request.Runtime,
                scope.ServiceProvider);
            var guardedClient = new ToolInvocationGuardChatClient(modelClient);
            var harnessOptions = CreateOptions(request.Runtime, historyProvider);
            agent = guardedClient.AsHarnessAgent(
                harnessOptions,
                loggerFactory,
                scope.ServiceProvider);
            var modeProvider = agent.GetService<AgentModeProvider>()
                ?? throw new InvalidOperationException(
                    "Harness AgentModeProvider is required for the main chat runtime.");

            return new ScopedRuntimeAgent(
                new HarnessRuntimeChatAgent(agent, modeProvider),
                new HarnessRuntimeHandle(agent, scope));
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public bool CanCreate(string providerName)
    {
        using var scope = serviceScopeFactory.CreateScope();
        return chatClientFactory.CanCreate(providerName, scope.ServiceProvider);
    }

    internal static HarnessAgentOptions CreateOptions(
        AgentRuntimeCreateRequest request,
        ChatHistoryProvider historyProvider)
    {
        var chatOptions = RuntimeToolAdapter.ToChatOptions(request.Options);
        chatOptions.AllowMultipleToolCalls = false;

#pragma warning disable MAAI001
        return new HarnessAgentOptions
        {
            Name = request.Template.Name,
            Description = request.Template.Description,
            ChatOptions = chatOptions,
            ChatHistoryProvider = historyProvider,
            MaximumIterationsPerRequest = 8,
            DisableToolAutoApproval = true,
            DisableApprovalNotRequiredFunctionBypassing = false,
            DisableApprovalResponseBinding = false,
            DisableFileMemory = true,
            FileMemoryStore = null,
            FileAccessStore = null,
            DisableWebSearch = true,
            DisableTodoProvider = false,
            DisableAgentModeProvider = false,
            AgentModeProviderOptions = null,
            DisableAgentSkillsProvider = true,
            AgentSkillsSource = null,
            BackgroundAgents = Array.Empty<AIAgent>(),
            LoopEvaluators = Array.Empty<LoopEvaluator>(),
            DisableCompaction = true
        };
#pragma warning restore MAAI001
    }

    private sealed class HarnessRuntimeHandle(
        HarnessAgent agent,
        IServiceScope scope) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            object agentObject = agent;
            if (agentObject is IAsyncDisposable asyncDisposableAgent)
            {
                await asyncDisposableAgent.DisposeAsync();
            }
            else if (agentObject is IDisposable disposableAgent)
            {
                disposableAgent.Dispose();
            }

            if (scope is IAsyncDisposable asyncDisposableScope)
            {
                await asyncDisposableScope.DisposeAsync();
            }
            else
            {
                scope.Dispose();
            }
        }
    }
}
