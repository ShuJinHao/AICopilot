using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.Visualization;
using AICopilot.Visualization.Widgets;

namespace AICopilot.AiGatewayService.Workflows.Executors;

/// <summary>
/// 数据分析执行器
/// 职责：处理 Analysis.* 意图，实例化 DBA Agent，执行 Text-to-SQL 任务。
/// </summary>
public class DataAnalysisExecutor(
    DataAnalysisAgentBuilder agentBuilder,
    IBusinessDatabaseReadService businessDatabaseReadService,
    IDatabaseConnector databaseConnector,
    IAuditLogWriter auditLogWriter,
    ISemanticIntentCatalog semanticIntentCatalog,
    ISemanticQueryPlanner semanticQueryPlanner,
    ISemanticPhysicalMappingProvider semanticPhysicalMappingProvider,
    ISemanticSqlGenerator semanticSqlGenerator,
    IDataAnalysisVisualizationContext vizContext,
    ApprovalRequirementResolver approvalRequirementResolver,
    ILogger<DataAnalysisExecutor> logger)
{
    public const string ExecutorId = nameof(DataAnalysisExecutor);
    private const string AnalysisIntentPrefix = "Analysis.";
    private static readonly DatabaseQueryOptions QueryOptions = new(MaxRows: 200, CommandTimeoutSeconds: 15);

    public async Task<BranchResult> ExecuteAsync(
        List<IntentResult> intentResults,
        ChatWorkflowSink? sink,
        SessionRuntimeSnapshot? session,
        CancellationToken ct = default)
    {
        // 1. 筛选数据分析类意图
        // 过滤规则：必须以 Analysis. 开头，且置信度高于 0.6
        var analysisIntents = intentResults
            .Where(i => i.Intent.StartsWith(AnalysisIntentPrefix, StringComparison.OrdinalIgnoreCase)
                        && i.Confidence > 0.6)
            .ToList();

        if (analysisIntents.Count == 0)
        {
            logger.LogDebug("未检测到数据分析意图，跳过执行。");
            // 返回空结果，表示该分支无产出
            return BranchResult.FromDataAnalysis(string.Empty);
        }

        logger.LogInformation("启动数据分析流程，命中 Analysis.* 意图数量: {Count}", analysisIntents.Count);

        var semanticIntents = analysisIntents
            .Where(intent => semanticIntentCatalog.TryGet(intent.Intent, out _))
            .ToList();
        var databaseIntents = analysisIntents
            .Where(intent => !semanticIntentCatalog.TryGet(intent.Intent, out _))
            .ToList();

        logger.LogInformation(
            "数据分析意图拆分完成。语义意图: {SemanticCount}, 自由分析意图: {DatabaseCount}",
            semanticIntents.Count,
            databaseIntents.Count);

        // 2. 遍历处理每一个意图
        var output = new StringBuilder();
        foreach (var intent in semanticIntents)
        {
            output.AppendLine(await ProcessSemanticIntentAsync(intent, ct));
        }

        foreach (var intent in databaseIntents)
        {
            output.AppendLine(await ProcessDatabaseIntentAsync(intent, sink, session, ct));
        }
        return BranchResult.FromDataAnalysis(output.ToString());

        async Task<string> ProcessSemanticIntentAsync(IntentResult intent, CancellationToken cancellationToken)
        {
            var planningResult = semanticQueryPlanner.Plan(intent.Intent, intent.Query);
            if (!planningResult.IsSuccess)
            {
                var failedTargetLabel = TryGetSemanticTargetLabel(intent.Intent);
                logger.LogWarning(
                    "{TargetLabel}语义查询规划失败。Intent: {Intent}, Error: {Error}",
                    failedTargetLabel,
                    intent.Intent,
                    planningResult.ErrorMessage);
                return $"[系统提示]: {failedTargetLabel}语义查询规划失败 - {planningResult.ErrorMessage}";
            }

            var plan = planningResult.Plan!;
            var targetLabel = GetSemanticTargetLabel(plan.Target);
            if (!semanticPhysicalMappingProvider.TryGetMapping(plan.Target, out var mapping))
            {
                logger.LogInformation(
                    "{TargetLabel}语义查询已识别，但尚未绑定只读业务库映射。Intent: {Intent}, Target: {Target}, Kind: {Kind}",
                    targetLabel,
                    plan.Intent,
                    plan.Target,
                    plan.Kind);
                return $"[系统提示]: 当前未找到{targetLabel}语义映射，请联系管理员检查后端映射配置。";
            }

            if (string.IsNullOrWhiteSpace(mapping.DatabaseName))
            {
                logger.LogWarning(
                    "{TargetLabel}语义映射缺少目标数据库名称。Intent: {Intent}, Target: {Target}",
                    targetLabel,
                    plan.Intent,
                    plan.Target);
                return $"[系统提示]: 当前{targetLabel}语义映射未绑定只读业务库，请联系管理员检查映射配置。";
            }

            var businessDatabase = await businessDatabaseReadService.GetByNameAsync(
                mapping.DatabaseName,
                cancellationToken);

            if (businessDatabase == null || !businessDatabase.IsEnabled)
            {
                logger.LogWarning(
                    "{TargetLabel}语义查询找不到启用中的只读业务库。Intent: {Intent}, DatabaseName: {DatabaseName}",
                    targetLabel,
                    plan.Intent,
                    mapping.DatabaseName);
                return $"[系统提示]: 当前未找到可用的{targetLabel}只读数据源，请联系管理员检查配置。";
            }

            if (!businessDatabase.IsReadOnly)
            {
                logger.LogWarning(
                    "{TargetLabel}语义查询命中的业务库未处于只读模式。Intent: {Intent}, DatabaseName: {DatabaseName}",
                    targetLabel,
                    plan.Intent,
                    businessDatabase.Name);
                return $"[系统提示]: 当前{targetLabel}数据源未处于只读模式，系统已拒绝本次查询。";
            }

            if (businessDatabase.Provider != mapping.Provider)
            {
                logger.LogWarning(
                    "{TargetLabel}语义查询的业务库类型与映射定义不匹配。Intent: {Intent}, DatabaseName: {DatabaseName}, DatabaseProvider: {DatabaseProvider}, MappingProvider: {MappingProvider}",
                    targetLabel,
                    plan.Intent,
                    businessDatabase.Name,
                    businessDatabase.Provider,
                    mapping.Provider);
                return $"[系统提示]: 当前{targetLabel}数据源类型与语义映射不匹配，请联系管理员检查配置。";
            }

            GeneratedSemanticSql generatedSql;
            try
            {
                generatedSql = semanticSqlGenerator.Generate(plan, mapping);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "{TargetLabel}语义查询未通过白名单 SQL 生成。Intent: {Intent}, DatabaseName: {DatabaseName}",
                    targetLabel,
                    plan.Intent,
                    businessDatabase.Name);
                return $"[系统提示]: 当前{targetLabel}查询请求未通过安全白名单校验，系统已拒绝执行。";
            }

            try
            {
                var queryResult = await databaseConnector.ExecuteQueryWithMetadataAsync(
                    businessDatabase,
                    generatedSql.SqlText,
                    generatedSql.Parameters,
                    QueryOptions,
                    cancellationToken);

                var normalizedRows = queryResult.Rows.ToList();
                var semanticSummary = SemanticSummaryBuilder.Build(plan, normalizedRows);
                await auditLogWriter.WriteAsync(
                    new AuditLogWriteRequest(
                        "DataAnalysis",
                        "DataAnalysis.ExecuteSemanticQuery",
                        "BusinessDatabase",
                        businessDatabase.Id.ToString(),
                        businessDatabase.Name,
                        AuditResults.Succeeded,
                        $"语义查询已执行。Target={plan.Target}; Source={mapping.SourceName}; Rows={queryResult.ReturnedRowCount}; Truncated={queryResult.IsTruncated}; ElapsedMs={queryResult.ElapsedMilliseconds}."),
                    cancellationToken);
                await auditLogWriter.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "{TargetLabel}语义查询执行完成。Intent: {Intent}, Target: {Target}, Kind: {Kind}, DatabaseName: {DatabaseName}, Source: {Source}, RowCount: {RowCount}, Truncated: {Truncated}",
                    targetLabel,
                    plan.Intent,
                    plan.Target,
                    plan.Kind,
                    businessDatabase.Name,
                    mapping.SourceName,
                    queryResult.ReturnedRowCount,
                    queryResult.IsTruncated);

                var combinedOutput = new
                {
                    analysis = BuildSemanticAnalysis(plan, businessDatabase.Name, semanticSummary, queryResult.IsTruncated),
                    visual_decision = (VisualDecisionDto?)null,
                    semantic_summary = semanticSummary,
                    data = normalizedRows
                };

                return combinedOutput.ToJson();
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(
                    ex,
                    "{TargetLabel}语义查询在执行阶段被安全规则拒绝。Intent: {Intent}, DatabaseName: {DatabaseName}",
                    targetLabel,
                    plan.Intent,
                    businessDatabase.Name);
                return $"[系统提示]: 当前{targetLabel}查询请求被系统安全策略拒绝。";
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "{TargetLabel}语义查询执行失败。Intent: {Intent}, DatabaseName: {DatabaseName}",
                    targetLabel,
                    plan.Intent,
                    businessDatabase.Name);
                return $"[系统提示]: 当前{targetLabel}数据源暂时不可用，请稍后重试或联系管理员检查连接。";
            }
        }
    }

    /// <summary>
    /// 处理单个数据库查询意图
    /// </summary>
    private async Task<string> ProcessDatabaseIntentAsync(
        IntentResult intent,
        ChatWorkflowSink? sink,
        SessionRuntimeSnapshot? session,
        CancellationToken ct)
    {
        var dbName = intent.Intent.Substring(AnalysisIntentPrefix.Length);

        try
        {
            // 1. 获取数据库配置
            // 我们需要 BusinessDatabase 实体来决定方言策略
            var db = await businessDatabaseReadService.GetByNameAsync(dbName, ct);

            if (db == null || !db.IsEnabled)
            {
                logger.LogWarning("意图指向数据库 '{DbName}'，但该库不存在或已禁用。", dbName);
                return $"[系统提示]: 无法连接数据库 {dbName}，请联系管理员核实配置。";
            }

            if (!db.IsReadOnly)
            {
                logger.LogWarning("意图指向数据库 '{DbName}'，但该库未配置为只读模式。", dbName);
                return $"[系统提示]: 数据库 {dbName} 未处于只读模式，系统已拒绝本次 AI 查询。";
            }

            // 2. 构建 DBA Agent
            // 这里会动态注入 PG 或 SQLServer 的方言提示词
            await using var scopedAgent = await agentBuilder.BuildAsync(db);
            // 创建临时会话线程
            var thread = await scopedAgent.Agent.CreateSessionAsync(ct);

            // 4. 执行 ReAct 循环
            // Agent 会自动进行: 思考 -> GetTableNames -> 思考 -> GetTableSchema -> 思考 -> ExecuteSQL -> 总结
            await foreach (var update in scopedAgent.Agent.RunStreamingAsync(intent.Query!, thread, cancellationToken: ct))
            {
                if (sink is null)
                {
                    continue;
                }

                await foreach (var chunk in ChatStreamRuntime.CreateUpdateChunksAsync(
                                   approvalRequirementResolver,
                                   update,
                                   ExecutorId,
                                   session,
                                   assistantText: null,
                                   appendAssistantText: false,
                                   ct))
                {
                    await sink.WriteAsync(chunk, ct);
                }
            }

            // 记录日志以便调试
            logger.LogInformation("数据库 {DbName} 查询完成。", dbName);

            // 获取可视化上下文
            var (rawData, schema) = vizContext.GetLastResult();
            var output = vizContext.GetOutput();

            // =========================================================
            // 分流路径 1：旁路输出 (Side Path) -> 前端 Widget
            // 目标：visual_decision + data -> Widget JSON
            // =========================================================
            if (output is { Decision: not null } && vizContext.HasData)
            {
                try
                {
                    var widget = BuildWidget(output.Decision, rawData!, schema!);
                    if (sink is not null)
                    {
                        await sink.WriteAsync(new ChatChunk(ExecutorId, ChunkType.Widget, widget.ToJson()), ct);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "构建可视化 Widget 失败。Database: {DbName}", dbName);
                    // 不中断流程，降级为文本输出
                }
            }

            // =========================================================
            // 分流路径 2：主路输出 (Main Path) -> 聚合器 -> Final Agent
            // 目标：schema + data -> Combined JSON
            // =========================================================

            var combinedOutput = new
            {
                analysis = output.Analysis,
                visual_decision = output.Decision,
                data = rawData ?? []
            };

            return combinedOutput.ToJson();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "执行数据分析意图时命中安全限制。Database: {DbName}", dbName);
            return $"[系统提示]: 查询数据库 {dbName} 的请求被系统安全策略拒绝。";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "执行数据分析意图失败。Database: {DbName}", dbName);
            return $"[系统提示]: 查询数据库 {dbName} 时发生异常，请稍后重试或联系管理员检查只读数据源配置。";
        }
    }

    private IWidget BuildWidget(VisualDecisionDto decision, IEnumerable<dynamic> data, IEnumerable<SchemaColumn> schema)
    {
        switch (decision.Type)
        {
            case WidgetType.StatsCard:
                // 取第一行第一列，或者根据列名查找
                var firstRow = data.First() as IDictionary<string, object>;
                if (firstRow == null || firstRow.Count == 0)
                {
                    throw new InvalidOperationException("StatsCard widget requires at least one row of data.");
                }

                var value = firstRow.Values.First();

                return new StatsCardWidget
                {
                    Title = decision.Title,
                    Description = decision.Description,
                    Data = new StatsCardData
                    {
                        Label = decision.Title,
                        Value = value,
                        Unit = decision.Unit
                    }
                };

            case WidgetType.DataTable:
                return new DataTableWidget
                {
                    Title = decision.Title,
                    Data = data.ToDataTableData(schema)
                };

            case WidgetType.Chart:
                var dataset = data.ToChartDataset(schema);
                return new ChartWidget
                {
                    Title = decision.Title,
                    Data = new ChartData
                    {
                        Category = decision.ChartConfig!.Category,
                        Dataset = dataset,
                        Encoding = new ChartEncoding
                        {
                            X = decision.ChartConfig.X,
                            Y = string.IsNullOrWhiteSpace(decision.ChartConfig.Y)
                                ? []
                                : [decision.ChartConfig.Y],
                            SeriesName = decision.ChartConfig.Series
                        }
                    }
                };

            default:
                throw new NotSupportedException($"不支持的 Widget 类型: {decision.Type}");
        }
    }

    private static AnalysisDto BuildSemanticAnalysis(
        SemanticQueryPlan plan,
        string databaseName,
        SemanticSummaryDto semanticSummary,
        bool isTruncated)
    {
        return new AnalysisDto
        {
            DatabaseName = databaseName,
            Description = BuildSemanticDescription(plan, semanticSummary, isTruncated),
            Metadata = plan.Projection.Fields
                .Select(field => new MetadataItemDto
                {
                    Name = field,
                    Description = GetSemanticFieldDescription(field)
                })
                .ToList()
        };
    }

    private static string BuildSemanticDescription(
        SemanticQueryPlan plan,
        SemanticSummaryDto semanticSummary,
        bool isTruncated)
    {
        var targetDescription = plan.Target switch
        {
            SemanticQueryTarget.Device => plan.Kind switch
            {
                SemanticQueryKind.List => "设备列表查询",
                SemanticQueryKind.Detail => "设备详情查询",
                SemanticQueryKind.Status => "设备状态查询",
                _ => "设备查询"
            },
            SemanticQueryTarget.DeviceLog => plan.Kind switch
            {
                SemanticQueryKind.Latest => "最新设备日志查询",
                SemanticQueryKind.Range => "设备日志时间范围查询",
                SemanticQueryKind.ByLevel => "设备日志级别查询",
                _ => "设备日志查询"
            },
            SemanticQueryTarget.Recipe => plan.Kind switch
            {
                SemanticQueryKind.List => "配方列表查询",
                SemanticQueryKind.Detail => "配方详情查询",
                SemanticQueryKind.VersionHistory => "配方版本历史查询",
                _ => "配方查询"
            },
            SemanticQueryTarget.Capacity => plan.Kind switch
            {
                SemanticQueryKind.Range => "产能时间范围查询",
                SemanticQueryKind.ByDevice => "设备产能查询",
                SemanticQueryKind.ByProcess => "工序产能查询",
                _ => "产能查询"
            },
            SemanticQueryTarget.ProductionData => plan.Kind switch
            {
                SemanticQueryKind.Latest => "最新生产数据查询",
                SemanticQueryKind.Range => "生产数据时间范围查询",
                SemanticQueryKind.ByDevice => "设备生产数据查询",
                _ => "生产数据查询"
            },
            _ => "业务查询"
        };

        var scope = string.IsNullOrWhiteSpace(semanticSummary.Scope)
            ? "结果上限以内的匹配记录"
            : semanticSummary.Scope;

        var truncationNote = isTruncated ? " 结果已截断。" : string.Empty;
        return $"{targetDescription}，{semanticSummary.Conclusion} 查询范围：{scope}。{truncationNote}";
    }

    private static string GetSemanticFieldDescription(string field)
    {
        return field switch
        {
            "deviceId" => "设备标识",
            "deviceCode" => "设备编码",
            "deviceName" => "设备名称",
            "status" => "设备状态",
            "lineName" => "产线",
            "updatedAt" => "时间",
            "logId" => "日志标识",
            "level" => "日志级别",
            "message" => "日志内容",
            "source" => "日志来源",
            "occurredAt" => "时间",
            "recipeId" => "配方标识",
            "recipeName" => "配方名称",
            "processName" => "工序名称",
            "version" => "版本号",
            "isActive" => "当前生效版本",
            "recordId" => "记录标识",
            "shiftDate" => "时间",
            "outputQty" => "总产出",
            "qualifiedQty" => "合格数",
            "barcode" => "条码",
            "stationName" => "工位名称",
            "result" => "生产结果",
            _ => field
        };
    }

    private static string GetSemanticTargetLabel(SemanticQueryTarget target)
    {
        return target switch
        {
            SemanticQueryTarget.Device => "设备",
            SemanticQueryTarget.DeviceLog => "设备日志",
            SemanticQueryTarget.Recipe => "配方",
            SemanticQueryTarget.Capacity => "产能",
            SemanticQueryTarget.ProductionData => "生产数据",
            _ => "业务"
        };
    }

    private static string TryGetSemanticTargetLabel(string intent)
    {
        if (intent.StartsWith("Analysis.DeviceLog.", StringComparison.OrdinalIgnoreCase))
        {
            return "设备日志";
        }

        if (intent.StartsWith("Analysis.Device.", StringComparison.OrdinalIgnoreCase))
        {
            return "设备";
        }

        if (intent.StartsWith("Analysis.Recipe.", StringComparison.OrdinalIgnoreCase))
        {
            return "配方";
        }

        if (intent.StartsWith("Analysis.Capacity.", StringComparison.OrdinalIgnoreCase))
        {
            return "产能";
        }

        if (intent.StartsWith("Analysis.ProductionData.", StringComparison.OrdinalIgnoreCase))
        {
            return "生产数据";
        }

        return "业务";
    }

}
