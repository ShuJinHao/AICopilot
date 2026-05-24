using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.EntityFrameworkCore.Transactions;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class BusinessDatabaseRepository(
    DataAnalysisDbContext dbContext,
    AuditTransactionCoordinator transactionCoordinator)
    : EfRepositoryBase<DataAnalysisDbContext, BusinessDatabase>(dbContext, transactionCoordinator);

public sealed class DataSourcePermissionGrantRepository(
    DataAnalysisDbContext dbContext,
    AuditTransactionCoordinator transactionCoordinator)
    : EfRepositoryBase<DataAnalysisDbContext, DataSourcePermissionGrant>(dbContext, transactionCoordinator);
