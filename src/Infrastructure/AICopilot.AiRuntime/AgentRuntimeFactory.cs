using AICopilot.Services.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AICopilot.AiRuntime;

internal sealed class AgentRuntimeFactory(
    IServiceScopeFactory serviceScopeFactory,
    IHostEnvironment hostEnvironment) : IAgentRuntimeFactory
{
    public ScopedRuntimeAgent Create(AgentRuntimeCreateRequest request)
    {
        var scope = serviceScopeFactory.CreateScope();
        try
        {
            var chatClientProvider = scope.ServiceProvider
                .GetServices<IChatClientProvider>()
                .FirstOrDefault(provider => provider.CanHandle(request.Model.Provider));

            if (chatClientProvider is null)
            {
                throw new InvalidOperationException($"No chat client provider is registered for '{request.Model.Provider}'.");
            }

            var chatClientBuilder = chatClientProvider
                .CreateClient(request.Model)
                .AsBuilder()
                .UseFunctionInvocation()
                .UseOpenTelemetry(
                    sourceName: nameof(AiRuntime),
                    configure: cfg => cfg.EnableSensitiveData = hostEnvironment.IsDevelopment());

            var agentOptions = new ChatClientAgentOptions
            {
                Name = request.Template.Name,
                ChatOptions = RuntimeToolAdapter.ToChatOptions(request.Options)
            };

            var agent = chatClientBuilder.BuildAIAgent(agentOptions, services: scope.ServiceProvider);
            return new ScopedRuntimeAgent(new MicrosoftAgentRuntimeChatAgent(agent), new AgentRuntimeHandle(agent, scope));
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
        return scope.ServiceProvider
            .GetServices<IChatClientProvider>()
            .Any(provider => provider.CanHandle(providerName));
    }

    private sealed class AgentRuntimeHandle(ChatClientAgent agent, IServiceScope scope) : IAsyncDisposable
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
