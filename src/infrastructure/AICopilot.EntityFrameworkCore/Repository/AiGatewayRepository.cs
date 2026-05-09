using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class AiGatewayRepository<T>(
    AiGatewayDbContext dbContext,
    AuditTransactionCoordinator transactionCoordinator)
    : EfRepositoryBase<AiGatewayDbContext, T>(dbContext, transactionCoordinator)
    where T : class, IEntity, IAggregateRoot;
