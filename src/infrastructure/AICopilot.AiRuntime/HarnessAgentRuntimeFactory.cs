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
            var toolSurfacePolicy = new HarnessToolSurfacePolicy(
                request.Runtime.Options.Tools.Select(tool => tool.Name));
            var guardedClient = new ToolSurfaceGuardChatClient(
                modelClient,
                toolSurfacePolicy);
            var harnessOptions = CreateOptions(request.Runtime, historyProvider);
            agent = guardedClient.AsHarnessAgent(
                harnessOptions,
                loggerFactory,
                scope.ServiceProvider);
            var modeProvider = agent.GetService<AgentModeProvider>()
                ?? throw new InvalidOperationException(
                    "Harness AgentModeProvider is required for the main chat runtime.");

            return new ScopedRuntimeAgent(
                new HarnessRuntimeChatAgent(agent, modeProvider, toolSurfacePolicy),
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

    private static HarnessAgentOptions CreateOptions(
        AgentRuntimeCreateRequest request,
        ChatHistoryProvider historyProvider)
    {
#pragma warning disable MAAI001
        return new HarnessAgentOptions
        {
            Name = request.Template.Name,
            Description = request.Template.Description,
            ChatOptions = RuntimeToolAdapter.ToChatOptions(request.Options),
            ChatHistoryProvider = historyProvider,
            MaximumIterationsPerRequest = 8,
            DisableToolAutoApproval = false,
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions
            {
                AutoApprovalRules =
                    Array.Empty<Func<ToolAutoApprovalRuleContext, ValueTask<bool>>>()
            },
            DisableApprovalNotRequiredFunctionBypassing = false,
            DisableApprovalResponseBinding = false,
            DisableFileMemory = true,
            FileMemoryStore = null,
            FileAccessStore = null,
            DisableWebSearch = true,
            DisableTodoProvider = false,
            DisableAgentModeProvider = false,
            AgentModeProviderOptions = new AgentModeProviderOptions
            {
                DefaultMode = "plan",
                Instructions =
                    """
                    Available operating modes: {available_modes}.
                    Current operating mode: {current_mode}.
                    Use mode_get when you need to confirm the mode.
                    Mode changes are authorized only through the server session endpoint.
                    Never call mode_set.
                    """,
                Modes =
                [
                    new AgentModeProviderOptions.AgentMode(
                        "plan",
                        "Plan interactively. You may manage todos and inspect the current mode, but you must not call application, business query, RAG, MCP, Cloud, MES, ERP, or write tools."),
                    new AgentModeProviderOptions.AgentMode(
                        "execute",
                        "Execute the user's approved request using only the tools exposed by the server for this session. Never change mode yourself.")
                ]
            },
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
