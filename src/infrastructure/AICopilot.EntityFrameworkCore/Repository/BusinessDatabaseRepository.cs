using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.EntityFrameworkCore.Transactions;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class BusinessDatabaseRepository(
    DataAnalysisDbContext dbContext,
    RepositoryPersistenceCommitter persistenceCommitter)
    : EfRepositoryBase<DataAnalysisDbContext, BusinessDatabase>(dbContext, persistenceCommitter);

public sealed class DataSourcePermissionGrantRepository(
    DataAnalysisDbContext dbContext,
    RepositoryPersistenceCommitter persistenceCommitter)
    : EfRepositoryBase<DataAnalysisDbContext, DataSourcePermissionGrant>(dbContext, persistenceCommitter);
