using System.Collections.Concurrent;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

[AuthorizeRequirement("DataSource.Query")]
public sealed record GenerateBusinessTextToSqlCommand(
    Guid DataSourceId,
    string Question,
    IReadOnlyCollection<string>? BusinessDomains = null,
    int? RequestedLimit = null,
    bool PreviewOnly = true) : ICommand<Result<BusinessTextToSqlDraftDto>>;

[AuthorizeRequirement("DataSource.Query")]
public sealed record ExecuteBusinessTextToSqlCommand(
    Guid? DraftId = null,
    Guid? DataSourceId = null,
    string? SqlPreview = null,
    int? RequestedLimit = null) : ICommand<Result<BusinessQueryResultDto>>;

public sealed class GenerateBusinessTextToSqlCommandHandler(
    IBusinessTextToSqlRuntime runtime)
    : ICommandHandler<GenerateBusinessTextToSqlCommand, Result<BusinessTextToSqlDraftDto>>
{
    public Task<Result<BusinessTextToSqlDraftDto>> Handle(
        GenerateBusinessTextToSqlCommand request,
        CancellationToken cancellationToken)
    {
        return runtime.GenerateDraftAsync(
            new BusinessTextToSqlDraftRequest(
                request.DataSourceId,
                request.Question,
                request.BusinessDomains,
                request.RequestedLimit,
                request.PreviewOnly),
            cancellationToken);
    }
}

public sealed class ExecuteBusinessTextToSqlCommandHandler(
    IBusinessTextToSqlRuntime runtime)
    : ICommandHandler<ExecuteBusinessTextToSqlCommand, Result<BusinessQueryResultDto>>
{
    public Task<Result<BusinessQueryResultDto>> Handle(
        ExecuteBusinessTextToSqlCommand request,
        CancellationToken cancellationToken)
    {
        return runtime.ExecuteAsync(
            new BusinessTextToSqlExecuteRequest(
                request.DraftId,
                request.DataSourceId,
                request.SqlPreview,
                request.RequestedLimit),
            cancellationToken);
    }
}

internal sealed class BusinessTextToSqlRuntime(
    IReadRepository<BusinessDatabase> repository,
    BusinessReadonlyQueryExecutor queryExecutor,
    BusinessTextToSqlDraftStore draftStore,
    IAuditLogWriter auditLogWriter)
    : IBusinessTextToSqlRuntime
{
    public async Task<Result<BusinessTextToSqlDraftDto>> GenerateDraftAsync(
        BusinessTextToSqlDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return Result.Invalid("Question cannot be empty.");
        }

        var database = await repository.FirstOrDefaultAsync(
            new BusinessDatabaseByIdSpec(new BusinessDatabaseId(request.DataSourceId)),
            cancellationToken);
        if (database is null)
        {
            return Result.NotFound();
        }

        var validation = ValidateSimulationBusinessDatabase(database, request.BusinessDomains);
        if (validation is not null)
        {
            await WriteDraftAuditAsync(database, request.Question, null, AuditResults.Rejected, validation, cancellationToken);
            return Result.Invalid(validation);
        }

        var limit = ResolveLimit(database, request.RequestedLimit);
        var generated = BusinessTextToSqlRuleBasedGenerator.Generate(request.Question, limit);
        var safetyError = BusinessReadonlyQuerySafetyPolicy.Validate(
            generated.Sql,
            SimulationBusinessQuerySchema.SafetySchema);
        if (safetyError is not null)
        {
            await WriteDraftAuditAsync(database, request.Question, generated.Sql, AuditResults.Rejected, safetyError, cancellationToken);
            return Result.Invalid(safetyError);
        }

        var sourceMode = BusinessDatabaseContractMapper.ToContractExternalSystemType(database.ExternalSystemType);
        var now = DateTimeOffset.UtcNow;
        var draft = new BusinessTextToSqlDraftDto(
            Guid.NewGuid(),
            database.Id,
            database.Name,
            sourceMode,
            IsSimulation: true,
            BusinessQueryResultMapper.SimulationSourceLabel,
            BusinessQueryResultMapper.ComputeQueryHash(request.Question),
            BusinessQueryResultMapper.ComputeQueryHash(generated.Sql),
            generated.Sql,
            generated.Explanation,
            database.DefaultQueryLimit,
            database.MaxQueryLimit,
            SimulationBusinessQuerySchema.BlockedFieldFragments,
            generated.Warnings,
            now);

        draftStore.Save(draft);
        await WriteDraftAuditAsync(database, request.Question, generated.Sql, AuditResults.Succeeded, "Business Text-to-SQL draft generated.", cancellationToken);
        return Result.Success(draft);
    }

    public Task<Result<BusinessQueryResultDto>> ExecuteAsync(
        BusinessTextToSqlExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        Guid dataSourceId;
        string sql;
        int? requestedLimit = request.RequestedLimit;

        if (request.DraftId.HasValue)
        {
            if (!draftStore.TryGet(request.DraftId.Value, out var draft))
            {
                return Task.FromResult<Result<BusinessQueryResultDto>>(Result.Invalid("Business Text-to-SQL draft was not found or has expired."));
            }

            dataSourceId = draft.DataSourceId;
            sql = draft.SqlPreview;
        }
        else
        {
            if (!request.DataSourceId.HasValue || string.IsNullOrWhiteSpace(request.SqlPreview))
            {
                return Task.FromResult<Result<BusinessQueryResultDto>>(Result.Invalid("DraftId or DataSourceId with SqlPreview is required."));
            }

            dataSourceId = request.DataSourceId.Value;
            sql = request.SqlPreview;
        }

        return queryExecutor.ExecuteAsync(
            dataSourceId,
            sql,
            requestedLimit,
            requireSimulationBusiness: true,
            SimulationBusinessQuerySchema.SafetySchema,
            auditAction: "DataSource.TextToSqlExecute",
            cancellationToken);
    }

    private static string? ValidateSimulationBusinessDatabase(
        BusinessDatabase database,
        IReadOnlyCollection<string>? businessDomains)
    {
        if (!database.IsEnabled || !database.IsReadOnly)
        {
            return "Business Text-to-SQL requires an enabled readonly data source.";
        }

        if (database.ExternalSystemType != BusinessDataExternalSystemType.SimulationBusiness)
        {
            return "P1 Business Text-to-SQL only supports SimulationBusiness data sources.";
        }

        if (businessDomains is null || businessDomains.Count == 0)
        {
            return null;
        }

        var allowed = businessDomains
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0 ||
            allowed.Contains(database.BusinessDomain) ||
            allowed.Contains(database.Category))
        {
            return null;
        }

        return "Requested business domain is not available on the selected data source.";
    }

    private static int ResolveLimit(BusinessDatabase database, int? requestedLimit)
    {
        var requested = requestedLimit.GetValueOrDefault(database.DefaultQueryLimit);
        if (requested <= 0)
        {
            requested = database.DefaultQueryLimit;
        }

        return Math.Clamp(requested, 1, database.MaxQueryLimit);
    }

    private async Task WriteDraftAuditAsync(
        BusinessDatabase database,
        string question,
        string? sql,
        string result,
        string summary,
        CancellationToken cancellationToken)
    {
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.DataAnalysis,
                "DataSource.TextToSqlDraft",
                "BusinessDatabase",
                database.Id.ToString(),
                database.Name,
                result,
                summary,
                Metadata: new Dictionary<string, string>
                {
                    ["questionHash"] = BusinessQueryResultMapper.ComputeQueryHash(question),
                    ["sqlHash"] = sql is null ? string.Empty : BusinessQueryResultMapper.ComputeQueryHash(sql),
                    ["sqlLength"] = (sql ?? string.Empty).Length.ToString(),
                    ["sourceMode"] = database.ExternalSystemType.ToString()
                }),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class BusinessTextToSqlDraftStore
{
    private readonly ConcurrentDictionary<Guid, BusinessTextToSqlDraftDto> _drafts = new();

    public void Save(BusinessTextToSqlDraftDto draft)
    {
        _drafts[draft.DraftId] = draft;
    }

    public bool TryGet(Guid draftId, out BusinessTextToSqlDraftDto draft)
    {
        return _drafts.TryGetValue(draftId, out draft!);
    }
}

internal static class SimulationBusinessQuerySchema
{
    public static readonly IReadOnlySet<string> AllowedTables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "employees",
            "attendance",
            "production_devices",
            "production_records",
            "device_events",
            "quality_inspections",
            "inventory_movements",
            "purchase_orders",
            "sales_orders",
            "delivery_records",
            "customer_complaints"
        };

    public static readonly IReadOnlyList<string> BlockedFieldFragments =
    [
        "api_key",
        "connection_string",
        "credential",
        "password",
        "secret",
        "token"
    ];

    public static readonly BusinessQuerySafetySchema SafetySchema =
        new(AllowedTables, BlockedFieldFragments.ToHashSet(StringComparer.OrdinalIgnoreCase));
}

internal sealed record GeneratedBusinessSql(
    string Sql,
    string Explanation,
    IReadOnlyList<string> Warnings);

internal static class BusinessTextToSqlRuleBasedGenerator
{
    public static GeneratedBusinessSql Generate(string question, int limit)
    {
        var normalized = question.Trim().ToLowerInvariant();
        if (ContainsAny(normalized, "complaint", "\u6295\u8bc9", "\u5ba2\u8bc9"))
        {
            return new GeneratedBusinessSql(
                $"""
                 SELECT so.customer_code, cc.complaint_type, cc.severity, COUNT(*) AS complaint_count
                 FROM customer_complaints cc
                 JOIN sales_orders so ON so.sales_order_id = cc.sales_order_id
                 GROUP BY so.customer_code, cc.complaint_type, cc.severity
                 ORDER BY complaint_count DESC
                 LIMIT {limit}
                 """,
                "Summarizes customer complaints by customer, type, and severity from the SimulationBusiness sales and complaint tables.",
                []);
        }

        if (ContainsAny(normalized, "employee", "attendance", "leave", "\u5458\u5de5", "\u8003\u52e4", "\u5a5a\u5047", "\u5047"))
        {
            return new GeneratedBusinessSql(
                $"""
                 SELECT e.department, COUNT(DISTINCT e.employee_id) AS employee_count, COUNT(a.attendance_id) AS leave_records
                 FROM employees e
                 LEFT JOIN attendance a ON a.employee_id = e.employee_id AND a.leave_type IS NOT NULL
                 GROUP BY e.department
                 ORDER BY leave_records DESC
                 LIMIT {limit}
                 """,
                "Summarizes employee leave records from SimulationBusiness employee and attendance tables.",
                ["Policy interpretation still requires RAG policy context; this query only returns simulated attendance facts."]);
        }

        if (ContainsAny(normalized, "quality", "defect", "pass", "\u8d28\u91cf", "\u4e0d\u826f", "\u5408\u683c"))
        {
            return new GeneratedBusinessSql(
                $"""
                 SELECT product_code, defect_type, COUNT(*) AS inspection_count, SUM(defect_qty) AS defect_qty,
                        ROUND(SUM(defect_qty)::numeric / NULLIF(SUM(sample_qty), 0), 4) AS defect_rate
                 FROM quality_inspections
                 GROUP BY product_code, defect_type
                 ORDER BY defect_rate DESC, defect_qty DESC
                 LIMIT {limit}
                 """,
                "Summarizes simulated quality inspections and defect rates by product and defect type.",
                []);
        }

        if (ContainsAny(normalized, "downtime", "device", "alarm", "\u8bbe\u5907", "\u505c\u673a", "\u62a5\u8b66"))
        {
            return new GeneratedBusinessSql(
                $"""
                 SELECT pd.device_code, pd.line_code, COUNT(*) AS event_count, SUM(de.duration_minutes) AS downtime_minutes
                 FROM device_events de
                 JOIN production_devices pd ON pd.device_id = de.device_id
                 WHERE de.event_type IS NOT NULL AND de.duration_minutes > 0
                 GROUP BY pd.device_code, pd.line_code
                 ORDER BY downtime_minutes DESC, event_count DESC
                 LIMIT {limit}
                 """,
                "Summarizes simulated device downtime by device and production line.",
                []);
        }

        if (ContainsAny(normalized, "inventory", "stock", "material", "\u5e93\u5b58", "\u7269\u6599", "\u5468\u8f6c", "\u91c7\u8d2d"))
        {
            return new GeneratedBusinessSql(
                $"""
                 SELECT material_code, warehouse_code,
                        SUM(quantity) AS movement_quantity,
                        COUNT(*) AS movement_count
                 FROM inventory_movements
                 GROUP BY material_code, warehouse_code
                 ORDER BY movement_count DESC
                 LIMIT {limit}
                 """,
                "Summarizes simulated inventory movement volume and net quantity by material and warehouse.",
                []);
        }

        if (ContainsAny(normalized, "sales", "delivery", "order", "customer", "\u9500\u552e", "\u4ea4\u4ed8", "\u8ba2\u5355", "\u5ba2\u6237"))
        {
            return new GeneratedBusinessSql(
                $"""
                 SELECT customer_code, product_code, COUNT(*) AS order_count, SUM(order_qty) AS order_qty,
                        SUM(delivered_qty) AS delivered_qty, SUM(order_qty - delivered_qty) AS backlog_qty
                 FROM sales_orders
                 GROUP BY customer_code, product_code
                 ORDER BY backlog_qty DESC, order_count DESC
                 LIMIT {limit}
                 """,
                "Summarizes simulated sales delivery backlog by customer and product.",
                []);
        }

        if (ContainsAny(normalized, "capacity", "production", "output", "\u4ea7\u80fd", "\u751f\u4ea7", "\u4ea7\u91cf"))
        {
            return new GeneratedBusinessSql(
                $"""
                 SELECT pr.production_date, pd.line_code, SUM(pr.planned_qty) AS planned_qty,
                        SUM(pr.actual_qty) AS actual_qty, SUM(pr.scrap_qty) AS scrap_qty,
                        ROUND(SUM(pr.actual_qty)::numeric / NULLIF(SUM(pr.planned_qty), 0), 4) AS completion_rate
                 FROM production_records pr
                 JOIN production_devices pd ON pd.device_id = pr.device_id
                 GROUP BY pr.production_date, pd.line_code
                 ORDER BY pr.production_date DESC, pd.line_code
                 LIMIT {limit}
                 """,
                "Summarizes simulated production capacity by date and line.",
                []);
        }

        return new GeneratedBusinessSql(
            $"""
             SELECT 'production_records' AS table_name, COUNT(*) AS row_count FROM production_records
             UNION ALL SELECT 'quality_inspections', COUNT(*) FROM quality_inspections
             UNION ALL SELECT 'inventory_movements', COUNT(*) FROM inventory_movements
             UNION ALL SELECT 'sales_orders', COUNT(*) FROM sales_orders
             UNION ALL SELECT 'employees', COUNT(*) FROM employees
             LIMIT {limit}
             """,
            "Returns a high-level row-count overview of the SimulationBusiness core tables.",
            ["The question did not match a specific P1 scenario, so a safe overview query was generated."]);
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
