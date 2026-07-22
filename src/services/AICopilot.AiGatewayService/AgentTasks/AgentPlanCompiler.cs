using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentPlanCompilerRequest(
    AgentTaskType TaskType,
    IReadOnlyCollection<AgentIntentCandidateDocument> IntentCandidates,
    AgentCapabilitySelectionMode CapabilitySelectionMode,
    IReadOnlyCollection<string> RequestedCapabilityCodes,
    AgentPluginSelectionMode PluginSelectionMode,
    IReadOnlyCollection<Guid> SelectedPluginIds,
    PlannerToolCatalog ToolCatalog,
    IReadOnlyCollection<Guid> UploadIds,
    IReadOnlyCollection<Guid> KnowledgeBaseIds,
    AgentTaskPlanCloudReadonlyIntentDocument? CloudReadonlyIntent,
    IReadOnlyCollection<Guid> DataSourceIds,
    IReadOnlyCollection<string> BusinessDomains,
    IReadOnlyCollection<AgentTaskPlanDataSourceSummaryDocument> DataSourceSummaries,
    IReadOnlyCollection<string> ArtifactTargets,
    bool RequiresDataApproval,
    bool IsSimulationOnly,
    string DataContractDigest);

internal sealed record AgentPlanCompilation(
    IReadOnlyCollection<AgentTaskPlanStepDocument> Steps,
    IReadOnlyCollection<AgentPlanNodeDocument> Nodes,
    IReadOnlyCollection<string> CapabilityGaps)
{
    public bool IsExecutable => Nodes.Count > 0 && CapabilityGaps.Count == 0;
}

internal interface IAgentPlanCompiler
{
    AgentPlanCompilation Compile(AgentPlanCompilerRequest request);
}

/// <summary>
/// The only production PlanCompiler boundary. It compiles canonical, server-owned
/// IntentCandidate documents into a deterministic LinearV1 skeleton. It never calls a
/// model, Tool, Cloud, MCP, Worker, or mutable resource discovery surface.
/// </summary>
internal sealed class DeterministicLinearAgentPlanCompiler : IAgentPlanCompiler
{
    public const string CompilerVersion = "linear-plan-compiler:v1";

    private const long ArtifactBytesPerNode = 33_554_432;

    private static readonly string[] ArtifactTargetOrder =
        ["chart", "markdown", "html", "pdf", "pptx", "xlsx"];

    private static readonly IReadOnlyDictionary<string, string> ArtifactTools =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chart"] = "generate_chart_data",
            ["markdown"] = "generate_markdown_report",
            ["html"] = "generate_html_report",
            ["pdf"] = "generate_pdf",
            ["pptx"] = "generate_pptx",
            ["xlsx"] = "generate_xlsx"
        };

    public AgentPlanCompilation Compile(AgentPlanCompilerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gaps = new HashSet<string>(StringComparer.Ordinal);
        var candidates = request.IntentCandidates
            .OrderBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
            .ToArray();
        var available = candidates
            .Where(candidate => candidate.Availability == AgentIntentAvailability.Available &&
                                candidate.CapabilityGap is null)
            .ToArray();

        foreach (var gap in candidates
                     .Where(candidate => candidate.CapabilityGap is not null)
                     .Select(candidate => candidate.CapabilityGap!.Code))
        {
            gaps.Add(gap);
        }

        if (request.CapabilitySelectionMode == AgentCapabilitySelectionMode.ExplicitAllowlist &&
            request.RequestedCapabilityCodes.Count == 0)
        {
            gaps.Add(AgentPlanCapabilityGapCodes.CapabilitySelectionEmpty);
        }

        if (request.PluginSelectionMode != AgentPluginSelectionMode.BuiltInOnly ||
            request.SelectedPluginIds.Count != 0)
        {
            // P2 has no stable plugin-id-to-node contract. Failing closed here prevents
            // an Action intent or plugin choice from expanding the built-in tool view.
            gaps.Add(AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
        }

        if (available.Length == 0)
        {
            gaps.Add(AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
        }

        var toolCodes = new List<string>();
        if (request.UploadIds.Count != 0)
        {
            toolCodes.Add("read_uploaded_file");
            toolCodes.Add("parse_table_file");
        }

        var knowledgeCandidates = available
            .Where(candidate => candidate.IntentClass == AgentIntentClass.Knowledge)
            .ToArray();
        if (knowledgeCandidates.Length != 0)
        {
            if (request.KnowledgeBaseIds.Count == 0)
            {
                gaps.Add(AgentPlanCapabilityGapCodes.ResourceResolutionRequired);
            }
            else
            {
                toolCodes.Add("rag_search");
            }
        }

        var cloudCandidates = available
            .Where(candidate => candidate.IntentClass == AgentIntentClass.CloudOnly)
            .ToArray();
        if (cloudCandidates.Length != 0)
        {
            if (cloudCandidates.Length != 1 ||
                request.CloudReadonlyIntent is null ||
                !string.Equals(
                    request.CloudReadonlyIntent.Intent,
                    cloudCandidates[0].IntentCode,
                    StringComparison.Ordinal))
            {
                gaps.Add(AgentPlanCapabilityGapCodes.CloudReadonlyIntentUnavailable);
            }
            else
            {
                toolCodes.Add("query_cloud_data_readonly");
            }
        }

        var governedCandidates = available
            .Where(candidate => candidate.IntentClass == AgentIntentClass.GovernedExploration)
            .ToArray();
        if (governedCandidates.Length != 0)
        {
            if (governedCandidates.Length != 1 ||
                request.DataSourceIds.Count != 1 ||
                request.DataSourceSummaries.Count != 1 ||
                request.DataSourceSummaries.Single().Id != request.DataSourceIds.Single())
            {
                gaps.Add(AgentPlanCapabilityGapCodes.ResourceResolutionRequired);
            }
            else
            {
                toolCodes.Add("query_business_database_readonly");
                toolCodes.Add("summarize_business_query_result");
            }
        }

        // Policy is a real quick-Chat capability, but there is not yet a durable,
        // snapshot-bound PolicyValidation executor/tool. It remains an explicit gap
        // instead of being silently converted to a generic report step.
        if (available.Any(candidate => candidate.IntentClass == AgentIntentClass.Policy))
        {
            gaps.Add(AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
        }

        var targets = request.ArtifactTargets
            .Distinct(StringComparer.Ordinal)
            .OrderBy(target => Array.IndexOf(ArtifactTargetOrder, target))
            .ThenBy(target => target, StringComparer.Ordinal)
            .ToArray();
        if (targets.Length == 0 || targets.Any(target => !ArtifactTools.ContainsKey(target)))
        {
            gaps.Add(AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
        }
        else
        {
            foreach (var target in targets)
            {
                var artifactTool = target == "chart" && governedCandidates.Length != 0
                    ? "generate_business_chart"
                    : ArtifactTools[target];
                toolCodes.Add(artifactTool);
            }

            toolCodes.Add("finalize_artifacts");
        }

        if (toolCodes.Count > AgentPlanContractVersions.DefaultMaxNodes)
        {
            gaps.Add(AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
        }

        var tools = request.ToolCatalog.Tools
            .Where(tool => tool.RuntimeAvailable && tool.IsVisibleToPlanner && tool.IsExecutableByAgent)
            .ToDictionary(tool => tool.ToolCode, StringComparer.Ordinal);
        if (toolCodes.Any(toolCode => !tools.ContainsKey(toolCode)))
        {
            gaps.Add(AgentPlanCapabilityGapCodes.PlannedToolUnavailable);
        }

        if (request.IsSimulationOnly &&
            (request.DataSourceIds.Count != 1 ||
             request.DataSourceSummaries.Count != 1 ||
             !request.DataSourceSummaries.Single().IsSimulation ||
             !string.Equals(
                 request.DataSourceSummaries.Single().SourceMode,
                 DataSourceExternalSystemType.SimulationBusiness.ToString(),
                 StringComparison.Ordinal) ||
             cloudCandidates.Length != 0))
        {
            gaps.Add(AgentPlanCapabilityGapCodes.ResourceResolutionRequired);
        }

        if (gaps.Count != 0)
        {
            return Gap(gaps);
        }

        var steps = new List<AgentTaskPlanStepDocument>(toolCodes.Count);
        var nodes = new List<AgentPlanNodeDocument>(toolCodes.Count);
        foreach (var toolCode in toolCodes)
        {
            var tool = tools[toolCode];
            var nodeId = $"linear-node-{nodes.Count + 1:00}";
            var previousNodeId = nodes.Count == 0 ? null : nodes[^1].NodeId;
            var nodeKind = ResolveNodeKind(toolCode);
            var capabilityCodes = ResolveCapabilityCodes(toolCode, available);
            if (capabilityCodes.Length == 0)
            {
                gaps.Add(AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
                break;
            }

            var isCloudRead = string.Equals(toolCode, "query_cloud_data_readonly", StringComparison.Ordinal);
            var isGovernedRead = string.Equals(toolCode, "query_business_database_readonly", StringComparison.Ordinal);
            var isFinalization = string.Equals(toolCode, "finalize_artifacts", StringComparison.Ordinal);
            var isArtifactWrite = string.Equals(tool.SideEffectClass, "ArtifactWrite", StringComparison.Ordinal);
            var sideEffectClass = isArtifactWrite ? "ArtifactDraftOnly" : "ReadOnly";
            var dataSourceId = isGovernedRead ? request.DataSourceIds.Single() : (Guid?)null;
            var requestedScope = isCloudRead
                ? request.CloudReadonlyIntent!.QueryScope
                : isGovernedRead
                    ? [$"data-source:{dataSourceId:D}"]
                    : Array.Empty<string>();
            var artifactBudgetCount = isFinalization ? targets.Length : isArtifactWrite ? 1 : 0;
            var artifactBudgetBytes = artifactBudgetCount == 0
                ? 0
                : checked(ArtifactBytesPerNode * artifactBudgetCount);
            var requiresApproval = tool.RequiresApproval ||
                                   tool.RiskLevel is nameof(AiToolRiskLevel.RequiresApproval)
                                       or nameof(AiToolRiskLevel.High)
                                       or nameof(AiToolRiskLevel.Critical) ||
                                   isGovernedRead && request.RequiresDataApproval;

            steps.Add(new AgentTaskPlanStepDocument(
                tool.DisplayName,
                tool.Description,
                ResolveStepType(toolCode),
                toolCode,
                requiresApproval,
                InputJson: null));
            nodes.Add(new AgentPlanNodeDocument(
                AgentPlanContractVersions.NodeV1,
                nodeId,
                nodeKind,
                previousNodeId is null ? [] : [previousNodeId],
                Required: true,
                InputSchemaRef: "node-input:v1",
                OutputSchemaRef: $"evidence:{toolCode}:v1",
                RequestedToolCodes: [toolCode],
                RequestedCapabilityCodes: capabilityCodes,
                DataScopes: isGovernedRead ? [dataSourceId!.Value] : [],
                KnowledgeScopes: string.Equals(toolCode, "rag_search", StringComparison.Ordinal)
                    ? request.KnowledgeBaseIds
                    : [],
                EvidenceSelectors: previousNodeId is null ? [] : [previousNodeId],
                Input: new AgentPlanNodeInputDocument(
                    SemanticIntent: isCloudRead ? request.CloudReadonlyIntent!.Intent : null,
                    SemanticPlanDigest: isCloudRead ? request.CloudReadonlyIntent!.SemanticPlanDigest : null,
                    TypedProvider: isCloudRead ? "CloudAiRead" : null,
                    RequestedScope: requestedScope
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray(),
                    MaxRows: isCloudRead
                        ? request.CloudReadonlyIntent!.Limit
                        : isGovernedRead ? 200 : null,
                    ExecutionMode: isGovernedRead ? "TextToSql" : null,
                    DataSourceId: dataSourceId,
                    BusinessDomains: isGovernedRead
                        ? request.BusinessDomains.OrderBy(value => value, StringComparer.Ordinal).ToArray()
                        : [],
                    GovernedSchemaDigest: isGovernedRead ? request.DataContractDigest : null,
                    RequestedPermission: isGovernedRead ? tool.RequiredPermission : null,
                    CanonicalInputJson: null),
                ModelPolicy: null,
                new AgentPlanTimeoutPolicyDocument("timeout-policy:v1", tool.TimeoutSeconds),
                new AgentPlanRetryPolicyDocument("retry-policy:v1", 1, "None"),
                new AgentPlanNodeBudgetDocument(
                    MaxToolCalls: 1,
                    MaxModelCalls: 0,
                    MaxInputTokens: 0,
                    MaxOutputTokens: 0,
                    MaxRows: isCloudRead ? request.CloudReadonlyIntent!.Limit : isGovernedRead ? 200 : 0,
                    MaxCostAmount: 0m,
                    MaxArtifactCount: artifactBudgetCount,
                    MaxArtifactBytes: artifactBudgetBytes),
                new AgentPlanApprovalPolicyDocument(
                    requiresApproval,
                    string.IsNullOrWhiteSpace(tool.ApprovalPolicy) ? "None" : tool.ApprovalPolicy),
                new AgentPlanIdempotencyPolicyDocument(
                    "idempotency-policy:v1",
                    sideEffectClass == "ReadOnly" ? "ReadOnly" : "Fenced"),
                sideEffectClass,
                JoinPolicy: null));
        }

        if (gaps.Count != 0)
        {
            return Gap(gaps);
        }

        return new AgentPlanCompilation(steps, nodes, []);
    }

    private static string[] ResolveCapabilityCodes(
        string toolCode,
        IReadOnlyCollection<AgentIntentCandidateDocument> available)
    {
        IEnumerable<AgentIntentCandidateDocument> candidates = toolCode switch
        {
            "query_cloud_data_readonly" => available.Where(candidate =>
                candidate.IntentClass == AgentIntentClass.CloudOnly),
            "query_business_database_readonly" => available.Where(candidate =>
                candidate.IntentClass == AgentIntentClass.GovernedExploration),
            "rag_search" => available.Where(candidate =>
                candidate.IntentClass == AgentIntentClass.Knowledge),
            _ => available.Where(candidate => candidate.IntentClass is
                AgentIntentClass.General or AgentIntentClass.Knowledge)
        };

        // One least-privilege capability is sufficient to authorize a common
        // file/artifact node; every required domain-specific candidate already has its
        // own producer node above.
        var selected = candidates
            .OrderBy(candidate => candidate.IntentClass == AgentIntentClass.General ? 0 : 1)
            .ThenBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
            .FirstOrDefault();
        return selected is null ? [] : [selected.IntentCode];
    }

    private static string ResolveNodeKind(string toolCode) => toolCode switch
    {
        "read_uploaded_file" or "parse_table_file" => "FileAnalysisNode",
        "rag_search" => "KnowledgeRetrievalNode",
        "query_cloud_data_readonly" => "CloudReadNode",
        "query_business_database_readonly" => "GovernedDataReadNode",
        "summarize_business_query_result" => "DeterministicComputeNode",
        "generate_business_chart" or "generate_chart_data" or
        "generate_markdown_report" or "generate_html_report" or
        "generate_pdf" or "generate_pptx" or "generate_xlsx" => "ArtifactGenerationNode",
        "finalize_artifacts" => "ApprovalCheckpointNode",
        _ => throw new InvalidOperationException($"Tool '{toolCode}' has no LinearV1 NodeKind mapping.")
    };

    private static AgentStepType ResolveStepType(string toolCode) => toolCode switch
    {
        "read_uploaded_file" => AgentStepType.FileRead,
        "rag_search" => AgentStepType.RagSearch,
        "query_cloud_data_readonly" or "query_business_database_readonly" => AgentStepType.DataQuery,
        "parse_table_file" or "summarize_business_query_result" => AgentStepType.Analysis,
        "generate_business_chart" or "generate_chart_data" => AgentStepType.ChartGeneration,
        "generate_markdown_report" or "generate_html_report" or
        "generate_pdf" or "generate_pptx" or "generate_xlsx" => AgentStepType.ArtifactGeneration,
        "finalize_artifacts" => AgentStepType.Finalize,
        _ => throw new InvalidOperationException($"Tool '{toolCode}' has no AgentStepType mapping.")
    };

    private static AgentPlanCompilation Gap(IEnumerable<string> gaps) => new(
        [],
        [],
        gaps.Where(gap => !string.IsNullOrWhiteSpace(gap))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(gap => gap, StringComparer.Ordinal)
            .ToArray());
}
