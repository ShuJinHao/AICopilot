using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.EntityFrameworkCore.Repository;

public class EfRepository<T>(
    AiCopilotDbContext dbContext,
    RepositoryPersistenceCommitter persistenceCommitter)
    : EfRepositoryBase<AiCopilotDbContext, T>(dbContext, persistenceCommitter)
    where T : class, IEntity, IAggregateRoot;
