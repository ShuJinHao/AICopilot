using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class AiGatewayRepository<T>(
    AiGatewayDbContext dbContext,
    RepositoryPersistenceCommitter persistenceCommitter)
    : EfRepositoryBase<AiGatewayDbContext, T>(dbContext, persistenceCommitter)
    where T : class, IEntity, IAggregateRoot;
