using AICopilot.Services.Contracts;
using Microsoft.Agents.AI;

namespace AICopilot.AiRuntime;

internal sealed class MicrosoftAgentRuntimeSession(AgentSession session) : IRuntimeAgentSession
{
    public AgentSession Session { get; } = session;
}
