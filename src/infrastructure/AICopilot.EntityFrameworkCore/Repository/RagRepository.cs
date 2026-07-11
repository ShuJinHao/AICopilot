using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class RagRepository<T>(
    RagDbContext dbContext,
    RepositoryPersistenceCommitter persistenceCommitter)
    : EfRepositoryBase<RagDbContext, T>(dbContext, persistenceCommitter)
    where T : class, IEntity, IAggregateRoot;
