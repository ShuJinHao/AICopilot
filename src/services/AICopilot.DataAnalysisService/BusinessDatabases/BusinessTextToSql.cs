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

[AuthorizeRequirement("DataSource.TextToSql")]
public sealed record GenerateBusinessTextToSqlCommand(
    Guid DataSourceId,
    string Question,
    IReadOnlyCollection<string>? BusinessDomains = null,
    int? RequestedLimit = null,
    bool PreviewOnly = true) : ICommand<Result<BusinessTextToSqlDraftDto>>;

[AuthorizeRequirement("DataSource.TextToSql")]
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
    BusinessDatabaseAccessService accessService,
    IAuditLogWriter auditLogWriter)
    : IBusinessTextToSqlRuntime
{
    private const string RedactedSqlPreviewMarker = "SQL_PREVIEW_REDACTED_USE_DRAFT_ID";

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

        if (!await accessService.CanQueryAsync(database, cancellationToken))
        {
            await WriteDraftAuditAsync(
                database,
                request.Question,
                null,
                AuditResults.Rejected,
                "Business Text-to-SQL draft rejected because the current user is not authorized for this data source.",
                cancellationToken);
            return Result.Forbidden(new ApiProblemDescriptor(
                "data_source_forbidden",
                "Current user is not authorized to query this business data source."));
        }

        var validation = ValidateSimulationBusinessDatabase(database, request.BusinessDomains);
        if (validation is not null)
        {
            await WriteDraftAuditAsync(database, request.Question, null, AuditResults.Rejected, validation, cancellationToken);
            return Result.Invalid(validation);
        }

        var limit = ResolveLimit(database, request.RequestedLimit);
        var generated = BusinessTextToSqlRuleBasedGenerator.Generate(request.Question, limit);

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
            RedactedSqlPreviewMarker,
            generated.Explanation,
            database.DefaultQueryLimit,
            database.MaxQueryLimit,
            SimulationBusinessQuerySchema.BlockedFieldFragments,
            generated.Warnings,
            now);

        draftStore.Save(draft, generated.Sql);
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
            if (!draftStore.TryGet(request.DraftId.Value, out var draft, out var storedSql))
            {
                return Task.FromResult<Result<BusinessQueryResultDto>>(Result.Invalid("Business Text-to-SQL draft was not found or has expired."));
            }

            dataSourceId = draft.DataSourceId;
            sql = storedSql;
        }
        else
        {
            return Task.FromResult<Result<BusinessQueryResultDto>>(Result.Invalid(
                "Business Text-to-SQL execution requires a governed draft id; free SQL preview execution is not allowed."));
        }

        return queryExecutor.ExecuteAsync(
            dataSourceId,
            sql,
            requestedLimit,
            requireSimulationBusiness: true,
            SimulationBusinessQuerySchema.SafetySchema,
            auditAction: "DataSource.TextToSqlExecute",
            cancellationToken,
            selectionMode: DataSourceSelectionMode.TextToSql);
    }

    private static string? ValidateSimulationBusinessDatabase(
        BusinessDatabase database,
        IReadOnlyCollection<string>? businessDomains)
    {
        if (!database.IsEnabled || !database.IsReadOnly)
        {
            return "Business Text-to-SQL requires an enabled readonly data source.";
        }

        if (!database.IsSelectableInChat)
        {
            return "Business Text-to-SQL requires a data source selectable in Chat mode.";
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
    private readonly ConcurrentDictionary<Guid, BusinessTextToSqlDraftEntry> _drafts = new();

    public void Save(BusinessTextToSqlDraftDto draft, string sql)
    {
        _drafts[draft.DraftId] = new BusinessTextToSqlDraftEntry(draft, sql);
    }

    public bool TryGet(Guid draftId, out BusinessTextToSqlDraftDto draft, out string sql)
    {
        if (_drafts.TryGetValue(draftId, out var entry))
        {
            draft = entry.Draft;
            sql = entry.Sql;
            return true;
        }

        draft = null!;
        sql = string.Empty;
        return false;
    }

    private sealed record BusinessTextToSqlDraftEntry(BusinessTextToSqlDraftDto Draft, string Sql);
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

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedColumns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["employees"] = Columns("employee_id", "employee_no", "employee_name", "department", "position_name", "hire_date", "employment_status"),
            ["attendance"] = Columns("attendance_id", "employee_id", "attendance_date", "shift_code", "work_hours", "overtime_hours", "leave_type"),
            ["production_devices"] = Columns("device_id", "device_code", "device_name", "workshop", "line_code", "device_status"),
            ["production_records"] = Columns("record_id", "device_id", "production_date", "product_code", "planned_qty", "actual_qty", "scrap_qty", "shift_code"),
            ["device_events"] = Columns("event_id", "device_id", "event_time", "event_type", "severity", "duration_minutes"),
            ["quality_inspections"] = Columns("inspection_id", "production_record_id", "inspection_time", "product_code", "sample_qty", "defect_qty", "defect_type", "result"),
            ["inventory_movements"] = Columns("movement_id", "material_code", "warehouse_code", "movement_time", "movement_type", "quantity", "unit"),
            ["purchase_orders"] = Columns("purchase_order_id", "supplier_code", "material_code", "order_date", "promised_date", "quantity", "received_quantity"),
            ["sales_orders"] = Columns("sales_order_id", "customer_code", "order_date", "promised_date", "product_code", "order_qty", "delivered_qty", "order_status"),
            ["delivery_records"] = Columns("delivery_id", "sales_order_id", "delivery_date", "delivered_qty", "carrier_code", "delivery_status"),
            ["customer_complaints"] = Columns("complaint_id", "sales_order_id", "complaint_date", "complaint_type", "severity", "resolution_status")
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
        new(
            AllowedTables,
            BlockedFieldFragments.ToHashSet(StringComparer.OrdinalIgnoreCase),
            AllowedColumnFragments: AllowedColumns.Values
                .SelectMany(columns => columns)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            SensitiveColumnFragments: BlockedFieldFragments.ToHashSet(StringComparer.OrdinalIgnoreCase),
            AllowedColumns: AllowedColumns);

    private static IReadOnlySet<string> Columns(params string[] columns)
    {
        return columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
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
                 FROM public.customer_complaints cc
                 JOIN public.sales_orders so ON so.sales_order_id = cc.sales_order_id
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
                 FROM public.employees e
                 LEFT JOIN public.attendance a ON a.employee_id = e.employee_id AND a.leave_type IS NOT NULL
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
                 FROM public.quality_inspections
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
                 FROM public.device_events de
                 JOIN public.production_devices pd ON pd.device_id = de.device_id
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
                 FROM public.inventory_movements
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
                 FROM public.sales_orders
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
                 FROM public.production_records pr
                 JOIN public.production_devices pd ON pd.device_id = pr.device_id
                 GROUP BY pr.production_date, pd.line_code
                 ORDER BY pr.production_date DESC, pd.line_code
                 LIMIT {limit}
                 """,
                "Summarizes simulated production capacity by date and line.",
                []);
        }

        return new GeneratedBusinessSql(
            $"""
             SELECT COUNT(*) AS row_count
             FROM public.production_records
             LIMIT {limit}
             """,
            "Returns a row-count overview of the SimulationBusiness production records table.",
            ["The question did not match a specific P1 scenario, so a single-table safe overview query was generated."]);
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
