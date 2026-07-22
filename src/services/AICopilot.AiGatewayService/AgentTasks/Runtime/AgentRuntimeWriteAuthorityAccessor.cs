using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentRuntimeWriteAuthority(
    AgentNodeRunId NodeRunId,
    long TaskFencingToken,
    long NodeFencingToken);

internal sealed class AgentRuntimeWriteAuthorityAccessor
{
    private AgentRuntimeWriteAuthority? current;

    public AgentRuntimeWriteAuthority? Current => current;

    public IDisposable Push(AgentRuntimeWriteAuthority authority)
    {
        var previous = current;
        current = authority;
        return new AuthorityScope(this, previous);
    }

    private sealed class AuthorityScope(
        AgentRuntimeWriteAuthorityAccessor owner,
        AgentRuntimeWriteAuthority? previous)
        : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.current = previous;
            }
        }
    }
}
