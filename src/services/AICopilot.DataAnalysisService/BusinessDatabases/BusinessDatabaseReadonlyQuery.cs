using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

[AuthorizeRequirement("DataSource.QueryGovernedSql")]
public sealed record ExecuteBusinessDatabaseReadonlyQueryCommand(
    Guid DataSourceId,
    string Sql,
    int? Limit = null) : ICommand<Result<BusinessQueryResultDto>>;

public sealed class ExecuteBusinessDatabaseReadonlyQueryCommandHandler(
    BusinessReadonlyQueryExecutor executor)
    : ICommandHandler<ExecuteBusinessDatabaseReadonlyQueryCommand, Result<BusinessQueryResultDto>>
{
    public Task<Result<BusinessQueryResultDto>> Handle(
        ExecuteBusinessDatabaseReadonlyQueryCommand request,
        CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            request.DataSourceId,
            request.Sql,
            request.Limit,
            requireSimulationBusiness: false,
            safetySchema: null,
            auditAction: "DataSource.QueryGovernedSql",
            cancellationToken,
            selectionMode: DataSourceSelectionMode.GovernedSql);
    }
}

public sealed record BusinessQuerySafetySchema(
    IReadOnlySet<string> AllowedTables,
    IReadOnlySet<string> BlockedFieldFragments,
    IReadOnlySet<string>? AllowedColumnFragments = null,
    IReadOnlySet<string>? SensitiveColumnFragments = null,
    IReadOnlyDictionary<string, IReadOnlySet<string>>? AllowedColumns = null);
