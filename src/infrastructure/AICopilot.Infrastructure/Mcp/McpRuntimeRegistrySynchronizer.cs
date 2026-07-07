using AICopilot.AgentPlugin;
using Microsoft.Extensions.Logging;

namespace AICopilot.Infrastructure.Mcp;

public sealed class McpRuntimeOptions
{
    public const int DefaultRefreshIntervalSeconds = 30;
    public const int MinimumRefreshIntervalSeconds = 5;
    public const int MaximumRefreshIntervalSeconds = 300;

    public int RefreshIntervalSeconds { get; init; } = DefaultRefreshIntervalSeconds;

    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(Math.Clamp(
        RefreshIntervalSeconds,
        MinimumRefreshIntervalSeconds,
        MaximumRefreshIntervalSeconds));
}

public sealed record McpRuntimeServerState(Guid ServerId, string Name, uint RowVersion);

public interface IMcpRuntimeRegistrationProvider
{
    Task<IReadOnlyList<McpRuntimeServerState>> ListCandidateServersAsync(CancellationToken cancellationToken);

    Task<McpRuntimeRegistration?> CreateRegistrationAsync(
        McpRuntimeServerState server,
        CancellationToken cancellationToken);
}

public sealed class McpRuntimeRegistration(
    Guid serverId,
    string serverName,
    uint rowVersion,
    IAgentPlugin plugin,
    McpRuntimeClientHandle clientHandle)
    : IAsyncDisposable
{
    public Guid ServerId { get; } = serverId;

    public string ServerName { get; } = serverName;

    public uint RowVersion { get; } = rowVersion;

    public IAgentPlugin Plugin { get; } = plugin;

    public McpRuntimeClientHandle ClientHandle { get; } = clientHandle;

    public ValueTask DisposeAsync()
    {
        return ClientHandle.DisposeAsync();
    }
}

public sealed class McpRuntimeClientHandle : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IAsyncDisposable client;
    private TaskCompletionSource? idleSignal;
    private int activeInvocations;
    private bool acceptingInvocations = true;
    private bool disposed;

    public McpRuntimeClientHandle(IAsyncDisposable client)
    {
        this.client = client;
        Client = client;
    }

    public IAsyncDisposable Client { get; }

    public IDisposable AcquireInvocation()
    {
        lock (gate)
        {
            if (!acceptingInvocations)
            {
                throw new ObjectDisposedException(nameof(McpRuntimeClientHandle));
            }

            activeInvocations++;
            return new InvocationLease(this);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? waitForIdle = null;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            acceptingInvocations = false;
            if (activeInvocations > 0)
            {
                idleSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                waitForIdle = idleSignal.Task;
            }
            else
            {
                disposed = true;
            }
        }

        if (waitForIdle is not null)
        {
            await waitForIdle.ConfigureAwait(false);
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
            }
        }

        await client.DisposeAsync().ConfigureAwait(false);
    }

    private void ReleaseInvocation()
    {
        TaskCompletionSource? signal = null;
        lock (gate)
        {
            activeInvocations--;
            if (activeInvocations == 0 && !acceptingInvocations)
            {
                signal = idleSignal;
            }
        }

        signal?.TrySetResult();
    }

    private sealed class InvocationLease(McpRuntimeClientHandle owner) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            owner.ReleaseInvocation();
        }
    }
}

public sealed class McpRuntimeRegistrySynchronizer(
    IAgentPluginRegistry pluginRegistry,
    ILogger<McpRuntimeRegistrySynchronizer> logger)
    : IAsyncDisposable
{
    private readonly Dictionary<string, McpRuntimeRegistration> activeRegistrations =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task ReconcileAsync(
        IMcpRuntimeRegistrationProvider registrationProvider,
        CancellationToken cancellationToken)
    {
        var candidateServers = await registrationProvider.ListCandidateServersAsync(cancellationToken);
        var candidatesByName = candidateServers
            .GroupBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var removedNames = activeRegistrations.Keys
            .Where(name => !candidatesByName.ContainsKey(name))
            .ToArray();
        foreach (var removedName in removedNames)
        {
            await RemoveRegistrationAsync(removedName);
        }

        foreach (var candidate in candidatesByName.Values)
        {
            if (activeRegistrations.TryGetValue(candidate.Name, out var existing)
                && existing.ServerId == candidate.ServerId
                && existing.RowVersion == candidate.RowVersion)
            {
                continue;
            }

            var replacement = await registrationProvider.CreateRegistrationAsync(candidate, cancellationToken);
            if (replacement is null)
            {
                await RemoveRegistrationAsync(candidate.Name);
                continue;
            }

            await ReplaceRegistrationAsync(replacement);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var names = activeRegistrations.Keys.ToArray();
        foreach (var name in names)
        {
            await RemoveRegistrationAsync(name);
        }
    }

    private async Task ReplaceRegistrationAsync(McpRuntimeRegistration registration)
    {
        activeRegistrations.TryGetValue(registration.ServerName, out var previous);

        try
        {
            pluginRegistry.RegisterAgentPlugin(registration.Plugin);
            activeRegistrations[registration.ServerName] = registration;
        }
        catch
        {
            await DisposeRegistrationAsync(registration);
            throw;
        }

        if (previous is not null)
        {
            await DisposeRegistrationAsync(previous);
        }
    }

    private async Task RemoveRegistrationAsync(string name)
    {
        pluginRegistry.UnregisterAgentPlugin(name);
        if (activeRegistrations.Remove(name, out var registration))
        {
            await DisposeRegistrationAsync(registration);
            logger.LogInformation("Unregistered MCP runtime plugin {Name}.", name);
        }
    }

    private async Task DisposeRegistrationAsync(McpRuntimeRegistration registration)
    {
        try
        {
            await registration.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Failed to dispose MCP runtime client for {Name}. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                registration.ServerName,
                ex.GetType().Name);
        }
    }
}
