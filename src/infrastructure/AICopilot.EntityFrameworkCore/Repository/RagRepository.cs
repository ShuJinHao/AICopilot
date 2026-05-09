using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class RagRepository<T>(
    RagDbContext dbContext,
    AuditTransactionCoordinator transactionCoordinator)
    : EfRepositoryBase<RagDbContext, T>(dbContext, transactionCoordinator)
    where T : class, IEntity, IAggregateRoot;
