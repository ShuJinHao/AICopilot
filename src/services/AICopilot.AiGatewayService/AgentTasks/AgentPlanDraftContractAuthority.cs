using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentPlanDraftContractRequest(
    string RawGoal,
    AgentTaskPlanDocument CurrentPlan,
    IReadOnlyCollection<IntentResult> RoutedIntents,
    AgentIntentAdapterContext IntentContext,
    PlannerToolCatalog ToolCatalog,
    AgentPluginSelectionMode? PluginSelectionMode,
    IReadOnlyCollection<Guid>? SelectedPluginIds,
    AgentCapabilitySelectionMode? CapabilitySelectionMode,
    IReadOnlyCollection<string>? RequestedCapabilityCodes,
    RuntimeAgentConfigurationSnapshot? RoutingConfiguration = null);

internal sealed record AgentPlanCatalogDigests(
    string ToolCatalogDigest,
    string ProviderCatalogDigest,
    string PluginCatalogDigest,
    string McpCatalogDigest);

internal static class AgentPlanCatalogSnapshotAuthority
{
    private const string DataContractVersion = "data-contract:v1";
    private const string KnowledgeContractVersion = "knowledge-contract:v1";
    private const string PolicyVersion = "agent-policy:v1";
    private const string GuardVersion = "agent-guard:v1";
    private const string BudgetPolicyVersion = "budget-policy:v1";
    private static readonly string DataContractDigest = HashContract(new
    {
        version = DataContractVersion,
        authorizedIdentity = "dataSourceId",
        selectionMode = "Agent",
        requiredFlags = new[] { "isEnabled", "isReadOnly", "isSelectableInAgent", "readOnlyCredentialVerified" },
        forbiddenPersistedFields = new[] { "connectionString", "credentials", "physicalSchema", "sql" }
    });
    private static readonly string KnowledgeContractDigest = HashContract(new
    {
        version = KnowledgeContractVersion,
        authorizedIdentity = "knowledgeBaseId",
        access = "CanReadAsync(userId,isAdmin)",
        forbiddenPersistedFields = new[] { "content", "credentials", "prompt", "retrievalText" }
    });
    private static readonly string PolicyDigest = HashContract(new
    {
        version = PolicyVersion,
        cloud = "readonly",
        device = "no-roster-fail-closed",
        pluginSelection = "BuiltInOnly",
        preConfirmationExecution = false
    });
    private static readonly string GuardDigest = HashContract(new
    {
        version = GuardVersion,
        gates = new[] { "capability-tool-intersection", "fresh-authorization", "plan-canonical", "tool-registry" },
        forbidden = new[] { "cloud-mutation", "plc-control", "raw-prompt", "secret", "sql" }
    });
    public static readonly string CanonicalEmptyInventoryDigest =
        CanonicalJson.ComputeSha256(CanonicalJson.Serialize(Array.Empty<string>()));

    public static AgentPlanCatalogDigests Compute(PlannerToolCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return new AgentPlanCatalogDigests(
            HashToolCatalog(catalog.Tools),
            HashProviderCatalog(catalog.Tools),
            CanonicalEmptyInventoryDigest,
            CanonicalEmptyInventoryDigest);
    }

    public static bool Matches(
        AgentExecutionSnapshotDocument snapshot,
        PlannerToolCatalog catalog,
        RuntimeAgentConfigurationSnapshot? routingConfiguration,
        IReadOnlyCollection<Guid>? authorizedDataSourceIds = null,
        IReadOnlyCollection<Guid>? authorizedKnowledgeBaseIds = null,
        IReadOnlyCollection<AgentIntentCandidateDocument>? authorizedIntentCandidates = null)
    {
        return snapshot == CreateSnapshot(
            catalog,
            routingConfiguration,
            authorizedDataSourceIds,
            authorizedKnowledgeBaseIds,
            authorizedIntentCandidates);
    }

    public static bool HasValidFrozenNonCatalogFields(
        AgentExecutionSnapshotDocument snapshot,
        Guid? modelId)
    {
        var baseContractValid = string.Equals(snapshot.SchemaVersion, AgentPlanContractVersions.ExecutionSnapshotV1, StringComparison.Ordinal) &&
               string.Equals(snapshot.PlanContractPolicyVersion, AgentPlanContractVersions.PlanPolicyV1, StringComparison.Ordinal) &&
               string.Equals(snapshot.PlanContractVersion, AgentPlanContractVersions.PlanV2, StringComparison.Ordinal) &&
               string.Equals(snapshot.PlanContractDigest, AgentPlanContractSchemaAuthority.PlanContractDigest, StringComparison.Ordinal) &&
               string.Equals(snapshot.NodeContractVersion, AgentPlanContractVersions.NodeV1, StringComparison.Ordinal) &&
               string.Equals(snapshot.NodeContractDigest, AgentPlanContractSchemaAuthority.NodeContractDigest, StringComparison.Ordinal) &&
               string.Equals(snapshot.IntentCatalogVersion, AgentIntentCatalogV1.CatalogVersion, StringComparison.Ordinal) &&
               string.Equals(snapshot.IntentCatalogDigest, AgentIntentCatalogV1.CatalogDigest, StringComparison.Ordinal) &&
               IsSha256(snapshot.AuthorizedIntentRosterDigest) &&
               snapshot.ModelId == modelId &&
               string.Equals(snapshot.DataContractVersion, DataContractVersion, StringComparison.Ordinal) &&
               string.Equals(snapshot.DataContractDigest, DataContractDigest, StringComparison.Ordinal) &&
               IsSha256(snapshot.AuthorizedDataSourceRosterDigest) &&
               string.Equals(snapshot.KnowledgeContractVersion, KnowledgeContractVersion, StringComparison.Ordinal) &&
               string.Equals(snapshot.KnowledgeContractDigest, KnowledgeContractDigest, StringComparison.Ordinal) &&
               IsSha256(snapshot.AuthorizedKnowledgeRosterDigest) &&
               string.Equals(snapshot.PolicyVersion, PolicyVersion, StringComparison.Ordinal) &&
               string.Equals(snapshot.PolicyDigest, PolicyDigest, StringComparison.Ordinal) &&
               string.Equals(snapshot.GuardVersion, GuardVersion, StringComparison.Ordinal) &&
               string.Equals(snapshot.GuardDigest, GuardDigest, StringComparison.Ordinal) &&
               string.Equals(snapshot.BudgetPolicyVersion, BudgetPolicyVersion, StringComparison.Ordinal) &&
               string.Equals(snapshot.ConcurrencyPolicyVersion, AgentPlanContractVersions.ConcurrencyPolicyV1, StringComparison.Ordinal) &&
               string.Equals(snapshot.PluginCatalogDigest, CanonicalEmptyInventoryDigest, StringComparison.Ordinal) &&
               string.Equals(snapshot.McpCatalogDigest, CanonicalEmptyInventoryDigest, StringComparison.Ordinal) &&
               snapshot.MaxCanonicalBytes == AgentPlanContractVersions.MaxPlanCanonicalBytes;
        if (!baseContractValid)
        {
            return false;
        }

        if (!modelId.HasValue)
        {
            return snapshot.PromptTemplateId is null &&
                   snapshot.PromptVersion is null &&
                   snapshot.PromptHash is null &&
                   snapshot.ModelProvider is null &&
                   snapshot.ModelProtocol is null &&
                   snapshot.ModelParametersHash is null &&
                   snapshot.ModelContextWindowTokens is null;
        }

        return !string.IsNullOrWhiteSpace(snapshot.PromptTemplateId) &&
               !string.IsNullOrWhiteSpace(snapshot.PromptVersion) &&
               IsSha256(snapshot.PromptHash) &&
               !string.IsNullOrWhiteSpace(snapshot.ModelProvider) &&
               !string.IsNullOrWhiteSpace(snapshot.ModelProtocol) &&
               IsSha256(snapshot.ModelParametersHash) &&
               snapshot.ModelContextWindowTokens is > 0 and <= 10_000_000;
    }

    public static AgentExecutionSnapshotDocument CreateSnapshot(
        PlannerToolCatalog catalog,
        RuntimeAgentConfigurationSnapshot? routingConfiguration,
        IReadOnlyCollection<Guid>? authorizedDataSourceIds = null,
        IReadOnlyCollection<Guid>? authorizedKnowledgeBaseIds = null,
        IReadOnlyCollection<AgentIntentCandidateDocument>? authorizedIntentCandidates = null)
    {
        var digests = Compute(catalog);
        return new AgentExecutionSnapshotDocument(
            AgentPlanContractVersions.ExecutionSnapshotV1,
            AgentPlanContractVersions.PlanPolicyV1,
            AgentPlanContractVersions.PlanV2,
            AgentPlanContractSchemaAuthority.PlanContractDigest,
            AgentPlanContractVersions.NodeV1,
            AgentPlanContractSchemaAuthority.NodeContractDigest,
            catalog.Version,
            digests.ToolCatalogDigest,
            digests.ProviderCatalogDigest,
            AgentIntentCatalogV1.CatalogVersion,
            AgentIntentCatalogV1.CatalogDigest,
            HashAuthorizedIntentRoster(authorizedIntentCandidates),
            routingConfiguration?.TemplateCode,
            routingConfiguration?.TemplateVersion,
            routingConfiguration?.PromptHash,
            routingConfiguration?.ModelId,
            routingConfiguration?.ModelProvider,
            routingConfiguration?.ModelProtocol,
            routingConfiguration?.ModelParametersHash,
            routingConfiguration?.ContextWindowTokens,
            digests.PluginCatalogDigest,
            digests.McpCatalogDigest,
            DataContractVersion,
            DataContractDigest,
            HashAuthorizedRoster(authorizedDataSourceIds),
            KnowledgeContractVersion,
            KnowledgeContractDigest,
            HashAuthorizedRoster(authorizedKnowledgeBaseIds),
            PolicyVersion,
            PolicyDigest,
            GuardVersion,
            GuardDigest,
            BudgetPolicyVersion,
            AgentPlanContractVersions.ConcurrencyPolicyV1,
            AgentPlanContractVersions.MaxPlanCanonicalBytes);
    }

    public static bool MatchesAuthorizedRosters(
        AgentExecutionSnapshotDocument snapshot,
        IReadOnlyCollection<Guid> dataSourceIds,
        IReadOnlyCollection<Guid> knowledgeBaseIds,
        IReadOnlyCollection<AgentIntentCandidateDocument> intentCandidates)
    {
        return string.Equals(
                   snapshot.AuthorizedDataSourceRosterDigest,
                   HashAuthorizedRoster(dataSourceIds),
                   StringComparison.Ordinal) &&
               string.Equals(
                   snapshot.AuthorizedKnowledgeRosterDigest,
                   HashAuthorizedRoster(knowledgeBaseIds),
                   StringComparison.Ordinal) &&
               string.Equals(
                   snapshot.AuthorizedIntentRosterDigest,
                   HashAuthorizedIntentRoster(intentCandidates),
                   StringComparison.Ordinal);
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static string HashAuthorizedRoster(IReadOnlyCollection<Guid>? ids)
    {
        var inventory = (ids ?? [])
            .Where(id => id != Guid.Empty)
            .Select(id => id.ToString("D"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        return CanonicalJson.ComputeSha256(CanonicalJson.Serialize(inventory));
    }

    private static string HashAuthorizedIntentRoster(
        IReadOnlyCollection<AgentIntentCandidateDocument>? candidates)
    {
        var inventory = (candidates ?? [])
            .Select(candidate => new
            {
                candidate.IntentCode,
                candidate.IntentClass,
                candidate.Availability,
                candidate.ProviderCode
            })
            .OrderBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
            .ToArray();
        return CanonicalJson.ComputeSha256(CanonicalJson.Serialize(inventory));
    }

    private static string HashContract(object contract)
    {
        return CanonicalJson.ComputeSha256(CanonicalJson.Serialize(contract));
    }

    private static string HashToolCatalog(IEnumerable<AgentPlannerToolSummary> tools)
    {
        var inventory = tools
            .OrderBy(tool => tool.ToolCode, StringComparer.Ordinal)
            .Select(tool => new
            {
                tool.ToolCode,
                tool.ProviderKind,
                tool.TargetType,
                tool.TargetName,
                tool.InputSchemaHash,
                tool.OutputSchemaHash,
                tool.RequiredPermission,
                tool.RequiresApproval,
                tool.RiskLevel,
                tool.TimeoutSeconds,
                tool.AuditLevel,
                tool.RuntimeAvailable,
                tool.Category,
                BusinessDomains = (tool.BusinessDomains ?? [])
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
                tool.DataBoundary,
                tool.IsVisibleToPlanner,
                tool.IsExecutableByAgent,
                tool.SchemaVersion,
                tool.CatalogVersion,
                tool.ApprovalPolicy,
                tool.SideEffectClass,
                tool.IsMock
            })
            .ToArray();
        return CanonicalJson.ComputeSha256(CanonicalJson.Serialize(inventory));
    }

    private static string HashProviderCatalog(IEnumerable<AgentPlannerToolSummary> tools)
    {
        var inventory = tools
            .Select(tool =>
                $"{tool.ProviderType}|{tool.ProviderKind}|{tool.TargetType}|{tool.TargetName}|{tool.CatalogVersion}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return CanonicalJson.ComputeSha256(CanonicalJson.Serialize(inventory));
    }

}

// P0 authority only seals the already-selected PlanDraft contract. It deliberately
// does not compile nodes or choose an execution graph; the sole production
// LinearV1 PlanCompiler remains a P2 deliverable.
public sealed class AgentPlanDraftContractAuthority
{
    private readonly IntentResultToCandidateAdapter intentAdapter;
    private readonly AgentPlanCanonicalizer canonicalizer;

    internal AgentPlanDraftContractAuthority(
        IntentResultToCandidateAdapter intentAdapter,
        AgentPlanCanonicalizer canonicalizer)
    {
        this.intentAdapter = intentAdapter;
        this.canonicalizer = canonicalizer;
    }

    internal Result<CanonicalAgentPlan> SealExecutable(AgentTaskPlanDocument draft)
    {
        return Invalid(
            "P0 freezes Plan/Node contracts but has no trusted LinearV1 PlanCompiler; PlanDraft cannot become executable before P2.");
    }

    internal Result<CanonicalAgentPlan> SealDraft(AgentPlanDraftContractRequest request)
    {
        var pluginMode = request.PluginSelectionMode ?? AgentPluginSelectionMode.BuiltInOnly;
        var selectedPluginIds = CanonicalGuids(request.SelectedPluginIds ?? []);
        if (pluginMode == AgentPluginSelectionMode.BuiltInOnly && selectedPluginIds.Length != 0)
        {
            return Invalid("BuiltInOnly requires selectedPluginIds=[].");
        }

        if (pluginMode == AgentPluginSelectionMode.ExplicitAllowlist)
        {
            return Invalid(
                "P0 has no verified stable plugin roster; ExplicitAllowlist remains fail-closed until its owning stage is implemented.");
        }

        var capabilityMode = request.CapabilitySelectionMode ?? AgentCapabilitySelectionMode.InferredFromGoal;
        var routedIntents = request.RoutedIntents.Count == 0
            ? [new IntentResult { Intent = "General.Chat", Confidence = 1 }]
            : request.RoutedIntents.ToArray();
        var explicitCapabilities = CanonicalStrings(request.RequestedCapabilityCodes ?? []);
        var adapted = intentAdapter.Adapt(routedIntents, request.IntentContext);
        if (!adapted.IsSuccess)
        {
            return Result.From(adapted);
        }

        var allCandidates = adapted.Value!.ToArray();
        var requestedCapabilities = capabilityMode == AgentCapabilitySelectionMode.ExplicitAllowlist
            ? explicitCapabilities
            : CanonicalStrings(allCandidates.Select(candidate => candidate.IntentCode));
        var candidates = capabilityMode == AgentCapabilitySelectionMode.ExplicitAllowlist
            ? allCandidates
                .Where(candidate => requestedCapabilities.Contains(candidate.IntentCode, StringComparer.Ordinal))
                .OrderBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
                .ToArray()
            : allCandidates;

        var stepsResult = NormalizeSteps(request.CurrentPlan.Steps);
        if (!stepsResult.IsSuccess)
        {
            return Result.From(stepsResult);
        }

        var explicitEmptyCapabilitySelection =
            capabilityMode == AgentCapabilitySelectionMode.ExplicitAllowlist &&
            requestedCapabilities.Length == 0;
        var steps = explicitEmptyCapabilitySelection
            ? Array.Empty<AgentTaskPlanStepDocument>()
            : stepsResult.Value!.ToArray();
        var approvalCheckpoints = steps
            .Where(step => step.RequiresApproval)
            .Select(step => string.IsNullOrWhiteSpace(step.ToolCode) ? step.Title : step.ToolCode!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var toolApprovalCheckpoints = steps
            .Where(step => step.RequiresApproval && !string.IsNullOrWhiteSpace(step.ToolCode))
            .Select(step => step.ToolCode!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var capabilityGaps = CanonicalStrings(
            (request.CurrentPlan.CapabilityGaps ?? [])
            .Concat(candidates
                .Where(candidate => candidate.CapabilityGap is not null)
                .Select(candidate => candidate.CapabilityGap!.Code))
            .Concat(request.RoutingConfiguration is null
                ? [AgentPlanCapabilityGapCodes.ExecutionSnapshotUnavailable]
                : [])
            .Concat(explicitEmptyCapabilitySelection
                ? [AgentPlanCapabilityGapCodes.CapabilitySelectionEmpty]
                : [])
            .Append(AgentPlanCapabilityGapCodes.PlanCompilerUnavailable));
        var artifactTargets = CanonicalStrings(request.CurrentPlan.ArtifactTypes ?? []);
        var toolRiskSummary = request.ToolCatalog.Tools
            .GroupBy(tool => tool.RiskLevel, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var plan = request.CurrentPlan with
        {
            Version = 2,
            Goal = BuildSafeGoalSummary(request.CurrentPlan.TaskType, request.RawGoal),
            UploadIds = CanonicalGuids(request.CurrentPlan.UploadIds),
            KnowledgeBaseIds = CanonicalGuids(request.CurrentPlan.KnowledgeBaseIds),
            Steps = steps,
            DataSourceIds = CanonicalGuids(request.CurrentPlan.DataSourceIds ?? []),
            BusinessDomains = CanonicalStrings(request.CurrentPlan.BusinessDomains ?? []),
            ArtifactTypes = artifactTargets,
            PlannerToolCatalogVersion = request.ToolCatalog.Version,
            PlannerAvailableToolCount = request.ToolCatalog.AvailableToolCount,
            ForcedStepCodes = CanonicalStrings(request.CurrentPlan.ForcedStepCodes ?? []),
            ApprovalCheckpoints = approvalCheckpoints,
            ToolApprovalCheckpoints = toolApprovalCheckpoints,
            DataSourceSummaries = (request.CurrentPlan.DataSourceSummaries ?? [])
                .OrderBy(summary => summary.Id.ToString("D"), StringComparer.Ordinal)
                .ToArray(),
            ToolCatalogVersion = request.ToolCatalog.Version,
            VisibleToolCount = request.ToolCatalog.AvailableToolCount,
            ToolRiskSummary = toolRiskSummary,
            MockMcpOnly = PlannerToolCatalogMetadata.IsMockMcpOnly(request.ToolCatalog.Tools),
            PlannerModelId = request.RoutingConfiguration?.ModelId,
            PlannerFallbackReason = null,
            PlannerSafetySummary = request.CurrentPlan.PlannerSafetySummary is null
                ? null
                : request.CurrentPlan.PlannerSafetySummary with
                {
                    PlanSource = "PlanV2Contract",
                    PlannerToolCatalogVersion = request.ToolCatalog.Version,
                    AvailableToolCount = request.ToolCatalog.AvailableToolCount,
                    ToolRiskSummary = toolRiskSummary,
                    MockMcpOnly = PlannerToolCatalogMetadata.IsMockMcpOnly(request.ToolCatalog.Tools)
                },
            SkillCode = null,
            SkillName = null,
            SkillRoutingReason = null,
            PlanKind = AgentTaskPlanKinds.PlanDraft,
            IsExecutable = false,
            LifecycleSealPadding = "0000",
            CapabilityGaps = capabilityGaps,
            SchemaVersion = AgentPlanContractVersions.PlanV2,
            PlanId = Guid.NewGuid(),
            PlanVersion = 1,
            PlanDigest = null,
            TopologyProfile = "LinearV1",
            IntentCandidates = candidates,
            CapabilitySelectionMode = capabilityMode,
            RequestedCapabilityCodes = requestedCapabilities,
            PluginSelectionMode = pluginMode,
            SelectedPluginIds = selectedPluginIds,
            ArtifactTargets = artifactTargets,
            // P0 freezes and validates Node v1 but does not compile a node graph.
            Nodes = [],
            JoinPolicies = [],
            Budgets = new AgentPlanBudgetDocument(
                "budget-policy:v1",
                16,
                1800,
                AgentPlanContractVersions.MaxPlanCanonicalBytes),
            ApprovalSummary = new AgentPlanApprovalSummaryDocument(
                true,
                approvalCheckpoints),
            ExecutionSnapshot = AgentPlanCatalogSnapshotAuthority.CreateSnapshot(
                request.ToolCatalog,
                request.RoutingConfiguration,
                request.CurrentPlan.DataSourceIds ?? [],
                request.CurrentPlan.KnowledgeBaseIds,
                candidates),
            SecuritySummary = new AgentPlanSecuritySummaryDocument(
                true,
                false,
                false,
                false,
                false,
                false)
        };

        return canonicalizer.Seal(plan);
    }

    private static Result<IReadOnlyCollection<AgentTaskPlanStepDocument>> NormalizeSteps(
        IReadOnlyCollection<AgentTaskPlanStepDocument> steps)
    {
        var normalized = new List<AgentTaskPlanStepDocument>(steps.Count);
        foreach (var step in steps)
        {
            string? inputJson = null;
            if (!string.IsNullOrWhiteSpace(step.InputJson))
            {
                var input = AgentNodeToolInputContractV1.Normalize(step.InputJson);
                if (!input.IsValid)
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentPlanSchemaInvalid,
                        input.Error ?? "Plan step input violates node-tool-input-policy:v1."));
                }

                inputJson = input.CanonicalJson;
            }

            normalized.Add(step with { InputJson = inputJson });
        }

        return Result.Success<IReadOnlyCollection<AgentTaskPlanStepDocument>>(normalized);
    }

    private static string BuildSafeGoalSummary(string taskType, string rawGoal)
    {
        var digest = CanonicalJson.ComputeSha256(rawGoal.Trim());
        return $"{taskType} task; goalSha256={digest}";
    }

    private static string[] CanonicalStrings(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static Guid[] CanonicalGuids(IEnumerable<Guid> values)
    {
        return values
            .Where(value => value != Guid.Empty)
            .Distinct()
            .OrderBy(value => value.ToString("D"), StringComparer.Ordinal)
            .ToArray();
    }

    private static Result<CanonicalAgentPlan> Invalid(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }
}
