using System.Net.Http.Headers;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Logging;

namespace AICopilot.BackendTests;

public class AICopilotAppFixture : IAsyncLifetime, IAsyncDisposable
{
    private const string TestPostgresPassword = "TestPg123!";
    private const string BootstrapUserName = "admin";
    private const string BootstrapPassword = "Password123!";
    private static readonly TimeSpan StageTimeout = TimeSpan.FromMinutes(2);

    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);
    private readonly FakeAiProviderHost _fakeAiProvider = new();

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
                    logging.ClearProviders();
                    logging.AddConsole();
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

    private void ConfigureEnvironment()
    {
        SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        SetEnvironmentVariable("Parameters__pg-password", TestPostgresPassword);
        SetEnvironmentVariable("JwtSettings__SecretKey", "test-aicopilot-jwt-secret-key-at-least-32-bytes");
        SetEnvironmentVariable("BootstrapAdmin__UserName", BootstrapUserName);
        SetEnvironmentVariable("BootstrapAdmin__Password", BootstrapPassword);
        SetEnvironmentVariable("AICopilotSecurity__ApiKeyEncryptionKey", "test-aicopilot-api-key-encryption-key");
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
        SetEnvironmentVariable("RateLimiting__Chat__TokenLimit", "1000");
        SetEnvironmentVariable("RateLimiting__Chat__TokensPerPeriod", "1000");
    }

    private void SetEnvironmentVariable(string name, string value)
    {
        if (!_originalEnvironment.ContainsKey(name))
        {
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
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
}

public sealed class CoreAICopilotAppFixture : AICopilotAppFixture
{
    protected override bool EnableRagWorker => false;

    protected override bool EnableDataWorker => false;
}
