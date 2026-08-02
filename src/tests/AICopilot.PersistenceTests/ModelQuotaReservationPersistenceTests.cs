using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.ModelQuota;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class ModelQuotaReservationPersistenceTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task Store_ShouldReserveDeduplicateSettleAndPersistActualUsage()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_model_quota");
        await MigrateStoresAsync(database.ConnectionString);

        var gatewayOptions = PostgresPersistenceTestOptions.Create<AiGatewayDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.AiGateway);
        var store = new PostgresModelQuotaReservationStore(
            new AiGatewayTransactionRunner(
                gatewayOptions,
                new PersistenceCommitEngine(
                    PostgresPersistenceTestOptions.CreateMarker(database.ConnectionString)),
                new PersistenceCommitScope()));
        var request = CreateRequest();

        var reserved = await store.TryReserveAsync(request);
        reserved.Result.Should().Be(ModelQuotaReservationResult.Granted);
        reserved.Lease.Should().NotBeNull();

        var duplicate = await store.TryReserveAsync(request);
        duplicate.Result.Should().Be(ModelQuotaReservationResult.Duplicate);
        duplicate.Lease.Should().Be(reserved.Lease);

        var settlement = new ModelQuotaSettlement(
            reserved.Lease!,
            ActualInputTokens: 73,
            ActualOutputTokens: 19,
            WasDispatched: true,
            OutcomeKnown: true,
            FailureCode: null,
            SettledAtUtc: request.RequestedAtUtc.AddSeconds(2));
        (await store.SettleAsync(settlement)).Should().Be(ModelQuotaReservationResult.Granted);
        (await store.SettleAsync(settlement)).Should().Be(ModelQuotaReservationResult.Duplicate);

        await using var verify = new AiGatewayDbContext(gatewayOptions);
        var persisted = await verify.ModelQuotaReservations.AsNoTracking().SingleAsync();
        persisted.Status.Should().Be(ModelQuotaReservationStatus.Settled);
        persisted.ActualInputTokens.Should().Be(73);
        persisted.ActualOutputTokens.Should().Be(19);
        persisted.CorrelationHash.Should().Be(request.CorrelationHash);
    }

    private static ModelQuotaReservationRequest CreateRequest()
    {
        return new ModelQuotaReservationRequest(
            TenantKeyHash: $"tenant-{Guid.NewGuid():N}",
            UserId: Guid.NewGuid(),
            RoleKeyHash: "role-admin",
            ModelId: LanguageModelId.New(),
            EndpointId: "endpoint-primary",
            PoolName: "chat",
            EstimatedInputTokens: 100,
            EstimatedOutputTokens: 50,
            ConcurrencySlots: 1,
            EndpointRpmLimit: 100,
            EndpointTpmLimit: 100_000,
            EndpointConcurrencyLimit: 10,
            ModelRpmLimit: 100,
            ModelTpmLimit: 100_000,
            ModelConcurrencyLimit: 10,
            UserRpmLimit: 100,
            UserTpmLimit: 100_000,
            UserConcurrencyLimit: 10,
            RoleRpmLimit: 100,
            RoleTpmLimit: 100_000,
            RoleConcurrencyLimit: 10,
            TenantRpmLimit: 100,
            TenantTpmLimit: 100_000,
            TenantConcurrencyLimit: 10,
            CorrelationHash: $"correlation-{Guid.NewGuid():N}",
            RequestedAtUtc: DateTimeOffset.UtcNow,
            ReservationLease: TimeSpan.FromMinutes(1));
    }

    private static async Task MigrateStoresAsync(string connectionString)
    {
        await using (var root = new AiCopilotDbContext(
                         PostgresPersistenceTestOptions.Create<AiCopilotDbContext>(
                             connectionString,
                             MigrationHistoryTables.AiCopilot)))
        {
            await root.Database.MigrateAsync();
        }

        await using var gateway = new AiGatewayDbContext(
            PostgresPersistenceTestOptions.Create<AiGatewayDbContext>(
                connectionString,
                MigrationHistoryTables.AiGateway));
        await gateway.Database.MigrateAsync();
    }
}
