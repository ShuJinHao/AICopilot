using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed class SemanticAnalysisRunner(
    ICloudAiReadClient cloudAiReadClient,
    IBusinessDatabaseReadService businessDatabaseReadService,
    IDatabaseConnector databaseConnector,
    ISemanticQueryPlanner semanticQueryPlanner,
    ISemanticPhysicalMappingProvider semanticPhysicalMappingProvider,
    ISemanticSqlGenerator semanticSqlGenerator,
    DataAnalysisAuditRecorder auditRecorder,
    ILogger<SemanticAnalysisRunner> logger,
    CloudReadOnlyTextToSqlFallbackRunner? cloudTextToSqlFallbackRunner = null)
{
    private static readonly DatabaseQueryOptions QueryOptions = new(MaxRows: 200, CommandTimeoutSeconds: 15);
    public const string RecipeDataReadBoundaryMarker = "当前 AI 不读取云端配方主数据或配方版本数据";
    private const string RecipeDataReadBoundaryMessage =
        "[系统提示]: " + RecipeDataReadBoundaryMarker + "。可以回答配方版本规则问题，但不能查询具体配方、设备配方清单或版本记录。";

    public async Task<string> RunAsync(IntentResult intent, CancellationToken cancellationToken)
    {
        return await RunAsync(intent, sink: null, cancellationToken);
    }

    public async Task<string> RunAsync(
        IntentResult intent,
        AgentWorkflowSink? sink,
        CancellationToken cancellationToken)
    {
        var planningResult = semanticQueryPlanner.Plan(intent.Intent, intent.Query);
        if (!planningResult.IsSuccess)
        {
            var failedTargetLabel = SemanticAnalysisPresentation.TryGetTargetLabel(intent.Intent);
            logger.LogWarning(
                "{TargetLabel}语义查询规划失败。Intent: {Intent}, Error: {Error}",
                failedTargetLabel,
                intent.Intent,
                planningResult.ErrorMessage);
            var fallback = await TryRunCloudReadOnlyTextToSqlFallbackAsync(
                database: null,
                question: intent.Query,
                requestedLimit: null,
                targetLabel: failedTargetLabel,
                cancellationToken);
            if (fallback?.Succeeded == true)
            {
                return fallback.Context;
            }

            return $"[系统提示]: {failedTargetLabel}语义查询规划失败 - {planningResult.ErrorMessage}";
        }

        var plan = planningResult.Plan!;
        var targetLabel = SemanticAnalysisPresentation.GetTargetLabel(plan.Target);
        if (plan.Target == SemanticQueryTarget.Recipe)
        {
            logger.LogInformation(
                "配方数据语义查询已按云端配方禁读边界拒绝。Intent: {Intent}, Kind: {Kind}",
                plan.Intent,
                plan.Kind);
            return RecipeDataReadBoundaryMessage;
        }

        if (cloudAiReadClient.IsEnabled && IsHighFrequencyCloudAiReadTarget(plan.Target))
        {
            return await RunCloudAiReadAsync(plan, targetLabel, sink, cancellationToken);
        }

        if (!semanticPhysicalMappingProvider.TryGetMapping(plan.Target, out var mapping))
        {
            if (cloudAiReadClient.IsEnabled && CloudAiReadSemanticSupport.IsSupported(plan.Target))
            {
                return await RunCloudAiReadAsync(plan, targetLabel, sink, cancellationToken);
            }

            var fallback = await TryRunCloudReadOnlyTextToSqlFallbackAsync(
                database: null,
                question: plan.QueryText,
                requestedLimit: plan.Limit,
                targetLabel,
                cancellationToken);
            if (fallback?.Succeeded == true)
            {
                return fallback.Context;
            }

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "{TargetLabel}语义查询未通过白名单 SQL 生成。Intent: {Intent}, DatabaseName: {DatabaseName}, ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                targetLabel,
                plan.Intent,
                businessDatabase.Name,
                ex.GetType().Name);
            var fallback = await TryRunCloudReadOnlyTextToSqlFallbackAsync(
                businessDatabase,
                plan.QueryText,
                plan.Limit,
                targetLabel,
                cancellationToken);
            if (fallback?.Succeeded == true)
            {
                return fallback.Context;
            }

            return $"[系统提示]: 当前{targetLabel}查询请求未通过安全白名单校验，系统已拒绝执行。";
        }

        if (businessDatabase.ExternalSystemType == DataSourceExternalSystemType.CloudReadOnly)
        {
            var cloudReadOnlySafetyError = CloudReadOnlySemanticSqlGuard.Validate(generatedSql.SqlText);
            if (cloudReadOnlySafetyError is not null)
            {
                logger.LogWarning(
                    "{TargetLabel}语义查询未通过 Cloud 只读安全白名单。Intent: {Intent}, DatabaseName: {DatabaseName}, Reason: {Reason}",
                    targetLabel,
                    plan.Intent,
                    businessDatabase.Name,
                    cloudReadOnlySafetyError);
                var fallback = await TryRunCloudReadOnlyTextToSqlFallbackAsync(
                    businessDatabase,
                    plan.QueryText,
                    plan.Limit,
                    targetLabel,
                    cancellationToken);
                if (fallback?.Succeeded == true)
                {
                    return fallback.Context;
                }

                return $"[系统提示]: 当前{targetLabel}查询请求未通过 Cloud 只读安全白名单校验，系统已拒绝执行。";
            }
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
            await auditRecorder.RecordSemanticQueryAsync(
                businessDatabase,
                plan,
                mapping,
                queryResult,
                cancellationToken);
            logger.LogInformation(
                "{TargetLabel}语义查询执行完成。Intent: {Intent}, Target: {Target}, Kind: {Kind}, DatabaseName: {DatabaseName}, Source: {Source}, RowsObserved: {RowsObserved}, Truncated: {Truncated}",
                targetLabel,
                plan.Intent,
                plan.Target,
                plan.Kind,
                businessDatabase.Name,
                mapping.SourceName,
                queryResult.ReturnedRowCount,
                queryResult.IsTruncated);

            var sourceLabel = SemanticAnalysisPresentation.BuildBusinessSourceLabel(
                targetLabel,
                businessDatabase.ExternalSystemType);
            var analysis = SemanticAnalysisPresentation.BuildAnalysis(
                plan,
                sourceLabel,
                semanticSummary,
                queryResult.IsTruncated);
            await TryEmitDeviceLogWidgetsAsync(plan, semanticSummary, normalizedRows, sink, cancellationToken);
            return DataAnalysisFinalContextFormatter.FormatSemantic(
                analysis,
                semanticSummary,
                normalizedRows,
                queryResult.IsTruncated,
                plan,
                queryResult.ReturnedRowCount);
        }
        catch (InvalidOperationException ex)
        {
            var executionFailure = CloudReadOnlyTextToSqlRepairClassifier.Classify(
                CloudReadOnlyTextToSqlFailureStage.Runtime,
                ex.Message);
            logger.LogWarning(
                "{TargetLabel}语义查询在执行阶段被安全规则拒绝。Intent: {Intent}, DatabaseName: {DatabaseName}, ErrorType={ErrorType}; FailureCode={FailureCode}; OriginalMessage=hidden_by_security_policy",
                targetLabel,
                plan.Intent,
                businessDatabase.Name,
                ex.GetType().Name,
                executionFailure.Code);
            var fallback = await TryRunCloudReadOnlyTextToSqlFallbackAsync(
                businessDatabase,
                plan.QueryText,
                plan.Limit,
                targetLabel,
                cancellationToken);
            if (fallback?.Succeeded == true)
            {
                return fallback.Context;
            }

            if (businessDatabase.ExternalSystemType == DataSourceExternalSystemType.CloudReadOnly &&
                executionFailure.Code == CloudReadOnlyTextToSqlFailureCode.Forbidden)
            {
                return $"[系统提示]: 当前{targetLabel}CloudReadOnly 只读权限不足 - {executionFailure.SafeSummary}";
            }

            return $"[系统提示]: 当前{targetLabel}查询请求被系统安全策略拒绝。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var executionFailure = CloudReadOnlyTextToSqlRepairClassifier.Classify(
                CloudReadOnlyTextToSqlFailureStage.Runtime,
                ex.Message);
            logger.LogError(
                "{TargetLabel}语义查询执行失败。Intent: {Intent}, DatabaseName: {DatabaseName}, ErrorType={ErrorType}; FailureCode={FailureCode}; OriginalMessage=hidden_by_security_policy",
                targetLabel,
                plan.Intent,
                businessDatabase.Name,
                ex.GetType().Name,
                executionFailure.Code);
            var fallback = await TryRunCloudReadOnlyTextToSqlFallbackAsync(
                businessDatabase,
                plan.QueryText,
                plan.Limit,
                targetLabel,
                cancellationToken);
            if (fallback?.Succeeded == true)
            {
                return fallback.Context;
            }

            if (businessDatabase.ExternalSystemType == DataSourceExternalSystemType.CloudReadOnly &&
                executionFailure.Code == CloudReadOnlyTextToSqlFailureCode.Forbidden)
            {
                return $"[系统提示]: 当前{targetLabel}CloudReadOnly 只读权限不足 - {executionFailure.SafeSummary}";
            }

            return $"[系统提示]: 当前{targetLabel}数据源暂时不可用，请稍后重试或联系管理员检查连接。";
        }
    }

    private async Task<CloudReadOnlyTextToSqlFallbackResult?> TryRunCloudReadOnlyTextToSqlFallbackAsync(
        BusinessDatabaseConnectionInfo? database,
        string? question,
        int? requestedLimit,
        string targetLabel,
        CancellationToken cancellationToken)
    {
        if (cloudTextToSqlFallbackRunner is null)
        {
            return null;
        }

        database ??= await ResolveDefaultCloudReadOnlyDatabaseAsync(cancellationToken);
        if (database is null || database.ExternalSystemType != DataSourceExternalSystemType.CloudReadOnly)
        {
            return null;
        }

        var result = await cloudTextToSqlFallbackRunner.RunAsync(
            database,
            question,
            requestedLimit,
            cancellationToken);
        if (result.Succeeded)
        {
            logger.LogInformation(
                "{TargetLabel}语义查询已通过 CloudReadOnly Text-to-SQL fallback 完成。DatabaseName: {DatabaseName}, RowsObserved: {RowsObserved}, Truncated: {Truncated}, RepairAttempts: {RepairAttempts}",
                targetLabel,
                database.Name,
                result.RowCount,
                result.IsTruncated,
                result.RepairAttempts.Count);
        }
        else
        {
            logger.LogWarning(
                "{TargetLabel}语义查询 CloudReadOnly Text-to-SQL fallback 未完成。DatabaseName: {DatabaseName}, Reason: {Reason}",
                targetLabel,
                database.Name,
                result.SafeMessage);
        }

        return result;
    }

    private async Task<BusinessDatabaseConnectionInfo?> ResolveDefaultCloudReadOnlyDatabaseAsync(
        CancellationToken cancellationToken)
    {
        var candidates = await businessDatabaseReadService.ListEnabledAsync(cancellationToken);
        var descriptor = candidates
            .Where(item => item.ExternalSystemType == DataSourceExternalSystemType.CloudReadOnly)
            .Where(item => item.IsEnabled && item.IsReadOnly && item.ReadOnlyCredentialVerified)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return descriptor is null
            ? null
            : await businessDatabaseReadService.GetByNameAsync(descriptor.Name, cancellationToken);
    }

    private async Task<string> RunCloudAiReadAsync(
        SemanticQueryPlan plan,
        string targetLabel,
        AgentWorkflowSink? sink,
        CancellationToken cancellationToken)
    {
        try
        {
            var queryResult = await cloudAiReadClient.QuerySemanticAsync(plan, cancellationToken);
            var rows = queryResult.Rows.ToList();
            var semanticSummary = SemanticSummaryBuilder.Build(plan, rows);
            var analysis = SemanticAnalysisPresentation.BuildAnalysis(
                plan,
                SemanticAnalysisPresentation.BuildCloudAiReadSourceLabel(targetLabel),
                semanticSummary,
                queryResult.IsTruncated);

            logger.LogInformation(
                "{TargetLabel}语义查询已通过 Cloud AiRead API 完成。Intent: {Intent}, SourcePath: {SourcePath}, RowsObserved: {RowsObserved}, Truncated: {Truncated}",
                targetLabel,
                plan.Intent,
                queryResult.SourcePath,
                rows.Count,
                queryResult.IsTruncated);

            await TryEmitDeviceLogWidgetsAsync(plan, semanticSummary, rows, sink, cancellationToken);
            return DataAnalysisFinalContextFormatter.FormatSemantic(
                analysis,
                semanticSummary,
                rows,
                queryResult.IsTruncated,
                plan,
                rows.Count);
        }
        catch (CloudAiReadException ex)
        {
            logger.LogWarning(
                "{TargetLabel} Cloud AiRead 查询被拒绝或暂不可用。Intent: {Intent}, Code: {Code}; ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                targetLabel,
                plan.Intent,
                ex.Code,
                ex.GetType().Name);
            return ex.Code switch
            {
                CloudAiReadProblemCodes.MissingRequiredParameter => $"[系统提示]: Cloud AiRead {targetLabel}查询缺少必要条件，请补充设备、时间范围或条码后重试。",
                CloudAiReadProblemCodes.Unauthorized => $"[系统提示]: Cloud AiRead {targetLabel}查询未通过身份凭据校验，请联系管理员检查只读服务账号。",
                CloudAiReadProblemCodes.Forbidden => $"[系统提示]: Cloud AiRead {targetLabel}查询权限或设备范围不足，系统已拒绝本次正式数据读取。",
                CloudAiReadProblemCodes.RequestBlocked => $"[系统提示]: Cloud AiRead {targetLabel}查询未通过只读白名单校验，系统已拒绝执行。",
                _ => $"[系统提示]: Cloud AiRead {targetLabel}只读接口暂不可用，请稍后重试或联系管理员检查配置。"
            };
        }
    }

    private async Task TryEmitDeviceLogWidgetsAsync(
        SemanticQueryPlan plan,
        SemanticSummaryDto semanticSummary,
        IReadOnlyList<Dictionary<string, object?>> rows,
        AgentWorkflowSink? sink,
        CancellationToken cancellationToken)
    {
        if (sink is null || plan.Target != SemanticQueryTarget.DeviceLog)
        {
            return;
        }

        try
        {
            foreach (var widget in DeviceLogSemanticDisplayBuilder.BuildWidgets(plan, semanticSummary, rows))
            {
                await sink.WriteAsync(
                    new ChatChunk(
                        DataAnalysisExecutor.ExecutorId,
                        ChunkType.Widget,
                        DataAnalysisWidgetPayloadSerializer.Serialize(widget)),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "{TargetLabel}语义查询展示组件构建失败。Intent: {Intent}, Target: {Target}, Kind: {Kind}, ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                SemanticAnalysisPresentation.GetTargetLabel(plan.Target),
                plan.Intent,
                plan.Target,
                plan.Kind,
                ex.GetType().Name);
        }
    }

    private static bool IsHighFrequencyCloudAiReadTarget(SemanticQueryTarget target)
    {
        return target is SemanticQueryTarget.DeviceLog
            or SemanticQueryTarget.Capacity
            or SemanticQueryTarget.ProductionData;
    }
}
