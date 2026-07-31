using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class AgentSessionStatePersistenceTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task PersistentKeyRing_ShouldRestoreStateAcrossIndependentProviders()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var ownerId = Guid.NewGuid();
        var sessionId = await SeedSessionAsync(database.ConnectionString, ownerId);
        var keyDirectoryPath = Path.Combine(
            Path.GetTempPath(),
            $"aicopilot-agent-session-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDirectoryPath);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                keyDirectoryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        const string serialized =
            """{"mode":"execute","restartMarker":"independent-provider"}""";
        try
        {
            await using (var firstServices = CreatePersistentDataProtectionServices(keyDirectoryPath))
            {
                var firstProvider = firstServices.GetRequiredService<IDataProtectionProvider>();
                await using var write = CreateContext(database.ConnectionString);
                var store = new ProtectedAgentSessionStateStore(write, firstProvider);
                store.AddNew(sessionId, ownerId, "tenant-restart", serialized);
                await write.SaveChangesAsync();
            }

            Directory.EnumerateFiles(keyDirectoryPath, "*.xml")
                .Should().NotBeEmpty("the first provider must persist a key ring before restart");

            await using (var secondServices = CreatePersistentDataProtectionServices(keyDirectoryPath))
            {
                var secondProvider = secondServices.GetRequiredService<IDataProtectionProvider>();
                await using var read = CreateContext(database.ConnectionString);
                var restored = await new ProtectedAgentSessionStateStore(read, secondProvider)
                    .LoadOwnedAsync(sessionId, ownerId, "tenant-restart");

                restored.SerializedSessionState.Should().Be(serialized);
            }
        }
        finally
        {
            Directory.Delete(keyDirectoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task ProtectedState_ShouldRoundTripWithoutPersistingPlaintext()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var ownerId = Guid.NewGuid();
        var sessionId = await SeedSessionAsync(database.ConnectionString, ownerId);
        var provider = new EphemeralDataProtectionProvider();
        const string serialized = """{"mode":"plan","secretMarker":"must-not-be-plaintext"}""";

        await using (var context = CreateContext(database.ConnectionString))
        {
            var store = new ProtectedAgentSessionStateStore(context, provider);
            store.AddNew(sessionId, ownerId, "tenant-a", serialized);
            await context.SaveChangesAsync();
        }

        await using (var inspect = CreateContext(database.ConnectionString))
        {
            var persisted = await inspect.AgentSessionStates.AsNoTracking().SingleAsync();
            persisted.ProtectedState.Should().NotContain("must-not-be-plaintext");
            persisted.AgentSchemaVersion.Should().Be(1);
            persisted.Status.Should().Be(AgentSessionRuntimeStatus.Ready);
            persisted.Version.Should().Be(1);
        }

        await using (var read = CreateContext(database.ConnectionString))
        {
            var loaded = await new ProtectedAgentSessionStateStore(read, provider)
                .LoadOwnedAsync(sessionId, ownerId, "tenant-a");
            loaded.SerializedSessionState.Should().Be(serialized);
            loaded.ExpiresAtUtc.Should().BeAfter(loaded.UpdatedAtUtc.AddDays(29));
        }
    }

    [Fact]
    public async Task PersistModeChange_ShouldRejectConcurrentVersion()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var ownerId = Guid.NewGuid();
        var sessionId = await SeedSessionAsync(database.ConnectionString, ownerId);
        var provider = new EphemeralDataProtectionProvider();
        await SeedAgentStateAsync(database.ConnectionString, provider, sessionId, ownerId);

        await using var firstContext = CreateContext(database.ConnectionString);
        await using var secondContext = CreateContext(database.ConnectionString);
        var firstStore = new ProtectedAgentSessionStateStore(firstContext, provider);
        var secondStore = new ProtectedAgentSessionStateStore(secondContext, provider);
        _ = await firstStore.LoadOwnedAsync(sessionId, ownerId, null);
        _ = await secondStore.LoadOwnedAsync(sessionId, ownerId, null);

        var first = await firstStore.PersistModeChangeAsync(
            sessionId,
            ownerId,
            null,
            expectedVersion: 1,
            """{"mode":"execute"}""");
        var action = () => secondStore.PersistModeChangeAsync(
            sessionId,
            ownerId,
            null,
            expectedVersion: 1,
            """{"mode":"plan"}""");

        first.Version.Should().Be(2);
        var failure = await action.Should().ThrowAsync<AgentSessionStateException>();
        failure.Which.Failure.Should().Be(AgentSessionStateFailure.VersionConflict);
    }

    [Fact]
    public async Task BeginTurn_ShouldConvertLeftoverRunningToInterruptedWithoutReplay()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var ownerId = Guid.NewGuid();
        var sessionId = await SeedSessionAsync(database.ConnectionString, ownerId);
        var provider = new EphemeralDataProtectionProvider();
        await SeedAgentStateAsync(database.ConnectionString, provider, sessionId, ownerId);
        var firstTurn = Guid.NewGuid();

        await using (var first = CreateContext(database.ConnectionString))
        {
            _ = await new ProtectedAgentSessionStateStore(first, provider).BeginTurnAsync(
                sessionId,
                ownerId,
                null,
                firstTurn,
                approvalContinuation: false);
        }

        await using (var recovery = CreateContext(database.ConnectionString))
        {
            var action = () => new ProtectedAgentSessionStateStore(recovery, provider)
                .BeginTurnAsync(
                    sessionId,
                    ownerId,
                    null,
                    Guid.NewGuid(),
                    approvalContinuation: false);
            var failure = await action.Should().ThrowAsync<AgentSessionStateException>();
            failure.Which.Failure.Should().Be(AgentSessionStateFailure.Interrupted);
        }

        await using var inspect = CreateContext(database.ConnectionString);
        var state = await inspect.AgentSessionStates.AsNoTracking().SingleAsync();
        state.Status.Should().Be(AgentSessionRuntimeStatus.Interrupted);
        state.ActiveTurnId.Should().BeNull();
        state.ProtectedApprovalBindings.Should().BeNull();
    }

    [Fact]
    public async Task CompleteTurn_ShouldRejectTamperedApprovalArgumentsDigest()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var ownerId = Guid.NewGuid();
        var sessionId = await SeedSessionAsync(database.ConnectionString, ownerId);
        var provider = new EphemeralDataProtectionProvider();
        await SeedAgentStateAsync(database.ConnectionString, provider, sessionId, ownerId);
        var turnId = Guid.NewGuid();

        await using (var context = CreateContext(database.ConnectionString))
        {
            var store = new ProtectedAgentSessionStateStore(context, provider);
            _ = await store.BeginTurnAsync(
                sessionId,
                ownerId,
                null,
                turnId,
                approvalContinuation: false);
            var binding = new AgentApprovalBinding(
                sessionId,
                ownerId,
                TenantId: null,
                RequestId: "request-1",
                ToolCallId: "call-1",
                ToolName: "BusinessQuery",
                ToolKind: AICopilot.SharedKernel.Ai.AiToolCallKind.Function,
                ServerName: null,
                TargetType: AICopilot.SharedKernel.Ai.AiToolTargetType.Plugin,
                TargetName: "main-chat",
                CanonicalToolName: "BusinessQuery",
                Arguments: new Dictionary<string, object?>
                {
                    ["question"] = "tampered after approval binding"
                },
                ToolSchemaVersion: 1,
                CanonicalArgumentsDigest: new string('0', 64));

            var action = () => store.CompleteTurnAsync(
                sessionId,
                ownerId,
                null,
                turnId,
                """{"mode":"plan"}""",
                [binding]);

            var failure = await action.Should().ThrowAsync<AgentSessionStateException>();
            failure.Which.Failure.Should().Be(AgentSessionStateFailure.Corrupt);
        }

        await using var inspect = CreateContext(database.ConnectionString);
        var state = await inspect.AgentSessionStates.AsNoTracking().SingleAsync();
        state.Status.Should().Be(AgentSessionRuntimeStatus.Running);
        state.ActiveTurnId.Should().Be(turnId);
        state.ProtectedApprovalBindings.Should().BeNull();
    }

    [Fact]
    public async Task StateStore_ShouldFailClosedForOversizeExpiredAndForeignOwner()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var ownerId = Guid.NewGuid();
        var sessionId = await SeedSessionAsync(database.ConnectionString, ownerId);
        var provider = new EphemeralDataProtectionProvider();

        await using (var oversizeContext = CreateContext(database.ConnectionString))
        {
            var oversizeStore = new ProtectedAgentSessionStateStore(oversizeContext, provider);
            var oversize = "\"" + new string('x', 2 * 1024 * 1024) + "\"";
            var oversizeAction = () => Task.Run(() =>
                oversizeStore.AddNew(sessionId, ownerId, null, oversize));
            var failure = await oversizeAction.Should().ThrowAsync<AgentSessionStateException>();
            failure.Which.Failure.Should().Be(AgentSessionStateFailure.Oversize);
        }

        await SeedAgentStateAsync(database.ConnectionString, provider, sessionId, ownerId);
        await using (var foreignContext = CreateContext(database.ConnectionString))
        {
            var foreignAction = () => new ProtectedAgentSessionStateStore(foreignContext, provider)
                .LoadOwnedAsync(sessionId, Guid.NewGuid(), null);
            var failure = await foreignAction.Should().ThrowAsync<AgentSessionStateException>();
            failure.Which.Failure.Should().Be(AgentSessionStateFailure.OwnershipMismatch);
        }

        await using (var expire = CreateContext(database.ConnectionString))
        {
            await expire.AgentSessionStates.ExecuteUpdateAsync(setters => setters
                .SetProperty(state => state.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await using (var expiredContext = CreateContext(database.ConnectionString))
        {
            var expiredAction = () => new ProtectedAgentSessionStateStore(expiredContext, provider)
                .LoadOwnedAsync(sessionId, ownerId, null);
            var failure = await expiredAction.Should().ThrowAsync<AgentSessionStateException>();
            failure.Which.Failure.Should().Be(AgentSessionStateFailure.Expired);
        }
    }

    private async Task<PostgresScratchDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_agent_session_state");
        await using var context = CreateContext(database.ConnectionString);
        await context.Database.MigrateAsync();
        return database;
    }

    private static AiGatewayDbContext CreateContext(string connectionString)
    {
        var options = PostgresPersistenceTestOptions.Create<AiGatewayDbContext>(
            connectionString,
            MigrationHistoryTables.AiGateway);
        return new AiGatewayDbContext(options);
    }

    private static async Task<Guid> SeedSessionAsync(
        string connectionString,
        Guid ownerId)
    {
        await using var context = CreateContext(connectionString);
        var session = new Session(ownerId, ConversationTemplateId.New());
        context.Sessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id.Value;
    }

    private static async Task SeedAgentStateAsync(
        string connectionString,
        IDataProtectionProvider provider,
        Guid sessionId,
        Guid ownerId)
    {
        await using var context = CreateContext(connectionString);
        var store = new ProtectedAgentSessionStateStore(context, provider);
        store.AddNew(sessionId, ownerId, null, """{"mode":"plan"}""");
        await context.SaveChangesAsync();
    }

    private static ServiceProvider CreatePersistentDataProtectionServices(
        string keyDirectoryPath)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AgentSessionStateDataProtection.SectionName}:{AgentSessionStateDataProtection.KeyPathConfigurationName}"] =
                    keyDirectoryPath
            })
            .Build();
        AgentSessionStateDataProtection.Configure(services, configuration);
        return services.BuildServiceProvider();
    }
}
