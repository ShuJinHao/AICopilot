using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Agents;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AgentWorkflowTestKit;

public sealed record AgentPlanV2TestStep(
    string Title,
    string Description,
    AgentStepType StepType,
    string ToolCode,
    bool RequiresApproval = false,
    string? InputJson = null);

public static class AgentPlanV2TestData
{
    public static RuntimeAgentConfigurationSnapshot RoutingConfiguration { get; } = new(
        Guid.Parse("99999999-9999-4999-8999-999999999999"),
        "IntentRoutingAgent",
        "builtin:1",
        new string('a', 64),
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
        "routing-test-model",
        "TestProvider",
        "OpenAICompatible",
        new string('b', 64),
        32_768,
        512,
        0);

    public static IAgentRoutingConfigurationSnapshotReader CreateMatchingRoutingSnapshotReader()
    {
        return new MatchingRoutingSnapshotReader();
    }

    public static AgentTaskPlanFreshReadGate CreateMatchingFreshReadGate()
    {
        return new AgentTaskPlanFreshReadGate(
            new MatchingAgentTaskPlanFreshReadVerifier(),
            new AgentPlanCanonicalizer());
    }

    /// <summary>
    /// Test-only downstream runtime harness. P0 production deliberately has no
    /// trusted PlanCompiler and remains fail-closed; focused runtime component
    /// tests use this gate only after arranging the exact tracked Step contract.
    /// It is never registered in production composition.
    /// </summary>
    public static AgentTaskPlanFreshReadGate CreateDownstreamRuntimeHarnessFreshReadGate(
        IAgentTaskPlanFreshReadVerifier? freshReadVerifier = null)
    {
        return new AgentTaskPlanFreshReadGate(
            freshReadVerifier ?? new MatchingAgentTaskPlanFreshReadVerifier(),
            CreateDownstreamRuntimeHarnessIntegrityValidator());
    }

    /// <summary>
    /// Test-only P2 downstream boundary. The validator still delegates every
    /// canonical/digest/schema/snapshot check to the production P0 authority and
    /// only projects the caller's executable requirement after that validation.
    /// It must never be registered by a production composition root.
    /// </summary>
    public static IAgentPlanIntegrityValidator CreateDownstreamRuntimeHarnessIntegrityValidator()
    {
        return new DownstreamRuntimeHarnessIntegrityValidator(new AgentPlanCanonicalizer());
    }

    public static string CreateSingleStep(
        string toolCode,
        bool executable = false,
        bool requiresApproval = false,
        string? inputJson = null,
        AgentTaskType taskType = AgentTaskType.ReportGeneration,
        IReadOnlyCollection<Guid>? knowledgeBaseIds = null)
    {
        return Create(
            [new AgentPlanV2TestStep(
                "生成图表数据",
                "生成图表数据。",
                AgentStepType.ChartGeneration,
                toolCode,
                requiresApproval,
                InputJson: inputJson)],
            executable,
            taskType,
            knowledgeBaseIds);
    }

    public static string CreateCloud(bool executable = false)
    {
        return Create(
            [
                new AgentPlanV2TestStep(
                    "Read Cloud",
                    "Read Cloud readonly data.",
                    AgentStepType.DataQuery,
                    "query_cloud_data_readonly",
                    true),
                new AgentPlanV2TestStep(
                    "Generate Markdown",
                    "Generate markdown report.",
                    AgentStepType.ArtifactGeneration,
                    "generate_markdown_report")
            ],
            executable,
            AgentTaskType.CloudDataReport,
            knowledgeBaseIds: null);
    }

    public static string CreateRag(Guid knowledgeBaseId, bool executable = false)
    {
        return Create(
            [
                new AgentPlanV2TestStep(
                    "Search RAG",
                    "Search admin-visible knowledge base.",
                    AgentStepType.RagSearch,
                    "rag_search"),
                new AgentPlanV2TestStep(
                    "Generate Markdown",
                    "Generate a governed summary of the retrieved knowledge.",
                    AgentStepType.ArtifactGeneration,
                    "generate_markdown_report")
            ],
            executable,
            AgentTaskType.DataAnalysis,
            [knowledgeBaseId]);
    }

    public static string Create(
        IReadOnlyCollection<AgentPlanV2TestStep> steps,
        bool executable,
        AgentTaskType taskType,
        IReadOnlyCollection<Guid>? knowledgeBaseIds)
    {
        return CreateCore(
            steps,
            executable,
            taskType,
            knowledgeBaseIds,
            requireCanonicalBuiltInCatalog: false);
    }

    /// <summary>
    /// Creates a canonical PlanDraft from the real built-in registration owner
    /// and the production planner catalog projection. This is the only Plan
    /// fixture suitable for HTTP tests that seed built-in runtime steps.
    /// </summary>
    public static string CreateCanonicalBuiltInPlanDraft(
        IReadOnlyCollection<AgentPlanV2TestStep> steps,
        AgentTaskType taskType,
        IReadOnlyCollection<Guid>? knowledgeBaseIds = null)
    {
        return CreateCore(
            steps,
            executable: false,
            taskType,
            knowledgeBaseIds,
            requireCanonicalBuiltInCatalog: true);
    }

    /// <summary>
    /// Projects every sealed Plan v2 execution step into the aggregate test fixture.
    /// This keeps persistence-bound tests byte-for-byte aligned with the canonical
    /// Plan owner, including contract-owned steps that the fixture did not request.
    /// </summary>
    public static IReadOnlyList<AgentStep> AddTrackedPlanSteps(
        AgentTask task,
        string canonicalPlanJson,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(task);
        var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(canonicalPlanJson, AgentRuntimeJson.Options)
            ?? throw new InvalidOperationException("The canonical Plan v2 fixture is required.");

        return plan.Steps
            .Select(step => task.AddStep(
                step.Title,
                step.Description,
                step.StepType,
                step.ToolCode,
                step.RequiresApproval,
                nowUtc,
                step.InputJson))
            .ToArray();
    }

    public static void AssertCanonicalBuiltInPlanIdentity(
        string planJson,
        AgentTaskType taskType,
        AgentTaskRiskLevel riskLevel,
        IReadOnlyCollection<AgentPlanV2TestStep> requestedSteps)
    {
        var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(planJson, AgentRuntimeJson.Options)
            ?? throw new InvalidOperationException("The canonical built-in PlanDraft fixture is required.");
        var expectedSteps = requestedSteps
            .Append(new AgentPlanV2TestStep(
                "Finalize artifacts",
                "Wait for final output approval before publishing workspace artifacts.",
                AgentStepType.Finalize,
                BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                RequiresApproval: true))
            .ToArray();
        var expectedCatalog = BuildCanonicalBuiltInCatalog(expectedSteps);
        if (!string.Equals(plan.TaskType, taskType.ToString(), StringComparison.Ordinal) ||
            !string.Equals(plan.RiskLevel, riskLevel.ToString(), StringComparison.Ordinal) ||
            plan.Steps.Count != expectedSteps.Length ||
            plan.Steps.Zip(expectedSteps).Any(pair =>
                !string.Equals(pair.First.Title, pair.Second.Title, StringComparison.Ordinal) ||
                !string.Equals(pair.First.Description, pair.Second.Description, StringComparison.Ordinal) ||
                pair.First.StepType != pair.Second.StepType ||
                !string.Equals(pair.First.ToolCode, pair.Second.ToolCode, StringComparison.Ordinal) ||
                pair.First.RequiresApproval != pair.Second.RequiresApproval ||
                !string.Equals(
                    pair.First.InputJson,
                    pair.Second.InputJson is null ? null : CanonicalJson.Canonicalize(pair.Second.InputJson),
                    StringComparison.Ordinal)) ||
            plan.Steps.Count(step => step.StepType == AgentStepType.Finalize) != 1 ||
            plan.Steps.Last().StepType != AgentStepType.Finalize ||
            !string.Equals(
                plan.Steps.Last().ToolCode,
                BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                StringComparison.Ordinal) ||
            plan.ExecutionSnapshot is null ||
            !AgentPlanCatalogSnapshotAuthority.Matches(
                plan.ExecutionSnapshot,
                expectedCatalog,
                RoutingConfiguration,
                plan.DataSourceIds,
                plan.KnowledgeBaseIds,
                plan.IntentCandidates))
        {
            throw new InvalidOperationException(
                "The canonical built-in PlanDraft must exactly match the aggregate task/risk/step contract.");
        }
    }

    private static string CreateCore(
        IReadOnlyCollection<AgentPlanV2TestStep> steps,
        bool executable,
        AgentTaskType taskType,
        IReadOnlyCollection<Guid>? knowledgeBaseIds,
        bool requireCanonicalBuiltInCatalog)
    {
        var isCloud = taskType == AgentTaskType.CloudDataReport;
        var isKnowledge = steps.Any(step => string.Equals(step.ToolCode, "rag_search", StringComparison.Ordinal));
        var capabilityCode = isCloud
            ? "Analysis.Device.List"
            : isKnowledge
                ? "Knowledge.Retrieve"
                : "General.Chat";
        var capabilityClass = isCloud
            ? AgentIntentClass.CloudOnly
            : isKnowledge
                ? AgentIntentClass.Knowledge
                : AgentIntentClass.General;
        var providerCode = isCloud
            ? "CloudAiRead"
            : isKnowledge
                ? "KnowledgeBase"
                : "BuiltIn";
        var canonicalKnowledgeBaseIds = (knowledgeBaseIds ?? [])
            .Distinct()
            .OrderBy(id => id.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        var candidate = new AgentIntentCandidateDocument(
            AgentPlanContractVersions.IntentV1,
            capabilityCode,
            capabilityClass,
            AgentIntentAvailability.Available,
            providerCode,
            1,
            new AgentIntentRequiredDocument(true, AgentIntentRequiredSource.ExplicitUserGoal, null),
            new AgentIntentRequestedResourcesDocument(
                [],
                [],
                isKnowledge ? canonicalKnowledgeBaseIds : [],
                []),
            new AgentIntentFiltersDocument(null, []),
            [],
            new AgentIntentProvenanceDocument(
                AgentIntentRegistryV1.RouterVersion,
                AgentIntentRegistryV1.PromptVersion,
                AgentIntentRegistryV1.RegistryVersion,
                AgentIntentRegistryV1.RegistryDigest),
            null);
        var requestedStepArray = steps.ToArray();
        var artifactTargets = requestedStepArray
            .Select(step => step.ToolCode switch
            {
                "generate_chart_data" or "generate_business_chart" => "chart",
                "generate_markdown_report" => "markdown",
                "generate_html_report" => "html",
                "generate_pdf" => "pdf",
                "generate_pptx" => "pptx",
                "generate_xlsx" => "xlsx",
                _ => null
            })
            .Where(target => target is not null)
            .Select(target => target!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(target => target, StringComparer.Ordinal)
            .ToArray();
        var stepArray = artifactTargets.Length > 0 &&
                        requestedStepArray.All(step => !string.Equals(
                            step.ToolCode,
                            BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                            StringComparison.Ordinal))
            ? requestedStepArray
                .Append(new AgentPlanV2TestStep(
                    "Finalize artifacts",
                    "Wait for final output approval before publishing workspace artifacts.",
                    AgentStepType.Finalize,
                    BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                    RequiresApproval: true))
                .ToArray()
            : requestedStepArray;
        var toolCatalog = requireCanonicalBuiltInCatalog
            ? BuildCanonicalBuiltInCatalog(stepArray)
            : BuildSyntheticComponentCatalog(stepArray);
        if (requireCanonicalBuiltInCatalog)
        {
            AssertCanonicalStepCatalogIdentity(stepArray, toolCatalog);
        }
        var cloudIntent = isCloud
            ? AgentTaskPlanCloudReadonlyIntentDocument.From(
                CloudReadonlyAgentPlanIntent.FromSemanticPlan(
                    new SemanticQueryPlan(
                        "Analysis.Device.List",
                        SemanticQueryTarget.Device,
                        SemanticQueryKind.List,
                        QueryText: null,
                        new SemanticProjection(["id"]),
                        [],
                        null,
                        null,
                        20),
                    1))
            : null;
        var intentCandidates = new List<AgentIntentCandidateDocument> { candidate };
        if (executable &&
            artifactTargets.Length > 0 &&
            !string.Equals(capabilityCode, "General.Chat", StringComparison.Ordinal))
        {
            intentCandidates.Add(new AgentIntentCandidateDocument(
                AgentPlanContractVersions.IntentV1,
                "General.Chat",
                AgentIntentClass.General,
                AgentIntentAvailability.Available,
                "BuiltIn",
                1,
                new AgentIntentRequiredDocument(true, AgentIntentRequiredSource.ExplicitUserGoal, null),
                new AgentIntentRequestedResourcesDocument([], [], [], []),
                new AgentIntentFiltersDocument(null, []),
                artifactTargets,
                new AgentIntentProvenanceDocument(
                    AgentIntentRegistryV1.RouterVersion,
                    AgentIntentRegistryV1.PromptVersion,
                    AgentIntentRegistryV1.RegistryVersion,
                    AgentIntentRegistryV1.RegistryDigest),
                null));
        }

        var canonicalIntentCandidates = intentCandidates
            .OrderBy(item => item.IntentCode, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyCollection<AgentPlanNodeDocument> nodes = executable
            ? BuildExecutableComponentNodes(
                stepArray,
                canonicalIntentCandidates,
                toolCatalog,
                cloudIntent,
                canonicalKnowledgeBaseIds,
                artifactTargets)
            : [];
        var approvals = stepArray
            .Where(step => step.RequiresApproval)
            .Select(step => step.ToolCode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var riskLevel = requireCanonicalBuiltInCatalog
            ? AgentTaskPlanMetadataBuilder.DetermineRiskLevel(taskType)
            : AgentTaskRiskLevel.Low;
        var riskSummary = requireCanonicalBuiltInCatalog
            ? AgentTaskPlanMetadataBuilder.BuildToolRiskSummary(toolCatalog)
            : new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Low"] = stepArray.Length
            };
        var plan = new AgentTaskPlanDocument(
            Version: 2,
            PlannerTemplateCode: "agent_planner",
            Goal: $"{taskType} task; goalSha256={CanonicalJson.ComputeSha256("test task")}",
            TaskType: taskType.ToString(),
            RiskLevel: riskLevel.ToString(),
            UploadIds: [],
            KnowledgeBaseIds: canonicalKnowledgeBaseIds,
            CloudReadonlyIntents: cloudIntent is null ? [] : [cloudIntent],
            Steps: stepArray.Select(step => new AgentTaskPlanStepDocument(
                step.Title,
                step.Description,
                step.StepType,
                step.ToolCode,
                step.RequiresApproval,
                step.InputJson is null ? null : CanonicalJson.Canonicalize(step.InputJson))).ToArray(),
            RuntimeSettings: new AgentTaskPlanRuntimeSettingsDocument(30, 12000),
            PlannerModelId: RoutingConfiguration.ModelId,
            PlannerToolCatalogVersion: PlannerToolCatalog.CurrentVersion,
            PlannerAvailableToolCount: toolCatalog.AvailableToolCount,
            DataSourceIds: [],
            BusinessDomains: [],
            QueryMode: isCloud ? "CloudReadonly" : "TextToSql",
            RequiresDataApproval: false,
            PlannerSafetySummary: new AgentTaskPlanSafetySummaryDocument(
                "PlanV2Contract",
                PlannerToolCatalog.CurrentVersion,
                toolCatalog.AvailableToolCount,
                false,
                false,
                riskSummary,
                false),
            ForcedStepCodes: [],
            ApprovalCheckpoints: approvals,
            DataSourceSummaries: [],
            ToolCatalogVersion: PlannerToolCatalog.CurrentVersion,
            VisibleToolCount: toolCatalog.AvailableToolCount,
            ToolRiskSummary: riskSummary,
            MockMcpOnly: false,
            ToolApprovalCheckpoints: approvals,
            PlanKind: executable ? AgentTaskPlanKinds.ExecutablePlan : AgentTaskPlanKinds.PlanDraft,
            IsExecutable: executable,
            LifecycleSealPadding: executable ? string.Empty : "0000",
            CapabilityGaps: executable ? [] : [AgentPlanCapabilityGapCodes.PlanCompilerUnavailable],
            SchemaVersion: AgentPlanContractVersions.PlanV2,
            PlanId: Guid.NewGuid(),
            PlanVersion: 1,
            PlanDigest: null,
            TopologyProfile: "LinearV1",
            IntentCandidates: canonicalIntentCandidates,
            CapabilitySelectionMode: AgentCapabilitySelectionMode.InferredFromGoal,
            RequestedCapabilityCodes: canonicalIntentCandidates
                .Select(item => item.IntentCode)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            PluginSelectionMode: AgentPluginSelectionMode.BuiltInOnly,
            SelectedPluginIds: [],
            ArtifactTargets: artifactTargets,
            Nodes: nodes,
            JoinPolicies: [],
            Budgets: new AgentPlanBudgetDocument(
                AgentPlanContractVersions.BudgetPolicyV1,
                AgentPlanContractVersions.DefaultMaxNodes,
                AgentPlanContractVersions.DefaultMaxToolCalls,
                AgentPlanContractVersions.DefaultMaxModelCalls,
                AgentPlanContractVersions.DefaultMaxInputTokens,
                AgentPlanContractVersions.DefaultMaxOutputTokens,
                AgentPlanContractVersions.DefaultMaxElapsedSeconds,
                AgentPlanContractVersions.DefaultMaxCostAmount,
                AgentPlanContractVersions.DefaultCostCurrency,
                AgentPlanContractVersions.DefaultMaxRetries,
                AgentPlanContractVersions.DefaultMaxArtifactCount,
                AgentPlanContractVersions.DefaultMaxArtifactBytes,
                AgentPlanContractVersions.MaxPlanCanonicalBytes),
            ConcurrencyPolicy: new AgentPlanConcurrencyPolicyDocument(
                AgentPlanContractVersions.LinearConcurrencyPolicyV1,
                1),
            ApprovalSummary: new AgentPlanApprovalSummaryDocument(true, approvals),
            ExecutionSnapshot: AgentPlanCatalogSnapshotAuthority.CreateSnapshot(
                toolCatalog,
                RoutingConfiguration,
                [],
                canonicalKnowledgeBaseIds,
                canonicalIntentCandidates),
            SecuritySummary: new AgentPlanSecuritySummaryDocument(
                true,
                false,
                false,
                false,
                false,
                false));
        var canonicalizer = new AgentPlanCanonicalizer();
        var result = canonicalizer.Seal(plan);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Test {(executable ? "ExecutablePlan" : "PlanDraft")} fixture violates Plan v2: {string.Join(" | ", result.Errors ?? [])}");
        }

        return result.Value!.CanonicalJson;
    }

    /// <summary>
    /// Creates a canonical executable graph for isolated runtime component tests.
    /// Production plans remain owned by DeterministicAgentPlanCompiler; this helper
    /// only binds already selected, registry-allowed Steps to their frozen Node v1
    /// contracts so runtime tests cannot bypass the durable execution plane.
    /// </summary>
    private static IReadOnlyCollection<AgentPlanNodeDocument> BuildExecutableComponentNodes(
        IReadOnlyCollection<AgentPlanV2TestStep> steps,
        IReadOnlyCollection<AgentIntentCandidateDocument> candidates,
        PlannerToolCatalog toolCatalog,
        AgentTaskPlanCloudReadonlyIntentDocument? cloudIntent,
        IReadOnlyCollection<Guid> knowledgeBaseIds,
        IReadOnlyCollection<string> artifactTargets)
    {
        const long artifactBytesPerNode = 33_554_432;
        var tools = toolCatalog.Tools.ToDictionary(tool => tool.ToolCode, StringComparer.Ordinal);
        var candidateCodes = candidates.Select(candidate => candidate.IntentCode).ToHashSet(StringComparer.Ordinal);
        var primaryCapability = candidates
            .FirstOrDefault(candidate => !string.Equals(
                candidate.IntentCode,
                "General.Chat",
                StringComparison.Ordinal))
            ?.IntentCode ?? "General.Chat";
        var nodes = new List<AgentPlanNodeDocument>(steps.Count);
        var previousNodeId = string.Empty;
        var index = 0;
        foreach (var step in steps)
        {
            index++;
            if (!tools.TryGetValue(step.ToolCode, out var tool))
            {
                throw new InvalidOperationException(
                    $"Executable component fixture tool '{step.ToolCode}' is absent from its frozen catalog.");
            }

            var isCloudRead = string.Equals(step.ToolCode, "query_cloud_data_readonly", StringComparison.Ordinal);
            var isKnowledgeRead = string.Equals(step.ToolCode, "rag_search", StringComparison.Ordinal);
            var isFinalization = string.Equals(
                step.ToolCode,
                BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                StringComparison.Ordinal);
            var isArtifact = step.ToolCode is
                "generate_business_chart" or "generate_chart_data" or "generate_markdown_report" or
                "generate_html_report" or "generate_pdf" or "generate_pptx" or "generate_xlsx";
            var capabilityCode = isCloudRead || isKnowledgeRead
                ? primaryCapability
                : candidateCodes.Contains("General.Chat")
                    ? "General.Chat"
                    : primaryCapability;
            var nodeKind = step.ToolCode switch
            {
                "query_cloud_data_readonly" => "CloudReadNode",
                "rag_search" => "KnowledgeRetrievalNode",
                "read_uploaded_file" or "parse_table_file" => "FileAnalysisNode",
                "finalize_artifacts" => "ApprovalCheckpointNode",
                "generate_business_chart" or "generate_chart_data" or "generate_markdown_report" or
                    "generate_html_report" or "generate_pdf" or "generate_pptx" or "generate_xlsx" =>
                    "ArtifactGenerationNode",
                _ => "DeterministicComputeNode"
            };
            var sideEffectClass = isArtifact || isFinalization ? "ArtifactDraftOnly" : "ReadOnly";
            var retryPolicy = sideEffectClass == "ReadOnly"
                ? new AgentPlanRetryPolicyDocument("retry-policy:v1", 2, "Exponential")
                : new AgentPlanRetryPolicyDocument("retry-policy:v1", 1, "None");
            var artifactCount = isFinalization ? Math.Max(1, artifactTargets.Count) : isArtifact ? 1 : 0;
            var nodeId = $"{index:00}-component-step";
            var dependencies = previousNodeId.Length == 0
                ? Array.Empty<string>()
                : [previousNodeId];
            nodes.Add(new AgentPlanNodeDocument(
                AgentPlanContractVersions.NodeV1,
                nodeId,
                nodeKind,
                dependencies,
                Required: true,
                "node-input:v1",
                $"evidence:{step.ToolCode}:v1",
                [step.ToolCode],
                [capabilityCode],
                [],
                isKnowledgeRead ? knowledgeBaseIds : [],
                dependencies,
                new AgentPlanNodeInputDocument(
                    SemanticIntent: isCloudRead ? cloudIntent?.Intent : null,
                    SemanticPlanDigest: isCloudRead ? cloudIntent?.SemanticPlanDigest : null,
                    TypedProvider: isCloudRead ? "CloudAiRead" : null,
                    RequestedScope: isCloudRead ? cloudIntent?.QueryScope ?? [] : [],
                    MaxRows: isCloudRead ? cloudIntent?.Limit : null,
                    ExecutionMode: null,
                    DataSourceId: null,
                    BusinessDomains: [],
                    GovernedSchemaDigest: null,
                    RequestedPermission: null,
                    CanonicalInputJson: step.InputJson is null ? null : CanonicalJson.Canonicalize(step.InputJson),
                    TimeRange: isCloudRead && cloudIntent?.TimeRange is { } timeRange
                        ? new AgentIntentTimeRangeDocument(timeRange.Start, timeRange.End, timeRange.TimeZone)
                        : null),
                ModelPolicy: null,
                new AgentPlanTimeoutPolicyDocument("timeout-policy:v1", tool.TimeoutSeconds),
                retryPolicy,
                new AgentPlanNodeBudgetDocument(
                    retryPolicy.MaxAttempts,
                    0,
                    0,
                    0,
                    isCloudRead ? cloudIntent?.Limit ?? 0 : 0,
                    0m,
                    artifactCount,
                    artifactBytesPerNode * artifactCount),
                new AgentPlanApprovalPolicyDocument(
                    step.RequiresApproval,
                    string.IsNullOrWhiteSpace(tool.ApprovalPolicy) ? "None" : tool.ApprovalPolicy),
                new AgentPlanIdempotencyPolicyDocument(
                    "idempotency-policy:v1",
                    sideEffectClass == "ReadOnly" ? "ReadOnly" : "Fenced"),
                sideEffectClass,
                JoinPolicy: null));
            previousNodeId = nodeId;
        }

        return nodes;
    }

    private static PlannerToolCatalog BuildCanonicalBuiltInCatalog(
        IReadOnlyCollection<AgentPlanV2TestStep> steps)
    {
        var registrations = steps
            .Select(step => step.ToolCode)
            .Distinct(StringComparer.Ordinal)
            .Select(toolCode => BuiltInToolRegistrations.FindAgentRuntimeTool(toolCode) ??
                throw new InvalidOperationException(
                    $"HTTP Plan v2 fixture tool '{toolCode}' is not owned by BuiltInToolRegistrations."))
            .Select(CreateCanonicalBuiltInRegistration)
            .ToArray();
        var result = PlannerToolCatalogBuilder.Build(
            registrations,
            new HashSet<string>(StringComparer.Ordinal));
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Canonical built-in planner catalog fixture is invalid: {string.Join(" | ", result.Errors ?? [])}");
        }

        var catalog = result.Value!;
        if (catalog.AvailableToolCount != registrations.Length)
        {
            throw new InvalidOperationException(
                "Canonical built-in planner catalog excluded a requested HTTP fixture tool.");
        }

        return catalog;
    }

    private static ToolRegistration CreateCanonicalBuiltInRegistration(ToolRegistrationSeed seed)
    {
        return new ToolRegistration(
            seed.ToolCode,
            seed.DisplayName,
            seed.Description,
            seed.ProviderType,
            seed.TargetType,
            seed.TargetName,
            seed.InputSchemaJson,
            seed.OutputSchemaJson,
            seed.RiskLevel,
            seed.RequiredPermission,
            seed.RequiresApproval,
            seed.IsEnabled,
            seed.TimeoutSeconds,
            seed.AuditLevel,
            DateTimeOffset.UnixEpoch,
            seed.Category,
            seed.BusinessDomains,
            seed.DataBoundary,
            seed.IsVisibleToPlanner,
            seed.IsExecutableByAgent,
            seed.SchemaVersion,
            seed.CatalogVersion,
            seed.ApprovalPolicy);
    }

    private static void AssertCanonicalStepCatalogIdentity(
        IReadOnlyCollection<AgentPlanV2TestStep> steps,
        PlannerToolCatalog catalog)
    {
        var tools = catalog.Tools.ToDictionary(tool => tool.ToolCode, StringComparer.Ordinal);
        foreach (var step in steps)
        {
            if (!tools.TryGetValue(step.ToolCode, out var tool))
            {
                throw new InvalidOperationException(
                    $"HTTP Plan v2 fixture step '{step.ToolCode}' is absent from the canonical catalog.");
            }

            if (step.RequiresApproval != tool.RequiresApproval)
            {
                throw new InvalidOperationException(
                    $"HTTP Plan v2 fixture step '{step.ToolCode}' approval does not match BuiltInToolRegistrations.");
            }
        }
    }

    private static PlannerToolCatalog BuildSyntheticComponentCatalog(
        IReadOnlyCollection<AgentPlanV2TestStep> steps)
    {
        var plannerTools = steps
            .Select(CreateSyntheticComponentTool)
            .ToArray();
        return new PlannerToolCatalog(
            PlannerToolCatalog.CurrentVersion,
            plannerTools.Length,
            plannerTools);
    }

    private static AgentPlannerToolSummary CreateSyntheticComponentTool(AgentPlanV2TestStep step)
    {
        var toolCode = step.ToolCode;
        var provider = toolCode switch
        {
            "query_cloud_data_readonly" => "CloudReadonly",
            "generate_business_chart" or "generate_chart_data" or "generate_markdown_report" or
                "generate_html_report" or "generate_pdf" or "generate_pptx" or "generate_xlsx" or
                "finalize_artifacts" => "Artifact",
            _ => "BuiltIn"
        };
        var builtIn = BuiltInToolRegistrations.FindAgentRuntimeTool(toolCode);
        var inputSchema = builtIn?.InputSchemaJson ??
            """{"type":"object","properties":{},"additionalProperties":false}""";
        var outputSchema = builtIn?.OutputSchemaJson ??
            """{"type":"object","properties":{},"additionalProperties":false}""";
        return new AgentPlannerToolSummary(
            toolCode,
            toolCode,
            toolCode,
            provider,
            builtIn?.TargetType.ToString() ?? "AgentRuntime",
            builtIn?.TargetName ?? "AgentTaskRuntime",
            inputSchema,
            step.RequiresApproval,
            "Low",
            ProviderKind: provider,
            OutputSchemaJson: outputSchema,
            SideEffectClass: provider == "Artifact" ? "ArtifactWrite" : "ReadOnly",
            InputSchemaHash: CanonicalJson.ComputeSha256(CanonicalJson.Canonicalize(inputSchema)),
            OutputSchemaHash: CanonicalJson.ComputeSha256(CanonicalJson.Canonicalize(outputSchema)));
    }

    private sealed class MatchingRoutingSnapshotReader : IAgentRoutingConfigurationSnapshotReader
    {
        public Task<RuntimeAgentConfigurationSnapshot> ReadCurrentAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(RoutingConfiguration);
        }
    }

    private sealed class DownstreamRuntimeHarnessIntegrityValidator(
        AgentPlanCanonicalizer productionCanonicalizer) : IAgentPlanIntegrityValidator
    {
        public Result<AgentPlanContractMetadata> ValidatePersisted(
            string planJson,
            bool requireExecutable = false)
        {
            var validation = productionCanonicalizer.ValidatePersisted(
                planJson,
                requireExecutable: false);
            if (!validation.IsSuccess)
            {
                return Result.From(validation);
            }

            var metadata = validation.Value!;
            return Result.Success(metadata with
            {
                IsExecutable = requireExecutable || metadata.IsExecutable
            });
        }

        public Result<IReadOnlyCollection<AgentTaskPlanExecutionStepContract>> ReadExecutionContract(
            string planJson,
            bool requireExecutable = false)
        {
            return productionCanonicalizer.ReadExecutionContract(
                planJson,
                requireExecutable: false);
        }
    }
}

public sealed class MatchingAgentTaskPlanFreshReadVerifier : IAgentTaskPlanFreshReadVerifier
{
    public Task<AgentTaskPlanFreshReadDecision> VerifyAsync(
        AgentTaskPlanFreshReadRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AgentTaskPlanFreshReadDecision.Match);
    }
}
