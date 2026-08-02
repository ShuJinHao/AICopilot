using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Repository;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class MessageHistoryPaginationPersistenceTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task Handler_ShouldPagePersistedHistoryByMessageSequence()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_message_history_sequence");
        var ownerId = Guid.NewGuid();
        Guid sessionId;
        await using (var write = CreateContext(database.ConnectionString))
        {
            await write.Database.MigrateAsync();
            var session = new Session(ownerId, ConversationTemplateId.New());
            for (var index = 1; index <= 6; index++)
            {
                session.AddMessage(
                    $"message-{index}",
                    index % 2 == 0 ? MessageType.Assistant : MessageType.User);
            }

            write.Sessions.Add(session);
            await write.SaveChangesAsync();
            sessionId = session.Id.Value;
        }

        await using var read = CreateContext(database.ConnectionString);
        var handler = new GetListChatMessageHistoryQueryHandler(
            new SessionReadRepository(read),
            new FixedCurrentUser(ownerId));

        var newest = await handler.Handle(
            new GetListChatMessageHistoryQuery(sessionId, Count: 2),
            CancellationToken.None);
        var older = await handler.Handle(
            new GetListChatMessageHistoryQuery(sessionId, Count: 2, BeforeSequence: 5),
            CancellationToken.None);
        var newer = await handler.Handle(
            new GetListChatMessageHistoryQuery(sessionId, Count: 2, AfterSequence: 4),
            CancellationToken.None);

        newest.IsSuccess.Should().BeTrue();
        newest.Value!.Items.Select(message => message.Sequence).Should().Equal(5, 6);
        newest.Value.BeforeSequence.Should().Be(5);
        newest.Value.AfterSequence.Should().Be(6);
        newest.Value.HasMoreBefore.Should().BeTrue();
        newest.Value.HasMoreAfter.Should().BeFalse();

        older.IsSuccess.Should().BeTrue();
        older.Value!.Items.Select(message => message.Sequence).Should().Equal(3, 4);
        older.Value.HasMoreBefore.Should().BeTrue();
        older.Value.HasMoreAfter.Should().BeTrue();

        newer.IsSuccess.Should().BeTrue();
        newer.Value!.Items.Select(message => message.Sequence).Should().Equal(5, 6);
        newer.Value.HasMore.Should().BeFalse();
    }

    private static AiGatewayDbContext CreateContext(string connectionString)
    {
        var options = PostgresPersistenceTestOptions.Create<AiGatewayDbContext>(
            connectionString,
            MigrationHistoryTables.AiGateway);
        return new AiGatewayDbContext(options);
    }

    private sealed class SessionReadRepository(AiGatewayDbContext dbContext)
        : EfReadRepositoryBase<AiGatewayDbContext, Session>(dbContext);

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? Id => userId;
        public string? UserName => "history-owner";
        public string? Role => "User";
        public string? IdentityProvider => "test";
        public string? CloudTenantId => null;
        public string? CloudEmployeeNo => null;
        public string? CloudDepartmentId => null;
        public string? CloudDepartmentName => null;
        public string? CloudStatusVersion => null;
        public bool IsAuthenticated => true;
    }
}
