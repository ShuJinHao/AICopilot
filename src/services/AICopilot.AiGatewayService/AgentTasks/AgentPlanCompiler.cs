using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
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
    IReadOnlyCollection<AgentTaskPlanCloudReadonlyIntentDocument> CloudReadonlyIntents,
    IReadOnlyCollection<Guid> DataSourceIds,
    IReadOnlyCollection<string> BusinessDomains,
    IReadOnlyCollection<AgentTaskPlanDataSourceSummaryDocument> DataSourceSummaries,
    IReadOnlyCollection<string> ArtifactTargets,
    bool RequiresDataApproval,
    bool IsSimulationOnly,
    string DataContractDigest,
    RuntimeAgentConfigurationSnapshot? ReasoningModelConfiguration);

internal sealed record AgentPlanCompilation(
    string TopologyProfile,
    AgentPlanConcurrencyPolicyDocument ConcurrencyPolicy,
    IReadOnlyCollection<AgentTaskPlanStepDocument> Steps,
    IReadOnlyCollection<AgentPlanNodeDocument> Nodes,
    IReadOnlyCollection<string> JoinPolicies,
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
/// IntentCandidate documents into deterministic LinearV1 or bounded DagV1 skeletons.
/// It never calls a model, Tool, Cloud, MCP, Worker, or mutable discovery surface.
/// </summary>
internal sealed class DeterministicAgentPlanCompiler : IAgentPlanCompiler
{
    public const string CompilerVersion = "deterministic-plan-compiler:v4";

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

        if (candidates.Any(candidate =>
                !AgentIntentRegistryV1.TryGetDescriptor(candidate.IntentCode, out var descriptor) ||
                candidate.IntentClass != descriptor.IntentClass ||
                !string.Equals(candidate.ProviderCode, descriptor.ProviderCode, StringComparison.Ordinal)))
        {
            gaps.Add(AgentPlanCapabilityGapCodes.UnknownIntent);
        }

        foreach (var candidate in candidates.Where(candidate => candidate.CapabilityGap is not null))
        {
            gaps.Add(candidate.IntentCode.StartsWith("Prediction.", StringComparison.Ordinal)
                ? AgentPlanCapabilityGapCodes.UnsupportedNodeKind
                : candidate.CapabilityGap!.Code);
        }

        if (request.CapabilitySelectionMode == AgentCapabilitySelectionMode.ExplicitAllowlist &&
            request.RequestedCapabilityCodes.Count == 0)
        {
            gaps.Add(AgentPlanCapabilityGapCodes.CapabilitySelectionEmpty);
        }

        if (request.PluginSelectionMode != AgentPluginSelectionMode.BuiltInOnly ||
            request.SelectedPluginIds.Count != 0)
        {
            gaps.Add(AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
        }

        if (available.Length == 0)
        {
            gaps.Add(AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
        }

        var knowledgeCandidates = available
            .Where(candidate => candidate.IntentClass == AgentIntentClass.Knowledge)
            .ToArray();
        if (knowledgeCandidates.Length != 0 && request.KnowledgeBaseIds.Count == 0)
        {
            gaps.Add(AgentPlanCapabilityGapCodes.ResourceResolutionRequired);
        }

        var cloudCandidates = available
            .Where(candidate => candidate.IntentClass == AgentIntentClass.CloudOnly)
            .ToArray();
        var expectedCloudIntentCodes = cloudCandidates
            .Select(candidate => candidate.IntentCode)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var actualCloudIntentCodes = request.CloudReadonlyIntents
            .Select(intent => intent.Intent)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!actualCloudIntentCodes.SequenceEqual(expectedCloudIntentCodes, StringComparer.Ordinal) ||
            request.CloudReadonlyIntents.Count != actualCloudIntentCodes.Distinct(StringComparer.Ordinal).Count())
        {
            gaps.Add(AgentPlanCapabilityGapCodes.CloudReadonlyIntentUnavailable);
        }

        var governedCandidates = available
            .Where(candidate => candidate.IntentClass == AgentIntentClass.GovernedExploration)
            .ToArray();
        if (governedCandidates.Length != 0 &&
            (governedCandidates.Length != 1 ||
             request.DataSourceIds.Count != 1 ||
             request.DataSourceSummaries.Count != 1 ||
             request.DataSourceSummaries.Single().Id != request.DataSourceIds.Single()))
        {
            gaps.Add(AgentPlanCapabilityGapCodes.ResourceResolutionRequired);
        }

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

        if (request.UploadIds.Count == 0 &&
            knowledgeCandidates.Length == 0 &&
            cloudCandidates.Length == 0 &&
            governedCandidates.Length == 0)
        {
            gaps.Add(AgentPlanCapabilityGapCodes.ResourceResolutionRequired);
        }

        if (gaps.Count != 0)
        {
            return Gap(gaps);
        }

        var tools = request.ToolCatalog.Tools
            .Where(tool => tool.RuntimeAvailable && tool.IsVisibleToPlanner && tool.IsExecutableByAgent)
            .ToDictionary(tool => tool.ToolCode, StringComparer.Ordinal);
        var specs = BuildNodeSpecs(
            request,
            available,
            knowledgeCandidates,
            cloudCandidates,
            governedCandidates,
            targets);
        if (specs.Any(spec => spec.NodeKind == "AgentReasoningNode") &&
            request.ReasoningModelConfiguration is null)
        {
            gaps.Add(AgentPlanCapabilityGapCodes.ExecutionSnapshotUnavailable);
            return Gap(gaps);
        }

        if (specs.Count > AgentPlanContractVersions.DefaultMaxNodes ||
            specs.Any(spec => !tools.ContainsKey(spec.ToolCode)))
        {
            gaps.Add(specs.Count > AgentPlanContractVersions.DefaultMaxNodes
                ? AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable
                : AgentPlanCapabilityGapCodes.PlannedToolUnavailable);
            return Gap(gaps);
        }

        var topologyProfile = RequiresDag(specs) ? "DagV1" : "LinearV1";
        var concurrencyPolicy = topologyProfile == "DagV1"
            ? new AgentPlanConcurrencyPolicyDocument(
                AgentPlanContractVersions.DagConcurrencyPolicyV1,
                AgentPlanContractVersions.DagMaxParallelism)
            : new AgentPlanConcurrencyPolicyDocument(
                AgentPlanContractVersions.LinearConcurrencyPolicyV1,
                1);
        var steps = new List<AgentTaskPlanStepDocument>(specs.Count);
        var nodes = new List<AgentPlanNodeDocument>(specs.Count);

        foreach (var spec in specs)
        {
            var tool = tools[spec.ToolCode];
            var capabilityCodes = ResolveCapabilityCodes(spec.ToolCode, available, spec.CapabilityCodes);
            if (capabilityCodes.Length == 0 ||
                capabilityCodes.Any(capabilityCode =>
                    !AgentIntentRegistryV1.TryGetDescriptor(capabilityCode, out var descriptor) ||
                    !descriptor.AllowedNodeKinds.Contains(spec.NodeKind, StringComparer.Ordinal) ||
                    !descriptor.AllowedToolCodes.Contains(spec.ToolCode, StringComparer.Ordinal)))
            {
                gaps.Add(AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
                break;
            }

            var isCloudRead =
                spec.ToolCode == "query_business_database_readonly" &&
                spec.CloudIntentCode is not null;
            var isCloudHealth = spec.ToolCode == "assess_cloud_health";
            var isCloudSemanticNode = isCloudRead || isCloudHealth;
            var isGovernedRead =
                spec.ToolCode == "query_business_database_readonly" &&
                !isCloudRead;
            var isFinalization = spec.ToolCode == "finalize_artifacts";
            var isReasoning = spec.NodeKind == "AgentReasoningNode";
            var cloudIntent = spec.CloudIntentCode is null
                ? null
                : request.CloudReadonlyIntents.SingleOrDefault(intent =>
                    string.Equals(intent.Intent, spec.CloudIntentCode, StringComparison.Ordinal));
            if (isCloudSemanticNode && cloudIntent is null)
            {
                gaps.Add(AgentPlanCapabilityGapCodes.CloudReadonlyIntentUnavailable);
                break;
            }

            var isArtifactWrite = string.Equals(tool.SideEffectClass, "ArtifactWrite", StringComparison.Ordinal);
            var sideEffectClass = spec.NodeKind is "JoinNode" or "DeterministicComputeNode"
                ? "DeterministicInternal"
                : isArtifactWrite ? "ArtifactDraftOnly" : "ReadOnly";
            var dataSourceId = isGovernedRead ? request.DataSourceIds.Single() : (Guid?)null;
            var requestedScope = isCloudSemanticNode
                ? cloudIntent!.QueryScope
                : isGovernedRead
                    ? [$"data-source:{dataSourceId!.Value:D}"]
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
            var retryPolicy = isReasoning
                ? new AgentPlanRetryPolicyDocument("retry-policy:v1", 1, "None")
                : ResolveRetryPolicy(sideEffectClass);

            steps.Add(new AgentTaskPlanStepDocument(
                tool.DisplayName,
                tool.Description,
                ResolveStepType(spec.ToolCode),
                spec.ToolCode,
                requiresApproval,
                InputJson: null));
            nodes.Add(new AgentPlanNodeDocument(
                AgentPlanContractVersions.NodeV1,
                spec.NodeId,
                spec.NodeKind,
                AgentPlanCanonicalCollections.Strings(spec.DependsOn),
                spec.Required,
                "node-input:v1",
                $"evidence:{spec.ToolCode}:v1",
                [spec.ToolCode],
                capabilityCodes,
                isGovernedRead ? [dataSourceId!.Value] : [],
                spec.ToolCode == "rag_search" ? request.KnowledgeBaseIds : [],
                AgentPlanCanonicalCollections.Strings(spec.DependsOn),
                new AgentPlanNodeInputDocument(
                    SemanticIntent: isCloudSemanticNode ? cloudIntent!.Intent : null,
                    SemanticPlanDigest: isCloudSemanticNode ? cloudIntent!.SemanticPlanDigest : null,
                    TypedProvider: isCloudRead
                        ? "CloudAiRead"
                        : isCloudHealth ? "DeterministicHealthAssessment" : null,
                    RequestedScope: AgentPlanCanonicalCollections.Strings(requestedScope),
                    MaxRows: isCloudSemanticNode
                        ? cloudIntent!.Limit
                        : isGovernedRead ? 200 : null,
                    ExecutionMode: isGovernedRead ? "GovernedSql" : null,
                    DataSourceId: dataSourceId,
                    BusinessDomains: isGovernedRead
                        ? AgentPlanCanonicalCollections.Strings(request.BusinessDomains)
                        : [],
                    GovernedSchemaDigest: isGovernedRead ? request.DataContractDigest : null,
                    RequestedPermission: isGovernedRead ? tool.RequiredPermission : null,
                    CanonicalInputJson: null,
                    TimeRange: isCloudSemanticNode
                        ? ToEvidenceTimeRange(cloudIntent!.TimeRange)
                        : null),
                ModelPolicy: isReasoning
                    ? AgentReasoningPolicyAuthority.Create(request.ReasoningModelConfiguration!)
                    : null,
                new AgentPlanTimeoutPolicyDocument("timeout-policy:v1", tool.TimeoutSeconds),
                retryPolicy,
                new AgentPlanNodeBudgetDocument(
                    MaxToolCalls: retryPolicy.MaxAttempts,
                    MaxModelCalls: isReasoning
                        ? AgentReasoningPolicyAuthority.MaxTurns + AgentReasoningPolicyAuthority.RecoveryTurns
                        : 0,
                    MaxInputTokens: isReasoning ? AgentReasoningPolicyAuthority.MaxInputTokens : 0,
                    MaxOutputTokens: isReasoning ? AgentReasoningPolicyAuthority.MaxOutputTokens : 0,
                    MaxRows: isCloudSemanticNode ? cloudIntent!.Limit : isGovernedRead ? 200 : 0,
                    MaxCostAmount: isReasoning ? AgentReasoningPolicyAuthority.MaxCostAmount : 0m,
                    MaxArtifactCount: artifactBudgetCount,
                    MaxArtifactBytes: artifactBudgetBytes),
                new AgentPlanApprovalPolicyDocument(
                    requiresApproval,
                    string.IsNullOrWhiteSpace(tool.ApprovalPolicy) ? "None" : tool.ApprovalPolicy),
                new AgentPlanIdempotencyPolicyDocument(
                    "idempotency-policy:v1",
                    sideEffectClass switch
                    {
                        "ReadOnly" => "ReadOnly",
                        "DeterministicInternal" => "Deterministic",
                        _ => "Fenced"
                    }),
                sideEffectClass,
                spec.JoinPolicy));
        }

        if (gaps.Count != 0)
        {
            return Gap(gaps);
        }

        var joinPolicies = AgentPlanCanonicalCollections.Strings(
            nodes.Select(node => node.JoinPolicy).Where(policy => policy is not null).Select(policy => policy!));
        return new AgentPlanCompilation(
            topologyProfile,
            concurrencyPolicy,
            steps,
            nodes,
            joinPolicies,
            []);
    }

    private static List<NodeSpec> BuildNodeSpecs(
        AgentPlanCompilerRequest request,
        IReadOnlyCollection<AgentIntentCandidateDocument> available,
        IReadOnlyCollection<AgentIntentCandidateDocument> knowledgeCandidates,
        IReadOnlyCollection<AgentIntentCandidateDocument> cloudCandidates,
        IReadOnlyCollection<AgentIntentCandidateDocument> governedCandidates,
        IReadOnlyCollection<string> targets)
    {
        var specs = new List<NodeSpec>();
        var evidenceProducers = new List<string>();

        if (request.UploadIds.Count != 0)
        {
            specs.Add(new NodeSpec("01-file-read", "read_uploaded_file", "FileAnalysisNode", [], true, null, []));
            specs.Add(new NodeSpec("02-file-parse", "parse_table_file", "FileAnalysisNode", ["01-file-read"], true, null, []));
            evidenceProducers.Add("02-file-parse");
        }

        if (knowledgeCandidates.Count != 0)
        {
            specs.Add(new NodeSpec(
                "03-knowledge-read",
                "rag_search",
                "KnowledgeRetrievalNode",
                [],
                knowledgeCandidates.Any(candidate => candidate.Required.Value),
                null,
                knowledgeCandidates.Select(candidate => candidate.IntentCode).ToArray()));
            evidenceProducers.Add("03-knowledge-read");
        }

        if (cloudCandidates.Count != 0)
        {
            var cloudIndex = 0;
            foreach (var candidate in cloudCandidates.OrderBy(
                         candidate => candidate.IntentCode,
                         StringComparer.Ordinal))
            {
                cloudIndex++;
                var cloudReadNodeId = $"04-cloud-{cloudIndex:00}-read";
                specs.Add(new NodeSpec(
                    cloudReadNodeId,
                    "query_business_database_readonly",
                    "CloudReadNode",
                    [],
                    true,
                    null,
                    [candidate.IntentCode],
                    candidate.IntentCode));
                if (string.Equals(
                        candidate.IntentCode,
                        "Analysis.Device.Status",
                        StringComparison.Ordinal))
                {
                    var healthNodeId = $"04-cloud-{cloudIndex:00}-health";
                    specs.Add(new NodeSpec(
                        healthNodeId,
                        "assess_cloud_health",
                        "DeterministicComputeNode",
                        [cloudReadNodeId],
                        true,
                        null,
                        [candidate.IntentCode],
                        candidate.IntentCode));
                    evidenceProducers.Add(healthNodeId);
                }
                else
                {
                    evidenceProducers.Add(cloudReadNodeId);
                }
            }
        }

        if (governedCandidates.Count != 0)
        {
            specs.Add(new NodeSpec(
                "05-governed-read",
                "query_business_database_readonly",
                "GovernedDataReadNode",
                [],
                true,
                null,
                governedCandidates.Select(candidate => candidate.IntentCode).ToArray()));
            specs.Add(new NodeSpec(
                "06-governed-summary",
                "summarize_business_query_result",
                "DeterministicComputeNode",
                ["05-governed-read"],
                Required: false,
                JoinPolicy: null,
                governedCandidates.Select(candidate => candidate.IntentCode).ToArray()));
            evidenceProducers.Add("05-governed-read");
            evidenceProducers.Add("06-governed-summary");
        }

        string[] artifactDependencies;
        if (evidenceProducers.Count > 1)
        {
            var joinCapabilities = available
                .Where(candidate => candidate.IntentClass == AgentIntentClass.General)
                .Select(candidate => candidate.IntentCode)
                .ToArray();
            var joinPolicy = specs
                .Where(spec => evidenceProducers.Contains(spec.NodeId, StringComparer.Ordinal))
                .Any(spec => !spec.Required)
                ? "OptionalBestEffort"
                : "AllRequired";
            specs.Add(CreateEvidenceAggregationNode(
                "07-evidence-join", "join_evidence", "JoinNode", evidenceProducers, joinPolicy, joinCapabilities));
            specs.Add(CreateEvidenceAggregationNode(
                "08-agent-reasoning", "agent_reasoning", "AgentReasoningNode", evidenceProducers, joinPolicy, joinCapabilities));
            artifactDependencies = ["07-evidence-join", "08-agent-reasoning"];
        }
        else
        {
            artifactDependencies = evidenceProducers.ToArray();
        }

        var previousArtifact = string.Empty;
        var artifactIndex = 0;
        foreach (var target in targets)
        {
            artifactIndex++;
            var toolCode = target == "chart" && governedCandidates.Count != 0
                ? "generate_business_chart"
                : ArtifactTools[target];
            var dependencies = previousArtifact.Length == 0
                ? artifactDependencies
                : [previousArtifact];
            var nodeId = $"{20 + artifactIndex:00}-artifact-{target}";
            specs.Add(new NodeSpec(
                nodeId,
                toolCode,
                "ArtifactGenerationNode",
                dependencies,
                true,
                dependencies.Length > 1 ? "AllRequired" : null,
                []));
            previousArtifact = nodeId;
        }

        specs.Add(new NodeSpec(
            "99-finalize-artifacts",
            "finalize_artifacts",
            "ApprovalCheckpointNode",
            [previousArtifact],
            true,
            null,
            []));
        return specs;
    }

    private static AgentIntentTimeRangeDocument? ToEvidenceTimeRange(
        AgentTaskPlanSemanticTimeRangeDocument? timeRange) =>
        timeRange is null
            ? null
            : new AgentIntentTimeRangeDocument(
                timeRange.Start,
                timeRange.End,
                timeRange.TimeZone);

    private static NodeSpec CreateEvidenceAggregationNode(
        string nodeId,
        string capability,
        string nodeKind,
        IReadOnlyCollection<string> evidenceProducers,
        string joinPolicy,
        IReadOnlyCollection<string> capabilities)
    {
        return new NodeSpec(
            nodeId,
            capability,
            nodeKind,
            evidenceProducers,
            Required: true,
            joinPolicy,
            capabilities);
    }

    private static bool RequiresDag(IReadOnlyCollection<NodeSpec> specs)
    {
        if (specs.Any(spec => spec.DependsOn.Count > 1 || spec.JoinPolicy is not null))
        {
            return true;
        }

        var roots = specs.Count(spec => spec.DependsOn.Count == 0);
        if (roots > 1)
        {
            return true;
        }

        var childCounts = specs
            .SelectMany(spec => spec.DependsOn)
            .GroupBy(parent => parent, StringComparer.Ordinal)
            .Select(group => group.Count());
        return childCounts.Any(count => count > 1);
    }

    private static AgentPlanRetryPolicyDocument ResolveRetryPolicy(string sideEffectClass) =>
        sideEffectClass switch
        {
            "ReadOnly" => new AgentPlanRetryPolicyDocument("retry-policy:v1", 2, "Exponential"),
            "DeterministicInternal" => new AgentPlanRetryPolicyDocument("retry-policy:v1", 2, "Fixed"),
            _ => new AgentPlanRetryPolicyDocument("retry-policy:v1", 1, "None")
        };

    private static string[] ResolveCapabilityCodes(
        string toolCode,
        IReadOnlyCollection<AgentIntentCandidateDocument> available,
        IReadOnlyCollection<string> explicitCodes)
    {
        if (explicitCodes.Count != 0)
        {
            return AgentPlanCanonicalCollections.Strings(explicitCodes);
        }

        IEnumerable<AgentIntentCandidateDocument> candidates = toolCode switch
        {
            "assess_cloud_health" => available.Where(candidate => candidate.IntentClass == AgentIntentClass.CloudOnly),
            "query_business_database_readonly" or "summarize_business_query_result" => available.Where(candidate =>
                candidate.IntentClass == AgentIntentClass.GovernedExploration),
            "rag_search" => available.Where(candidate => candidate.IntentClass == AgentIntentClass.Knowledge),
            "join_evidence" or "agent_reasoning" => available,
            _ => available.Where(candidate => candidate.IntentClass is
                AgentIntentClass.General or AgentIntentClass.Knowledge or
                AgentIntentClass.CloudOnly or AgentIntentClass.GovernedExploration)
        };

        var selected = candidates
            .OrderBy(candidate => candidate.IntentClass == AgentIntentClass.General ? 0 : 1)
            .ThenBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
            .FirstOrDefault();
        return selected is null ? [] : [selected.IntentCode];
    }

    private static AgentStepType ResolveStepType(string toolCode) => toolCode switch
    {
        "read_uploaded_file" => AgentStepType.FileRead,
        "rag_search" => AgentStepType.RagSearch,
        "query_business_database_readonly" => AgentStepType.DataQuery,
        "parse_table_file" or "summarize_business_query_result" or "assess_cloud_health" or
        "join_evidence" or "agent_reasoning" => AgentStepType.Analysis,
        "generate_business_chart" or "generate_chart_data" => AgentStepType.ChartGeneration,
        "generate_markdown_report" or "generate_html_report" or
        "generate_pdf" or "generate_pptx" or "generate_xlsx" => AgentStepType.ArtifactGeneration,
        "finalize_artifacts" => AgentStepType.Finalize,
        _ => throw new InvalidOperationException($"Tool '{toolCode}' has no AgentStepType mapping.")
    };

    private static AgentPlanCompilation Gap(IEnumerable<string> gaps) => new(
        "LinearV1",
        new AgentPlanConcurrencyPolicyDocument(
            AgentPlanContractVersions.LinearConcurrencyPolicyV1,
            1),
        [],
        [],
        [],
        gaps.Where(gap => !string.IsNullOrWhiteSpace(gap))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(gap => gap, StringComparer.Ordinal)
            .ToArray());

    private sealed record NodeSpec(
        string NodeId,
        string ToolCode,
        string NodeKind,
        IReadOnlyCollection<string> DependsOn,
        bool Required,
        string? JoinPolicy,
        IReadOnlyCollection<string> CapabilityCodes,
        string? CloudIntentCode = null);
}
