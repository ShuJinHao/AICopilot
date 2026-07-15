using System.Reflection;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Migrations;
using AICopilot.EntityFrameworkCore.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.PersistenceTests;

[Collection(IdentityPersistenceTestCollection.Name)]
public sealed class OutboxDispatcherPersistenceTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task ConcurrentDispatchAndPublishCancellation_ShouldSkipLockedRowWithoutIncrementingRetry()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_outbox_dispatch");
        await using (var migrationContext = new AiCopilotDbContext(
            PostgresPersistenceTestOptions.Create<AiCopilotDbContext>(
                database.ConnectionString,
                MigrationHistoryTables.AiCopilot)))
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using (var seed = CreateOutboxContext(database.ConnectionString))
        {
            seed.OutboxMessages.Add(OutboxMessage.FromIntegrationEvent(
                new DispatcherProbeEvent(Guid.NewGuid())));
            await seed.SaveChangesAsync();
        }

        var endpoint = DispatchProxy.Create<IPublishEndpoint, BlockingCancelledPublishEndpoint>();
        var endpointProxy = (BlockingCancelledPublishEndpoint)(object)endpoint;
        await using var serviceProvider = new ServiceCollection()
            .AddDbContext<OutboxDbContext>(options => options.UseNpgsql(database.ConnectionString))
            .AddSingleton(endpoint)
            .BuildServiceProvider();
        var dispatcher = new OutboxDispatcher(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxDispatcher>.Instance);

        Task? firstDispatch = null;
        Task? secondDispatch = null;
        try
        {
            firstDispatch = InvokeDispatchOnceAsync(dispatcher);
            await endpointProxy.FirstPublishStarted.WaitAsync(TimeSpan.FromSeconds(5));

            secondDispatch = InvokeDispatchOnceAsync(dispatcher);
            await secondDispatch.WaitAsync(TimeSpan.FromSeconds(5));
            endpointProxy.PublishAttempts.Should().Be(1, "the second transaction must skip the locked row");
        }
        finally
        {
            endpointProxy.ReleaseCancelledPublish();
            if (firstDispatch is not null)
            {
                await firstDispatch;
            }

            if (secondDispatch is not null)
            {
                await secondDispatch;
            }
        }

        await using var verification = CreateOutboxContext(database.ConnectionString);
        var message = await verification.OutboxMessages.SingleAsync();
        message.ProcessedOnUtc.Should().BeNull();
        message.DeadLetteredOnUtc.Should().BeNull();
        message.NextAttemptUtc.Should().BeNull();
        message.Error.Should().BeNull();
        message.RetryCount.Should().Be(0, "transport cancellation must not consume a retry");
    }

    private static Task InvokeDispatchOnceAsync(OutboxDispatcher dispatcher)
    {
        var method = typeof(OutboxDispatcher).GetMethod(
            "DispatchOnceAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("OutboxDispatcher.DispatchOnceAsync was not found.");
        return (Task)(method.Invoke(dispatcher, [CancellationToken.None])
            ?? throw new InvalidOperationException("Outbox dispatch did not return a task."));
    }

    private static OutboxDbContext CreateOutboxContext(string connectionString)
    {
        return new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>()
                .UseNpgsql(connectionString)
                .Options);
    }

    public sealed record DispatcherProbeEvent(Guid Id);

    public class BlockingCancelledPublishEndpoint : DispatchProxy
    {
        private readonly TaskCompletionSource firstPublishStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releasePublish = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int publishAttempts;

        public Task FirstPublishStarted => firstPublishStarted.Task;

        public int PublishAttempts => Volatile.Read(ref publishAttempts);

        public void ReleaseCancelledPublish()
        {
            releasePublish.TrySetResult();
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name.StartsWith("Publish", StringComparison.Ordinal) == true &&
                targetMethod.ReturnType == typeof(Task))
            {
                return PublishAndCancelAsync();
            }

            throw new NotSupportedException(
                $"Unexpected IPublishEndpoint call '{targetMethod?.Name ?? "<unknown>"}'.");
        }

        private async Task PublishAndCancelAsync()
        {
            Interlocked.Increment(ref publishAttempts);
            firstPublishStarted.TrySetResult();
            await releasePublish.Task;
            throw new OperationCanceledException("simulated transport cancellation");
        }
    }
}
