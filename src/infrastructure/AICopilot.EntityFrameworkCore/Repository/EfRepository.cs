using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.EntityFrameworkCore.Repository;

public class EfRepository<T>(
    AiCopilotDbContext dbContext,
    AuditTransactionCoordinator transactionCoordinator)
    : EfRepositoryBase<AiCopilotDbContext, T>(dbContext, transactionCoordinator)
    where T : class, IEntity, IAggregateRoot;
