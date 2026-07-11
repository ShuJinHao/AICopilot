using AICopilot.Core.Rag.Ids;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Persistence;

public sealed class PostgresDocumentIdAllocator(
    DbContextOptions<RagDbContext> options) : IDocumentIdAllocator
{
    public async Task<DocumentId> AllocateAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = new RagDbContext(options);
        var strategy = dbContext.Database.CreateExecutionStrategy();

        var value = await strategy.ExecuteAsync(
            async token =>
            {
                await dbContext.Database.OpenConnectionAsync(token);
                try
                {
                    await using var command = dbContext.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "SELECT nextval('rag.documents_id_seq'::regclass)";
                    var scalar = await command.ExecuteScalarAsync(token)
                                 ?? throw new InvalidOperationException(
                                     "RAG document id sequence returned no value.");
                    return Convert.ToInt32(scalar);
                }
                finally
                {
                    await dbContext.Database.CloseConnectionAsync();
                }
            },
            cancellationToken);

        return new DocumentId(value);
    }
}
