using System.Net.Http.Headers;
using System.Threading.Channels;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AICopilot.AspireIntegrationTestKit;

public class AICopilotAppEnvironment : IAsyncDisposable
{
    private const string TestPostgresPassword = "TestPg123!";
    private const string BootstrapUserName = "admin";
    private const string BootstrapPassword = "Password123!";
    private static readonly TimeSpan StageTimeout = TimeSpan.FromMinutes(2);
    private static readonly string[] ProxyEnvironmentVariables =
    [
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "ALL_PROXY",
        "http_proxy",
        "https_proxy",
        "all_proxy"
    ];
    private const string LocalNoProxy =
        "localhost,127.0.0.1,::1,[::1],0.0.0.0,host.docker.internal,*.local,169.254.0.0/16";

    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);
    private readonly FakeAiProviderHost _fakeAiProvider = new();
    private readonly ForwardedResourceLogCaptureProvider _forwardedResourceLogs = new();

    private DistributedApplication? _app;

    public HttpClient HttpClient { get; private set; } = null!;

    public Uri FakeAiBaseUri => _fakeAiProvider.BaseUri;

    public string BootstrapAdminUserName => BootstrapUserName;

    public string BootstrapAdminPassword => BootstrapPassword;

    protected virtual bool EnableRagWorker => true;

    protected virtual bool EnableDataWorker => true;

    public async Task InitializeAsync()
    {
        await RunStageAsync("Fake AI startup", () => _fakeAiProvider.StartAsync(), TimeSpan.FromSeconds(30));
        RunStage("Configure test environment", ConfigureEnvironment);

        try
        {
            var builder = await RunStageAsync(
                "Create distributed application builder",
                () => DistributedApplicationTestingBuilder.CreateAsync<Projects.AICopilot_AppHost>());

            RunStage("Configure distributed application logging", () =>
            {
                builder.Services.AddLogging(logging =>
                {
                    logging.AddConsole();
                    logging.AddProvider(_forwardedResourceLogs);
                });
            });

            _app = await RunStageAsync("Build distributed application", () => builder.BuildAsync());
            await RunStageAsync("Start distributed application", () => _app.StartAsync());

            await WaitForHealthyResourceAsync("postgres");
            await WaitForHealthyResourceAsync("eventbus");
            await WaitForHealthyResourceAsync("qdrant");
            await WaitForHealthyResourceAsync("final-agent-context-redis");
            await WaitForHealthyResourceAsync("aicopilot-httpapi");

            if (EnableRagWorker)
            {
                await WaitForResourceRunningAsync("rag-worker");
            }

            if (EnableDataWorker)
            {
                await WaitForResourceRunningAsync("data-worker");
            }

            HttpClient = RunStage(
                "Create HttpClient for aicopilot-httpapi",
                () => _app.CreateHttpClient("aicopilot-httpapi"));
        }
        catch (DistributedApplicationException ex)
            when (ex.Message.Contains("docker", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Docker 不可用，无法启动 AICopilot 后端集成测试环境。", ex);
        }
    }

    public async Task DisposeAsync()
    {
        HttpClient?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        await _fakeAiProvider.DisposeAsync();

        foreach (var entry in _originalEnvironment)
        {
            Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeAsync();
    }

    public async Task<string> GetConnectionStringAsync(string resourceName = "ai-copilot", CancellationToken cancellationToken = default)
    {
        var connectionString = await (_app?.GetConnectionStringAsync(resourceName, cancellationToken)
            ?? throw new InvalidOperationException("测试环境尚未启动。"));

        return connectionString ?? throw new InvalidOperationException($"资源 {resourceName} 未提供连接字符串。");
    }

    public void SetAuthToken(string token)
    {
        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearAuthToken()
    {
        HttpClient.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<IReadOnlyList<string>> ExecuteWithHttpTraceLogEvidenceAsync(
        string resourceName,
        IReadOnlyCollection<string> traceIds,
        Func<Task> exerciseAsync,
        CancellationToken cancellationToken = default)
    {
        if (_app is null)
        {
            throw new InvalidOperationException("测试环境尚未启动。");
        }

        ArgumentNullException.ThrowIfNull(exerciseAsync);

        var collected = new List<string>();
        using var subscription = _forwardedResourceLogs.Subscribe(resourceName);

        await exerciseAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await foreach (var line in subscription.ReadAllAsync(timeout.Token))
            {
                collected.Add(line);
                if (ContainsAllHttpTraceEvidence(collected, traceIds))
                {
                    return collected;
                }
            }
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
        }

        var missing = traceIds.SelectMany(traceId =>
            new[] { "HTTP request started", "HTTP request completed" }
                .Where(marker => !collected.Any(line =>
                    line.Contains(marker, StringComparison.Ordinal) &&
                    line.Contains(traceId, StringComparison.Ordinal)))
                .Select(marker => $"{traceId}:{marker}"));
        throw new TimeoutException(
            $"Resource '{resourceName}' logs did not contain required trace evidence. " +
            $"Missing=[{string.Join(',', missing)}]; collectedLines={collected.Count}.");
    }

    private static bool ContainsAllHttpTraceEvidence(
        IReadOnlyCollection<string> collected,
        IReadOnlyCollection<string> traceIds)
    {
        return traceIds.All(traceId =>
            collected.Any(line =>
                line.Contains("HTTP request started", StringComparison.Ordinal) &&
                line.Contains(traceId, StringComparison.Ordinal)) &&
            collected.Any(line =>
                line.Contains("HTTP request completed", StringComparison.Ordinal) &&
                line.Contains(traceId, StringComparison.Ordinal)));
    }

    private void ConfigureEnvironment()
    {
        ConfigureLocalProxyEnvironment();
        SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        SetEnvironmentVariable("Parameters__pg-password", TestPostgresPassword);
        SetEnvironmentVariable("Parameters__aicopilot-api-key-encryption-key", "test-aicopilot-api-key-encryption-key");
        SetEnvironmentVariable("Parameters__jwt-secret-key", "test-aicopilot-jwt-secret-key-at-least-64-characters-0123456789-ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        SetEnvironmentVariable("Parameters__bootstrap-admin-username", BootstrapUserName);
        SetEnvironmentVariable("Parameters__bootstrap-admin-password", BootstrapPassword);
        SetEnvironmentVariable("JwtSettings__SecretKey", "test-aicopilot-jwt-secret-key-at-least-64-characters-0123456789-ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        SetEnvironmentVariable("BootstrapAdmin__UserName", BootstrapUserName);
        SetEnvironmentVariable("BootstrapAdmin__Password", BootstrapPassword);
        SetEnvironmentVariable("AICopilotSecurity__ApiKeyEncryptionKey", "test-aicopilot-api-key-encryption-key");
        SetEnvironmentVariable(
            "ArtifactWorkspace__RootPath",
            Path.Combine(Path.GetTempPath(), "AICopilotIntegrationTests", "artifact-workspaces"));
        SetEnvironmentVariable("AppHost__EnableDockerComposeEnvironment", "false");
        SetEnvironmentVariable("AppHost__EnableWebUi", "false");
        SetEnvironmentVariable("AppHost__EnablePgWeb", "false");
        SetEnvironmentVariable("AppHost__PersistentContainers", "false");
        SetEnvironmentVariable("AppHost__EnableRagWorker", EnableRagWorker ? "true" : "false");
        SetEnvironmentVariable("AppHost__EnableDataWorker", EnableDataWorker ? "true" : "false");
        SetEnvironmentVariable("Mcp__Runtime__Enabled", "false");
        SetEnvironmentVariable("RateLimiting__Default__TokenLimit", "1000");
        SetEnvironmentVariable("RateLimiting__Default__TokensPerPeriod", "1000");
        SetEnvironmentVariable("RateLimiting__Login__TokenLimit", "1000");
        SetEnvironmentVariable("RateLimiting__Login__TokensPerPeriod", "1000");
        SetEnvironmentVariable("RateLimiting__IdentityManagement__TokenLimit", "1000");
        SetEnvironmentVariable("RateLimiting__IdentityManagement__TokensPerPeriod", "1000");
        SetEnvironmentVariable("RateLimiting__Chat__TokenLimit", "1000");
        SetEnvironmentVariable("RateLimiting__Chat__TokensPerPeriod", "1000");
        ConfigureAdditionalEnvironment();
    }

    protected virtual void ConfigureAdditionalEnvironment()
    {
    }

    protected void ConfigureCloudReadonlySimulationEnvironment()
    {
        SetEnvironmentVariable("CloudReadonly__Mode", "Simulation");
        SetEnvironmentVariable("CloudReadonly__Simulation__Enabled", "true");
        SetEnvironmentVariable("CloudReadonly__Simulation__SeedData", "true");
        SetEnvironmentVariable("CloudReadonly__Simulation__DataSet", "ManufacturingDemo");
        SetEnvironmentVariable("CloudReadonly__Simulation__AlwaysMarkAsSimulation", "true");
        SetEnvironmentVariable("CloudReadonly__Real__Enabled", "false");
        SetEnvironmentVariable("CloudReadonly__Real__AllowProductionRead", "false");
        SetEnvironmentVariable("CloudAiRead__Enabled", "false");
    }

    private void ConfigureLocalProxyEnvironment()
    {
        foreach (var variable in ProxyEnvironmentVariables)
        {
            ClearEnvironmentVariable(variable);
        }

        SetEnvironmentVariable("NO_PROXY", LocalNoProxy);
        SetEnvironmentVariable("no_proxy", LocalNoProxy);
    }

    protected void SetEnvironmentVariable(string name, string? value)
    {
        if (!_originalEnvironment.ContainsKey(name))
        {
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }

    protected void ClearEnvironmentVariable(string name)
    {
        SetEnvironmentVariable(name, null);
    }

    private async Task WaitForHealthyResourceAsync(string resourceName)
    {
        try
        {
            await RunStageAsync(
                $"Wait for resource {resourceName} healthy",
                () => _app!.ResourceNotifications.WaitForResourceHealthyAsync(resourceName));
        }
        catch (TimeoutException)
        {
            LogResourceTimeoutContext(resourceName);
            throw;
        }
    }

    private async Task WaitForResourceRunningAsync(string resourceName)
    {
        try
        {
            await RunStageAsync(
                $"Wait for resource {resourceName} running",
                () => _app!.ResourceNotifications.WaitForResourceAsync(resourceName, KnownResourceStates.Running));
        }
        catch (TimeoutException)
        {
            LogResourceTimeoutContext(resourceName);
            throw;
        }
    }

    private void LogResourceTimeoutContext(string resourceName)
    {
        Console.WriteLine(
            $"[{DateTimeOffset.Now:O}] AICopilotAppFixture resource wait timed out for {resourceName}. " +
            $"EnableRagWorker={EnableRagWorker}, EnableDataWorker={EnableDataWorker}.");

        if (_app is null)
        {
            return;
        }

        foreach (var knownResourceName in GetKnownResourceNames())
        {
            if (!_app.ResourceNotifications.TryGetCurrentState(knownResourceName, out var resourceEvent))
            {
                Console.WriteLine($"[{DateTimeOffset.Now:O}] Resource state unavailable: {knownResourceName}");
                continue;
            }

            var snapshot = resourceEvent.Snapshot;
            Console.WriteLine(
                $"[{DateTimeOffset.Now:O}] Resource {knownResourceName}: " +
                $"state={snapshot.State?.Text ?? "<none>"}, " +
                $"health={snapshot.HealthStatus?.ToString() ?? "<none>"}, " +
                $"exitCode={snapshot.ExitCode?.ToString() ?? "<none>"}");
        }
    }

    private IEnumerable<string> GetKnownResourceNames()
    {
        yield return "postgres";
        yield return "eventbus";
        yield return "qdrant";
        yield return "final-agent-context-redis";
        yield return "aicopilot-migration";
        yield return "aicopilot-httpapi";

        if (EnableRagWorker)
        {
            yield return "rag-worker";
        }

        if (EnableDataWorker)
        {
            yield return "data-worker";
        }
    }

    private static async Task RunStageAsync(
        string name,
        Func<Task> action,
        TimeSpan? timeout = null)
    {
        await RunStageAsync<object?>(
            name,
            async () =>
            {
                await action();
                return null;
            },
            timeout);
    }

    private static async Task<T> RunStageAsync<T>(
        string name,
        Func<Task<T>> action,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? StageTimeout;
        Console.WriteLine($"[{DateTimeOffset.Now:O}] AICopilotAppFixture stage started: {name}");

        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        var stageTask = action();
        var completedTask = await Task.WhenAny(stageTask, Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token));

        if (completedTask != stageTask)
        {
            var message = $"AICopilotAppFixture stage timed out after {effectiveTimeout}: {name}";
            Console.WriteLine($"[{DateTimeOffset.Now:O}] {message}");
            throw new TimeoutException(message);
        }

        try
        {
            var result = await stageTask;
            Console.WriteLine($"[{DateTimeOffset.Now:O}] AICopilotAppFixture stage completed: {name}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:O}] AICopilotAppFixture stage failed: {name}. {ex}");
            throw;
        }
    }

    private static void RunStage(string name, Action action)
    {
        Console.WriteLine($"[{DateTimeOffset.Now:O}] AICopilotAppFixture stage started: {name}");
        try
        {
            action();
            Console.WriteLine($"[{DateTimeOffset.Now:O}] AICopilotAppFixture stage completed: {name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:O}] AICopilotAppFixture stage failed: {name}. {ex}");
            throw;
        }
    }

    private static T RunStage<T>(string name, Func<T> action)
    {
        Console.WriteLine($"[{DateTimeOffset.Now:O}] AICopilotAppFixture stage started: {name}");
        try
        {
            var result = action();
            Console.WriteLine($"[{DateTimeOffset.Now:O}] AICopilotAppFixture stage completed: {name}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:O}] AICopilotAppFixture stage failed: {name}. {ex}");
            throw;
        }
    }

    private sealed class ForwardedResourceLogCaptureProvider : ILoggerProvider
    {
        private const string ResourceCategoryMarker = ".Resources.";
        private readonly object _gate = new();
        private readonly HashSet<ResourceLogSubscription> _subscriptions = [];
        private bool _disposed;

        public ILogger CreateLogger(string categoryName)
        {
            return new ForwardedResourceLogger(this, categoryName);
        }

        public ResourceLogSubscription Subscribe(string resourceName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

            var subscription = new ResourceLogSubscription(this, resourceName);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _subscriptions.Add(subscription);
            }

            return subscription;
        }

        public void Dispose()
        {
            ResourceLogSubscription[] subscriptions;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                subscriptions = [.. _subscriptions];
                _subscriptions.Clear();
            }

            foreach (var subscription in subscriptions)
            {
                subscription.Complete();
            }
        }

        private void Record(string categoryName, string message)
        {
            lock (_gate)
            {
                foreach (var subscription in _subscriptions)
                {
                    if (categoryName.EndsWith(
                            ResourceCategoryMarker + subscription.ResourceName,
                            StringComparison.Ordinal))
                    {
                        subscription.TryWrite(message);
                    }
                }
            }
        }

        private void Unsubscribe(ResourceLogSubscription subscription)
        {
            lock (_gate)
            {
                _subscriptions.Remove(subscription);
            }

            subscription.Complete();
        }

        private sealed class ForwardedResourceLogger(
            ForwardedResourceLogCaptureProvider provider,
            string categoryName) : ILogger
        {
            IDisposable? ILogger.BeginScope<TState>(TState state)
            {
                return null;
            }

            bool ILogger.IsEnabled(LogLevel logLevel)
            {
                return logLevel != LogLevel.None;
            }

            void ILogger.Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.None)
                {
                    return;
                }

                var message = formatter(state, exception);
                if (exception is not null)
                {
                    message += Environment.NewLine + exception;
                }

                provider.Record(categoryName, message);
            }
        }

        public sealed class ResourceLogSubscription(
            ForwardedResourceLogCaptureProvider owner,
            string resourceName) : IDisposable
        {
            private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            private int _disposed;

            public string ResourceName { get; } = resourceName;

            public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken)
            {
                return _channel.Reader.ReadAllAsync(cancellationToken);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    owner.Unsubscribe(this);
                }
            }

            public void TryWrite(string message)
            {
                _channel.Writer.TryWrite(message);
            }

            public void Complete()
            {
                _channel.Writer.TryComplete();
            }
        }
    }
}
