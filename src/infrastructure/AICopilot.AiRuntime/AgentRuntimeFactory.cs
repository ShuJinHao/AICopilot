using AICopilot.Services.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.AiRuntime;

/// <summary>
/// Lightweight agent path used by routing, classification, RAG, and
/// Text-to-SQL. Main chat is created by <see cref="HarnessAgentRuntimeFactory"/>.
/// </summary>
internal sealed class AgentRuntimeFactory(
    IServiceScopeFactory serviceScopeFactory,
    ModelChatClientFactory chatClientFactory) : IAgentRuntimeFactory
{
    public ScopedRuntimeAgent Create(AgentRuntimeCreateRequest request)
    {
        var scope = serviceScopeFactory.CreateScope();
        try
        {
            var chatClient = chatClientFactory.Create(request, scope.ServiceProvider);
            var agentOptions = new ChatClientAgentOptions
            {
                Name = request.Template.Name,
                ChatOptions = RuntimeToolAdapter.ToChatOptions(request.Options)
            };
            var agent = chatClient
                .AsBuilder()
                .UseFunctionInvocation()
                .BuildAIAgent(agentOptions, services: scope.ServiceProvider);

            return new ScopedRuntimeAgent(
                new MicrosoftAgentRuntimeChatAgent(agent),
                new AgentRuntimeHandle(agent, scope));
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

    private sealed class AgentRuntimeHandle(
        AIAgent agent,
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
