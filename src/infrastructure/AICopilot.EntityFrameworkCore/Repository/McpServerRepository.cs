using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.EntityFrameworkCore.Transactions;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class McpServerRepository(
    McpServerDbContext dbContext,
    AuditTransactionCoordinator transactionCoordinator)
    : EfRepositoryBase<McpServerDbContext, McpServerInfo>(dbContext, transactionCoordinator);
