using System.Text.Json;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Paging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentTaskAuditQueryCoordinator(
    IRepository<AgentTask> taskRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IToolExecutionAuditStore toolExecutionAuditStore,
    IAuditLogQueryService auditLogQueryService,
    IAgentTaskRunAttemptStore runAttemptStore,
    IAgentTaskRunQueueStore queueStore,
    ICurrentUser currentUser,
    IAgentNodeRunStore? nodeRunStore = null)
{
    private const int MaxSummaryItems = 200;

    public async Task<Result<IReadOnlyCollection<AgentTaskAuditSummaryDto>>> GetSummaryAsync(
        GetAgentTaskAuditSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var taskResult = await LoadTaskAsync(request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var workspace = await LoadWorkspaceAsync(task, cancellationToken);
        var taskId = task.Id.Value.ToString();
        var workspaceCode = workspace?.WorkspaceCode;
        var toolRecords = await toolExecutionAuditStore.ListByTaskAsync(task.Id, cancellationToken);
        var auditLogs = await auditLogQueryService.GetListAsync(
            page: 1,
            pageSize: MaxSummaryItems,
            actionGroup: AuditActionGroups.AiGateway,
            actionCode: null,
            targetType: null,
            targetId: null,
            targetName: null,
            operatorUserName: null,
            result: null,
            from: null,
            to: null,
            cancellationToken);

        var items = auditLogs.Items
            .Where(item => BelongsToTask(item, taskId, workspaceCode))
            .Select(item => new AgentTaskAuditSummaryDto(
                item.Id,
                task.Id,
                ResolveWorkspaceCode(item.Metadata, workspaceCode),
                item.ActionCode,
                item.TargetType,
                item.TargetName ?? string.Empty,
                item.Result,
                item.Summary,
                item.CreatedAt,
                item.Metadata))
            .ToList();

        items.AddRange(toolRecords.Select(record => MapToolExecutionRecord(task, workspaceCode, record)));

        var failureSummary = AgentTaskFailureSummaryResolver.Resolve(task, toolRecords);
        if (failureSummary is not null)
        {
            items.Add(MapFailureSummary(task, workspaceCode, failureSummary));
        }

        return Result.Success<IReadOnlyCollection<AgentTaskAuditSummaryDto>>(
            items
                .OrderBy(item => item.CreatedAt)
                .Take(MaxSummaryItems)
                .ToArray());
    }

    public async Task<Result<AgentTaskRunAttemptPageDto>> GetRunAttemptsAsync(
        GetAgentTaskRunAttemptsQuery request,
        CancellationToken cancellationToken)
    {
        var taskResult = await LoadTaskAsync(request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var pagination = new Pagination
        {
            PageNumber = request.PageIndex,
            PageSize = request.PageSize
        };
        var attempts = await runAttemptStore.ListByTaskAsync(taskResult.Value!.Id, cancellationToken);
        var ordered = attempts
            .OrderByDescending(attempt => attempt.StartedAt)
            .ThenByDescending(attempt => attempt.AttemptNo)
            .ToArray();
        var items = ordered
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(AgentTaskRunAttemptDtoMapper.Map)
            .ToArray();
        var totalPages = (int)Math.Ceiling(ordered.Length / (double)pagination.PageSize);

        return Result.Success(new AgentTaskRunAttemptPageDto(
            items,
            pagination.PageNumber,
            pagination.PageSize,
            ordered.Length,
            totalPages,
            pagination.PageNumber > 1,
            pagination.PageNumber < totalPages));
    }

    public async Task<Result<AgentTaskRuntimeSnapshotDto>> GetRuntimeSnapshotAsync(
        GetAgentTaskRuntimeSnapshotQuery request,
        CancellationToken cancellationToken)
    {
        var taskResult = await LoadTaskAsync(request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var nowUtc = DateTimeOffset.UtcNow;
        var workspace = await LoadWorkspaceAsync(task, cancellationToken, includeArtifacts: true);
        var attempts = await runAttemptStore.ListByTaskAsync(task.Id, cancellationToken);
        var attempt = attempts
            .OrderByDescending(item => item.AttemptNo)
            .ThenByDescending(item => item.StartedAt)
            .FirstOrDefault();
        if (attempt is null)
        {
            return Result.Success(new AgentTaskRuntimeSnapshotDto(
                task.Id.Value,
                RunAttemptId: null,
                "NotStarted",
                nowUtc,
                EvidenceSetDigest: null,
                Nodes: [],
                Evidence: [],
                Metrics: BuildNotStartedMetrics()));
        }

        if (nodeRunStore is null)
        {
            return RuntimeSnapshotFailure(
                "The durable NodeRun query surface is unavailable for this runtime snapshot.");
        }

        var nodes = await nodeRunStore.ListByAttemptAsync(attempt.Id, cancellationToken);
        var evidenceRecords = await nodeRunStore.ListEvidenceByAttemptAsync(attempt.Id, cancellationToken);
        var queueItems = await queueStore.ListByTaskAsync(task.Id, cancellationToken);
        var nodesById = nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var evidence = new List<AgentRuntimeEvidenceDto>(evidenceRecords.Count);
        string? latestLineageEvidenceSetDigest = null;
        string? latestArtifactEvidenceSetDigest = null;
        foreach (var record in evidenceRecords.OrderBy(item => item.CreatedAt).ThenBy(item => item.NodeId))
        {
            var access = AgentEvidenceAccessChecker.ValidateDurable(
                record,
                task,
                attempt.Id.Value,
                nodesById,
                nowUtc);
            if (!access.IsSuccess)
            {
                return RuntimeSnapshotFailure(
                    "A durable Evidence record failed its task/user/scope integrity check.");
            }

            AgentEvidenceEnvelopeDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<AgentEvidenceEnvelopeDocument>(
                    record.CanonicalEnvelopeJson,
                    CanonicalJson.SerializerOptions);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return RuntimeSnapshotFailure(
                    "A durable Evidence envelope could not be decoded safely.");
            }

            if (document is null)
            {
                return RuntimeSnapshotFailure("A durable Evidence envelope is missing.");
            }

            evidence.Add(MapEvidence(document));
            if (!string.IsNullOrWhiteSpace(document.Lineage.EvidenceSetDigest))
            {
                latestLineageEvidenceSetDigest = document.Lineage.EvidenceSetDigest;
                if (document.Producer.NodeKind is "ArtifactGenerationNode" or "ApprovalCheckpointNode")
                {
                    latestArtifactEvidenceSetDigest = document.Lineage.EvidenceSetDigest;
                }
            }
        }

        var nodeDtos = nodes
            .OrderBy(node => node.CreatedAt)
            .ThenBy(node => node.NodeId, StringComparer.Ordinal)
            .Select(node => MapNode(node, nowUtc))
            .ToArray();
        var cumulativeEvidenceSetDigest = evidenceRecords.Count == 0
            ? null
            : CanonicalJson.ComputeSha256(CanonicalJson.Serialize(
                evidenceRecords
                    .Select(record => record.EnvelopeDigest)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray()));
        var evidenceSetDigest = latestArtifactEvidenceSetDigest ??
                                latestLineageEvidenceSetDigest ??
                                cumulativeEvidenceSetDigest;
        return Result.Success(new AgentTaskRuntimeSnapshotDto(
            task.Id.Value,
            attempt.Id.Value,
            attempt.Status.ToString(),
            nowUtc,
            evidenceSetDigest,
            nodeDtos,
            evidence,
            BuildRuntimeMetrics(
                attempt,
                nodes,
                queueItems,
                workspace?.Artifacts ?? [],
                evidenceSetDigest)));
    }

    public async Task<Result<AgentTaskRunQueuePageDto>> GetRunQueueAsync(
        GetAgentTaskRunQueueQuery request,
        CancellationToken cancellationToken)
    {
        var taskResult = await LoadTaskAsync(request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var pagination = new Pagination
        {
            PageNumber = request.PageIndex,
            PageSize = request.PageSize
        };
        var queueItems = await queueStore.ListByTaskAsync(taskResult.Value!.Id, cancellationToken);
        var ordered = queueItems
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id.Value)
            .ToArray();
        var items = ordered
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(AgentTaskRunQueueItemDtoMapper.Map)
            .ToArray();
        var totalPages = (int)Math.Ceiling(ordered.Length / (double)pagination.PageSize);

        return Result.Success(new AgentTaskRunQueuePageDto(
            items,
            pagination.PageNumber,
            pagination.PageSize,
            ordered.Length,
            totalPages,
            pagination.PageNumber > 1,
            pagination.PageNumber < totalPages));
    }

    private static AgentRuntimeNodeDto MapNode(AgentNodeRun node, DateTimeOffset nowUtc)
    {
        var completedOrNow = node.CompletedAt ??
                             (node.Status is AgentNodeRunStatus.Running or AgentNodeRunStatus.Claimed
                                 ? nowUtc
                                 : null);
        var durationMs = node.StartedAt is { } startedAt && completedOrNow is { } endedAt
            ? Math.Max(0, (long)(endedAt - startedAt).TotalMilliseconds)
            : (long?)null;
        return new AgentRuntimeNodeDto(
            node.NodeId,
            ResolveNodeLabel(node.NodeKind, node.ToolCode),
            ResolveNodeKind(node.NodeKind),
            node.Status.ToString(),
            node.IsRequired,
            ReadDependencyCount(node.DependenciesJson),
            node.JoinPolicy,
            node.AttemptNo,
            node.MaxAttempts,
            Math.Max(0, node.AttemptNo - 1),
            node.TimeoutSeconds,
            durationMs,
            node.StartedAt,
            node.CompletedAt,
            node.FailureCode,
            ToolExecutionRecordSanitizer.Sanitize(node.SafeMessage, 1_000));
    }

    private static AgentRuntimeEvidenceDto MapEvidence(AgentEvidenceEnvelopeDocument document)
    {
        return new AgentRuntimeEvidenceDto(
            document.NodeId,
            ResolveNodeLabel(document.Producer.NodeKind, document.Producer.ToolCode),
            document.EvidenceKind,
            document.TruthClass,
            ResolveTruthLabel(document.TruthClass),
            ResolveSourceLabel(document.Source.Provider, document.Source.SourceMode),
            document.Source.SourceMode,
            document.Source.IsSimulation,
            document.Source.AsOfUtc,
            document.Source.TimeRange?.FromUtc,
            document.Source.TimeRange?.ToUtc,
            new AgentRuntimeEvidenceQualityDto(
                document.Quality.RowCount,
                document.Quality.IsTruncated,
                document.Quality.Freshness,
                document.Quality.MissingRate,
                document.Quality.Confidence,
                document.Quality.QualityFlags),
            ToolExecutionRecordSanitizer.Sanitize(document.Content.SafeSummary, 2_000) ??
                "Evidence summary is unavailable.",
            document.Content.Findings
                .Select(finding => ToolExecutionRecordSanitizer.Sanitize(finding, 500))
                .Where(finding => !string.IsNullOrWhiteSpace(finding))
                .Select(finding => finding!)
                .ToArray(),
            document.Content.TypedMetrics,
            document.Content.CitationRefs.Count);
    }

    private static IReadOnlyCollection<AgentRuntimeMetricDto> BuildRuntimeMetrics(
        AgentTaskRunAttempt attempt,
        IReadOnlyCollection<AgentNodeRun> nodes,
        IReadOnlyCollection<AgentTaskRunQueueItem> queueItems,
        IReadOnlyCollection<Artifact> artifacts,
        string? evidenceSetDigest)
    {
        var metrics = new List<AgentRuntimeMetricDto>();
        var queueItem = queueItems.FirstOrDefault(item => item.RunAttemptId == attempt.Id);
        var queueWaitMs = queueItem is { StartedAt: { } queueStarted }
            ? Math.Max(0, (long)(queueStarted - queueItem.CreatedAt).TotalMilliseconds)
            : (long?)null;
        metrics.Add(Metric(
            "queue_wait_ms",
            "排队等待",
            queueWaitMs,
            "ms",
            queueWaitMs.HasValue ? "Recorded" : "Pending",
            "RunQueueRecord"));

        var completedDurations = nodes
            .Where(node => node.StartedAt is not null && node.CompletedAt is not null)
            .Select(node => Math.Max(0, (long)(node.CompletedAt!.Value - node.StartedAt!.Value).TotalMilliseconds))
            .ToArray();
        metrics.Add(Metric(
            "node_duration_ms",
            "节点平均耗时",
            completedDurations.Length == 0 ? null : (decimal)completedDurations.Average(),
            "ms",
            completedDurations.Length == 0 ? "Pending" : "Recorded",
            "NodeRunRecord"));

        metrics.Add(Metric(
            "checkpoint_latency_ms",
            "检查点提交耗时",
            null,
            "ms",
            "ObservedByTelemetryOnly",
            "AICopilot.AgentRuntime"));

        AddCountMetric(metrics, "retry_count", "重试次数", nodes.Sum(node => Math.Max(0, node.AttemptNo - 1)));
        AddCountMetric(metrics, "outcome_unknown_count", "结果待核对", nodes.Count(node => node.Status == AgentNodeRunStatus.OutcomeUnknown));
        AddCountMetric(metrics, "required_node_failure_count", "必需节点失败", nodes.Count(node => node.IsRequired && node.Status == AgentNodeRunStatus.Failed));
        AddFailureCodeMetric(metrics, "claim_conflict_count", "领取冲突", CountFailureCodes(nodes, "claim", "conflict"));
        AddFailureCodeMetric(metrics, "lease_failure_count", "租约异常", CountFailureCodes(nodes, "lease"));
        AddFailureCodeMetric(metrics, "stale_worker_reject_count", "过期 Worker 拒绝", CountFailureCodes(nodes, "stale", "fence"));
        AddFailureCodeMetric(metrics, "evidence_normalization_failure_count", "证据规范化失败", CountFailureCodes(nodes, "evidence", "schema"));
        AddFailureCodeMetric(metrics, "evidence_access_reject_count", "证据访问拒绝", CountFailureCodes(nodes, "evidence", "access"));
        AddFailureCodeMetric(metrics, "budget_reject_count", "预算拒绝", CountFailureCodes(nodes, "budget"));

        AddCountMetric(metrics, "tool_call_count", "工具调用", attempt.BudgetConsumedToolCalls);
        AddCountMetric(metrics, "model_call_count", "模型调用", attempt.BudgetConsumedModelCalls);
        var modelUsageReported = attempt.BudgetConsumedModelCalls == 0 ||
                                 attempt.BudgetConsumedInputTokens > 0 ||
                                 attempt.BudgetConsumedOutputTokens > 0;
        metrics.Add(Metric(
            "input_tokens",
            "输入 Token",
            modelUsageReported ? attempt.BudgetConsumedInputTokens : null,
            "token",
            modelUsageReported ? "Recorded" : "ProviderUsageUnavailable",
            "RunAttemptBudgetLedger"));
        metrics.Add(Metric(
            "output_tokens",
            "输出 Token",
            modelUsageReported ? attempt.BudgetConsumedOutputTokens : null,
            "token",
            modelUsageReported ? "Recorded" : "ProviderUsageUnavailable",
            "RunAttemptBudgetLedger"));
        var costReported = attempt.BudgetConsumedModelCalls == 0 || attempt.BudgetConsumedCostAmount > 0;
        metrics.Add(Metric(
            "cost_amount",
            "记录成本",
            costReported ? attempt.BudgetConsumedCostAmount : null,
            attempt.BudgetCostCurrency,
            costReported ? "Recorded" : "ProviderUsageUnavailable",
            "RunAttemptBudgetLedger"));
        var artifactEvidenceDigests = artifacts
            .Where(artifact => !string.IsNullOrWhiteSpace(artifact.EvidenceSetDigest))
            .Select(artifact => artifact.EvidenceSetDigest!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var crossSurfaceRecorded = !string.IsNullOrWhiteSpace(evidenceSetDigest) &&
                                   artifactEvidenceDigests.Length > 0;
        var crossSurfaceConsistent = crossSurfaceRecorded &&
                                     artifactEvidenceDigests.All(digest =>
                                         string.Equals(digest, evidenceSetDigest, StringComparison.Ordinal));
        if (crossSurfaceRecorded)
        {
            AgentRuntimeTelemetry.RecordCrossSurfaceEvidenceConsistency(crossSurfaceConsistent);
        }

        metrics.Add(Metric(
            "chat_durable_evidence_consistency",
            "对话与持久证据一致",
            crossSurfaceRecorded ? (crossSurfaceConsistent ? 100m : 0m) : null,
            "%",
            crossSurfaceRecorded ? crossSurfaceConsistent ? "Recorded" : "Failed" : "NotRecorded",
            crossSurfaceRecorded ? "EvidenceLineage+ArtifactEvidenceSetDigest" : "NoAuthoritativeCrossSurfaceRecord"));
        return metrics;
    }

    private static IReadOnlyCollection<AgentRuntimeMetricDto> BuildNotStartedMetrics() =>
    [
        Metric("queue_wait_ms", "排队等待", null, "ms", "NotStarted", "RunQueueRecord"),
        Metric("retry_count", "重试次数", null, "count", "NotStarted", "NodeRunRecord"),
        Metric("outcome_unknown_count", "结果待核对", null, "count", "NotStarted", "NodeRunRecord")
    ];

    private static void AddCountMetric(
        ICollection<AgentRuntimeMetricDto> target,
        string code,
        string label,
        int value)
    {
        target.Add(Metric(code, label, value, "count", "Recorded", "RuntimeRecord"));
    }

    private static void AddFailureCodeMetric(
        ICollection<AgentRuntimeMetricDto> target,
        string code,
        string label,
        int value)
    {
        target.Add(Metric(
            code,
            label,
            value > 0 ? value : null,
            "count",
            value > 0 ? "DerivedFromRuntimeRecord" : "NotRecorded",
            "NodeRun.FailureCode"));
    }

    private static AgentRuntimeMetricDto Metric(
        string code,
        string label,
        decimal? value,
        string unit,
        string status,
        string source) =>
        new(code, label, value, unit, status, source);

    private static int CountFailureCodes(
        IEnumerable<AgentNodeRun> nodes,
        params string[] fragments)
    {
        return nodes.Count(node =>
            node.FailureCode is { } code && fragments.All(fragment =>
                code.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    private static int ReadDependencyCount(string dependenciesJson)
    {
        try
        {
            using var document = JsonDocument.Parse(dependenciesJson);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string ResolveNodeLabel(string nodeKind, string? toolCode = null) =>
        string.Equals(toolCode, "assess_cloud_health", StringComparison.Ordinal)
            ? "当前运行健康评估"
            : ResolveNodePresentation(nodeKind).Label;

    private static string ResolveNodeKind(string nodeKind) => ResolveNodePresentation(nodeKind).Kind;

    private static (string Label, string Kind) ResolveNodePresentation(string nodeKind) => nodeKind switch
    {
        "CloudReadNode" => ("Cloud 只读查询", "CloudRead"), "GovernedDataReadNode" => ("受控数据查询", "GovernedRead"),
        "KnowledgeRetrievalNode" => ("知识检索", "KnowledgeRead"), "FileAnalysisNode" => ("文件分析", "FileAnalysis"),
        "JoinNode" => ("证据汇合", "EvidenceJoin"), "AgentReasoningNode" => ("AI 证据推断", "AiInference"),
        "ArtifactGenerationNode" => ("结果产物生成", "Artifact"), "ApprovalCheckpointNode" => ("最终确认", "Approval"),
        "DeterministicComputeNode" => ("确定性计算", "DeterministicCompute"), _ => ("受控执行节点", "ControlledNode")
    };

    private static string ResolveTruthLabel(string truthClass) => truthClass switch
    {
        "ObservedFact" => "事实",
        "DerivedFact" => "确定性计算",
        "ModelPrediction" => "预测模型输出",
        "LlmInference" => "AI 推断",
        "Recommendation" => "建议",
        _ => "未分类证据"
    };

    private static string ResolveSourceLabel(string? provider, string sourceMode) => provider switch
    {
        "CloudAiRead" => "Cloud 只读数据",
        "GovernedTextToSql" or "GovernedDirectDb" => "受控业务数据",
        "ConfiguredModel" => "受控 AI 推断",
        "DeterministicHealthAlgorithm" => "固定健康评估算法",
        _ when sourceMode.Contains("Simulation", StringComparison.OrdinalIgnoreCase) => "Simulation 只读数据",
        _ => "受控任务数据"
    };

    private static Result<AgentTaskRuntimeSnapshotDto> RuntimeSnapshotFailure(string detail) =>
        Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentNodeRunStateConflict,
            detail));

    private Task<Result<AgentTask>> LoadTaskAsync(Guid id, CancellationToken cancellationToken)
    {
        return AgentTaskCommandLoader.LoadTaskAsync(
            taskRepository,
            currentUser,
            id,
            cancellationToken);
    }

    private async Task<ArtifactWorkspace?> LoadWorkspaceAsync(
        AgentTask task,
        CancellationToken cancellationToken,
        bool includeArtifacts = false)
    {
        if (task.WorkspaceId is null)
        {
            return null;
        }

        return await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts),
            cancellationToken);
    }

    private static bool BelongsToTask(
        AuditLogSummaryDto item,
        string taskId,
        string? workspaceCode)
    {
        if (string.Equals(item.TargetId, taskId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.Metadata.TryGetValue("taskId", out var metadataTaskId) &&
            string.Equals(metadataTaskId, taskId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(workspaceCode) &&
               item.Metadata.TryGetValue("workspaceCode", out var metadataWorkspaceCode) &&
               string.Equals(metadataWorkspaceCode, workspaceCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveWorkspaceCode(
        IReadOnlyDictionary<string, string> metadata,
        string? fallback)
    {
        return metadata.TryGetValue("workspaceCode", out var workspaceCode) &&
               !string.IsNullOrWhiteSpace(workspaceCode)
            ? workspaceCode
            : fallback;
    }

    private static AgentTaskAuditSummaryDto MapToolExecutionRecord(
        AgentTask task,
        string? workspaceCode,
        ToolExecutionRecord record)
    {
        var metadata = ParseAuditMetadata(record.AuditMetadata);
        metadata["toolExecutionId"] = record.Id.Value.ToString();
        metadata["stepId"] = record.StepId.Value.ToString();
        metadata["toolCode"] = record.ToolCode;
        metadata["status"] = record.Status.ToString();
        if (record.RunAttemptId is not null)
        {
            metadata["runAttemptId"] = record.RunAttemptId.Value.Value.ToString();
        }

        if (record.DurationMs.HasValue)
        {
            metadata["durationMs"] = record.DurationMs.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(record.ArtifactId))
        {
            metadata["artifactId"] = record.ArtifactId;
        }

        if (!string.IsNullOrWhiteSpace(record.ErrorCode))
        {
            metadata["errorCode"] = record.ErrorCode;
        }

        return new AgentTaskAuditSummaryDto(
            record.Id.Value,
            task.Id,
            ResolveWorkspaceCode(metadata, workspaceCode),
            "Agent.ToolExecutionRecord",
            "ToolExecutionRecord",
            record.ToolCode,
            record.Status.ToString(),
            BuildToolExecutionSummary(record),
            record.StartedAt.UtcDateTime,
            metadata);
    }

    private static AgentTaskAuditSummaryDto MapFailureSummary(
        AgentTask task,
        string? workspaceCode,
        AgentTaskFailureSummaryDto failureSummary)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["errorCode"] = failureSummary.ErrorCode,
            ["canRetry"] = failureSummary.CanRetry.ToString(),
            ["nextAction"] = failureSummary.NextAction
        };
        if (failureSummary.StepIndex.HasValue)
        {
            metadata["stepIndex"] = failureSummary.StepIndex.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(failureSummary.ToolCode))
        {
            metadata["toolCode"] = failureSummary.ToolCode!;
        }

        if (!string.IsNullOrWhiteSpace(workspaceCode))
        {
            metadata["workspaceCode"] = workspaceCode!;
        }

        return new AgentTaskAuditSummaryDto(
            Guid.NewGuid(),
            task.Id,
            workspaceCode,
            "Agent.FailureSummary",
            "AgentTask",
            task.TaskCode,
            task.Status.ToString(),
            ToolExecutionRecordSanitizer.Sanitize(failureSummary.SafeMessage, 2000) ?? "Agent task failed.",
            (task.CompletedAt ?? task.UpdatedAt).UtcDateTime,
            metadata);
    }

    private static string BuildToolExecutionSummary(ToolExecutionRecord record)
    {
        var message = record.Status switch
        {
            ToolExecutionStatus.Succeeded => record.OutputSummary,
            ToolExecutionStatus.Failed or ToolExecutionStatus.Rejected => record.ErrorMessage,
            _ => record.InputSummary
        };
        return ToolExecutionRecordSanitizer.Sanitize(message, 1000) ??
               $"Controlled tool execution ended with status {record.Status}.";
    }

    private static Dictionary<string, string> ParseAuditMetadata(string? auditMetadata)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(auditMetadata))
        {
            return metadata;
        }

        try
        {
            using var document = JsonDocument.Parse(auditMetadata);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                metadata["metadataParseStatus"] = "not_object";
                return metadata;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                var sanitized = ToolExecutionRecordSanitizer.Sanitize(value, 500);
                if (!string.IsNullOrWhiteSpace(sanitized))
                {
                    metadata[property.Name] = sanitized;
                }
            }
        }
        catch (JsonException)
        {
            metadata["metadataParseStatus"] = "invalid_json";
        }

        return metadata;
    }
}
