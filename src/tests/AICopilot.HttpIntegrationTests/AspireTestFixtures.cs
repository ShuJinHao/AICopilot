extern alias HttpApiHost;

using AICopilot.AspireIntegrationTestKit;
using AICopilot.AgentWorkflowTestKit;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.EntityFrameworkCore;
using AICopilot.IdentityService.Authorization;
using AICopilot.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace AICopilot.HttpIntegrationTests;

public class AICopilotAppFixture : AICopilotAppEnvironment, IAsyncLifetime
{
}

public sealed class CoreAICopilotAppFixture : AICopilotAppFixture
{
    private readonly SemaphoreSlim downstreamHarnessGate = new(1, 1);
    private DownstreamRuntimeHarnessHttpApiFactory? downstreamHarnessFactory;
    private HttpClient? downstreamHarnessClient;

    protected override bool EnableRagWorker => false;

    protected override bool EnableDataWorker => true;

    public async Task<HttpClient> GetDownstreamRuntimeHarnessClientAsync()
    {
        if (downstreamHarnessClient is not null)
        {
            return downstreamHarnessClient;
        }

        await downstreamHarnessGate.WaitAsync();
        try
        {
            if (downstreamHarnessClient is not null)
            {
                return downstreamHarnessClient;
            }

            var settings = new DownstreamRuntimeHarnessSettings(
                await GetConnectionStringAsync("ai-copilot"),
                await GetConnectionStringAsync("eventbus"),
                await GetConnectionStringAsync("qdrant"),
                await GetConnectionStringAsync("final-agent-context-redis"),
                ApiKeyEncryptionKey,
                JwtSecretKey,
                BootstrapAdminUserName,
                BootstrapAdminPassword,
                ArtifactWorkspaceRootPath);
            downstreamHarnessFactory = new DownstreamRuntimeHarnessHttpApiFactory(settings);
            downstreamHarnessClient = downstreamHarnessFactory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false
                });

            var resolvedValidator = downstreamHarnessFactory.Services
                .GetRequiredService<IAgentPlanIntegrityValidator>();
            if (!ReferenceEquals(resolvedValidator, downstreamHarnessFactory.HarnessValidator))
            {
                throw new InvalidOperationException(
                    "The downstream HTTP harness did not resolve its isolated Plan validator.");
            }

            await downstreamHarnessFactory.AssertSharedIdentityReadyAsync();

            return downstreamHarnessClient;
        }
        finally
        {
            downstreamHarnessGate.Release();
        }
    }

    public override async Task DisposeAsync()
    {
        downstreamHarnessClient?.Dispose();
        downstreamHarnessFactory?.Dispose();
        downstreamHarnessGate.Dispose();
        await base.DisposeAsync();
    }
}

internal sealed record DownstreamRuntimeHarnessSettings(
    string AiCopilotConnectionString,
    string EventBusConnectionString,
    string QdrantConnectionString,
    string FinalAgentContextRedisConnectionString,
    string ApiKeyEncryptionKey,
    string JwtSecretKey,
    string BootstrapAdminUserName,
    string BootstrapAdminPassword,
    string ArtifactWorkspaceRootPath);

/// <summary>
/// Runs the production HttpApi Program and middleware against the already-healthy
/// Aspire dependencies. Only the Plan integrity singleton is replaced so five
/// downstream-finalization tests can exercise the post-P2 state machine while
/// P0 production remains fail-closed.
/// </summary>
internal sealed class DownstreamRuntimeHarnessHttpApiFactory(
    DownstreamRuntimeHarnessSettings settings) : WebApplicationFactory<HttpApiHost::Program>
{
    public IAgentPlanIntegrityValidator HarnessValidator { get; } =
        AgentPlanV2TestData.CreateDownstreamRuntimeHarnessIntegrityValidator();

    public async Task AssertSharedIdentityReadyAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
        var actualConnection = dbContext.Database.GetDbConnection() as NpgsqlConnection
            ?? throw new InvalidOperationException("Downstream identity preflight failed at connection type.");
        var expectedConnection = new NpgsqlConnectionStringBuilder(settings.AiCopilotConnectionString);
        var finalConfigurationConnectionString = scope.ServiceProvider
            .GetRequiredService<IConfiguration>()
            .GetConnectionString("ai-copilot");
        if (string.IsNullOrWhiteSpace(finalConfigurationConnectionString))
        {
            throw new InvalidOperationException(
                "Downstream identity preflight failed at final configuration connection visibility.");
        }

        var finalConfigurationConnection =
            new NpgsqlConnectionStringBuilder(finalConfigurationConnectionString);
        AssertConnectionIdentity(
            finalConfigurationConnection.Host,
            finalConfigurationConnection.Port,
            finalConfigurationConnection.Database,
            finalConfigurationConnection.Username,
            expectedConnection,
            "final configuration");

        if (!await dbContext.Database.CanConnectAsync())
        {
            throw new InvalidOperationException("Downstream identity preflight failed at database connectivity.");
        }

        var openedForPreflight = actualConnection.State != System.Data.ConnectionState.Open;
        if (openedForPreflight)
        {
            await actualConnection.OpenAsync();
        }

        try
        {
            AssertConnectionIdentity(
                actualConnection.Host,
                actualConnection.Port,
                actualConnection.Database,
                actualConnection.UserName,
                expectedConnection,
                "resolved IdentityStore");
        }
        finally
        {
            if (openedForPreflight)
            {
                await actualConnection.CloseAsync();
            }
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(settings.BootstrapAdminUserName)
            ?? throw new InvalidOperationException("Downstream identity preflight failed at admin seed visibility.");
        if (!await userManager.CheckPasswordAsync(user, settings.BootstrapAdminPassword))
        {
            throw new InvalidOperationException("Downstream identity preflight failed at admin credential verification.");
        }

        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            throw new InvalidOperationException("Downstream identity preflight failed at admin security stamp.");
        }

        var roles = await userManager.GetRolesAsync(user);
        var claims = await userManager.GetClaimsAsync(user);
        var token = await scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>()
            .GenerateTokenAsync(new JwtTokenUser(
                user.Id,
                user.UserName!,
                user.SecurityStamp,
                roles.ToArray(),
                claims.ToArray()));
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Downstream identity preflight failed at JWT generation.");
        }
    }

    private static void AssertConnectionIdentity(
        string? actualHost,
        int actualPort,
        string? actualDatabase,
        string? actualUserName,
        NpgsqlConnectionStringBuilder expected,
        string source)
    {
        var actualHostIsLoopback = IsLoopbackHost(actualHost);
        var expectedHostIsLoopback = IsLoopbackHost(expected.Host);
        if (!string.Equals(actualHost, expected.Host, StringComparison.OrdinalIgnoreCase) &&
            !(actualHostIsLoopback && expectedHostIsLoopback))
        {
            var hostFailure = string.IsNullOrWhiteSpace(actualHost)
                ? "actual host missing"
                : string.IsNullOrWhiteSpace(expected.Host)
                    ? "expected host missing"
                    : actualHostIsLoopback != expectedHostIsLoopback
                        ? "loopback classification mismatch"
                        : "non-loopback host mismatch";
            throw new InvalidOperationException(
                $"Downstream identity preflight failed at {source} database host identity: {hostFailure}.");
        }

        if (actualPort != expected.Port)
        {
            throw new InvalidOperationException(
                $"Downstream identity preflight failed at {source} database port identity.");
        }

        if (!string.Equals(actualDatabase, expected.Database, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Downstream identity preflight failed at {source} database name identity.");
        }

        if (!string.Equals(actualUserName, expected.Username, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Downstream identity preflight failed at {source} database user identity.");
        }
    }

    private static bool IsLoopbackHost(string? host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               (System.Net.IPAddress.TryParse(host, out var address) &&
                System.Net.IPAddress.IsLoopback(address));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ai-copilot", settings.AiCopilotConnectionString);
        builder.UseSetting("ConnectionStrings:eventbus", settings.EventBusConnectionString);
        builder.UseSetting("ConnectionStrings:qdrant", settings.QdrantConnectionString);
        builder.UseSetting(
            "ConnectionStrings:final-agent-context-redis",
            settings.FinalAgentContextRedisConnectionString);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ai-copilot"] = settings.AiCopilotConnectionString,
                ["ConnectionStrings:eventbus"] = settings.EventBusConnectionString,
                ["ConnectionStrings:qdrant"] = settings.QdrantConnectionString,
                ["ConnectionStrings:final-agent-context-redis"] = settings.FinalAgentContextRedisConnectionString,
                ["AICopilotSecurity:ApiKeyEncryptionKey"] = settings.ApiKeyEncryptionKey,
                ["JwtSettings:SecretKey"] = settings.JwtSecretKey,
                ["BootstrapAdmin:UserName"] = settings.BootstrapAdminUserName,
                ["BootstrapAdmin:Password"] = settings.BootstrapAdminPassword,
                ["CloudOidc:BootstrapAdminUserName"] = settings.BootstrapAdminUserName,
                ["ArtifactWorkspace:RootPath"] = settings.ArtifactWorkspaceRootPath,
                ["AiGateway:Deployment:Mode"] = "SingleInstance",
                ["AiGateway:FinalAgentContextStore:Provider"] = "Redis",
                ["Mcp:Runtime:Enabled"] = "false",
                ["RateLimiting:Default:TokenLimit"] = "1000",
                ["RateLimiting:Default:TokensPerPeriod"] = "1000",
                ["RateLimiting:Login:TokenLimit"] = "1000",
                ["RateLimiting:Login:TokensPerPeriod"] = "1000",
                ["RateLimiting:IdentityManagement:TokenLimit"] = "1000",
                ["RateLimiting:IdentityManagement:TokensPerPeriod"] = "1000"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            var productionDescriptors = services
                .Where(descriptor => descriptor.ServiceType == typeof(IAgentPlanIntegrityValidator))
                .ToArray();
            if (productionDescriptors.Length != 1 ||
                productionDescriptors[0].Lifetime != ServiceLifetime.Singleton)
            {
                throw new InvalidOperationException(
                    "The production HttpApi must expose exactly one Singleton Plan integrity validator before test isolation.");
            }

            services.RemoveAll<IAgentPlanIntegrityValidator>();
            services.AddSingleton(HarnessValidator);
        });
    }
}
