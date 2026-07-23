using AICopilot.AiGatewayService.Models;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentPlanDraftContractRequest(
    string RawGoal,
    AgentTaskPlanDocument CurrentPlan,
    IReadOnlyCollection<IntentResult> RoutedIntents,
    AgentIntentRegistryContext IntentContext,
    PlannerToolCatalog ToolCatalog,
    AgentPluginSelectionMode? PluginSelectionMode,
    IReadOnlyCollection<Guid>? SelectedPluginIds,
    AgentCapabilitySelectionMode? CapabilitySelectionMode,
    IReadOnlyCollection<string>? RequestedCapabilityCodes,
    RuntimeAgentConfigurationSnapshot? RoutingConfiguration = null,
    RuntimeAgentConfigurationSnapshot? ReasoningConfiguration = null,
    bool AllowDevelopmentSimulationExecution = false);

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
        IReadOnlyCollection<AgentIntentCandidateDocument>? authorizedIntentCandidates = null,
        string? concurrencyPolicyVersion = null)
    {
        return snapshot == CreateSnapshot(
            catalog,
            routingConfiguration,
            authorizedDataSourceIds,
            authorizedKnowledgeBaseIds,
            authorizedIntentCandidates,
            concurrencyPolicyVersion);
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
               string.Equals(snapshot.IntentCatalogVersion, AgentIntentRegistryV1.RegistryVersion, StringComparison.Ordinal) &&
               IsSha256(snapshot.IntentCatalogDigest) &&
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
               (snapshot.ConcurrencyPolicyVersion is
                   AgentPlanContractVersions.LinearConcurrencyPolicyV1 or
                   AgentPlanContractVersions.DagConcurrencyPolicyV1) &&
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
        IReadOnlyCollection<AgentIntentCandidateDocument>? authorizedIntentCandidates = null,
        string? concurrencyPolicyVersion = null)
    {
        var digests = Compute(catalog);
        var registryVersions = (authorizedIntentCandidates ?? [])
            .Select(candidate => candidate.Provenance.CatalogVersion)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var registryDigests = (authorizedIntentCandidates ?? [])
            .Select(candidate => candidate.Provenance.CatalogDigest)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var registryVersion = registryVersions.Length == 1
            ? registryVersions[0]
            : AgentIntentRegistryV1.RegistryVersion;
        var registryDigest = registryDigests.Length == 1
            ? registryDigests[0]
            : AgentIntentRegistryV1.RegistryDigest;
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
            registryVersion,
            registryDigest,
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
            concurrencyPolicyVersion ?? AgentPlanContractVersions.LinearConcurrencyPolicyV1,
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

public sealed class AgentPlanDraftContractAuthority
{
    private readonly AgentIntentRegistryProjector intentProjector;
    private readonly AgentPlanCanonicalizer canonicalizer;
    private readonly IAgentPlanCompiler planCompiler;

    internal AgentPlanDraftContractAuthority(
        AgentIntentRegistryProjector intentProjector,
        AgentPlanCanonicalizer canonicalizer,
        IAgentPlanCompiler? planCompiler = null)
    {
        this.intentProjector = intentProjector;
        this.canonicalizer = canonicalizer;
        this.planCompiler = planCompiler ?? new DeterministicAgentPlanCompiler();
    }

    internal Result<CanonicalAgentPlan> SealExecutable(AgentTaskPlanDocument draft)
    {
        if (!string.Equals(draft.PlanKind, AgentTaskPlanKinds.PlanDraft, StringComparison.Ordinal) ||
            draft.IsExecutable ||
            draft.Nodes is not { Count: > 0 } ||
            draft.CapabilityGaps is not { Count: 0 })
        {
            return Invalid(
                "Only a gap-free PlanDraft produced by the authoritative PlanCompiler can become executable.");
        }

        if (draft.PlanVersion == int.MaxValue)
        {
            return Invalid("Plan version cannot be incremented safely.");
        }

        return canonicalizer.Seal(draft with
        {
            PlanKind = AgentTaskPlanKinds.ExecutablePlan,
            IsExecutable = true,
            LifecycleSealPadding = string.Empty,
            PlanVersion = draft.PlanVersion + 1,
            PlanDigest = null
        });
    }

    internal Result<CanonicalAgentPlan> SealDraft(AgentPlanDraftContractRequest request)
    {
        var pluginMode = request.PluginSelectionMode ?? AgentPluginSelectionMode.BuiltInOnly;
        var selectedPluginIds = AgentPlanCanonicalCollections.Guids(request.SelectedPluginIds ?? []);
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
        var explicitCapabilities = AgentPlanCanonicalCollections.Strings(request.RequestedCapabilityCodes ?? []);
        var projected = intentProjector.Project(routedIntents, request.IntentContext);
        if (!projected.IsSuccess)
        {
            return Result.From(projected);
        }

        var allCandidates = projected.Value!.ToArray();
        var requestedCapabilities = capabilityMode == AgentCapabilitySelectionMode.ExplicitAllowlist
            ? explicitCapabilities
            : AgentPlanCanonicalCollections.Strings(allCandidates.Select(candidate => candidate.IntentCode));
        var candidates = capabilityMode == AgentCapabilitySelectionMode.ExplicitAllowlist
            ? allCandidates
                .Where(candidate => requestedCapabilities.Contains(candidate.IntentCode, StringComparer.Ordinal))
                .OrderBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
                .ToArray()
            : allCandidates;

        var explicitEmptyCapabilitySelection =
            capabilityMode == AgentCapabilitySelectionMode.ExplicitAllowlist &&
            requestedCapabilities.Length == 0;
        var unresolvedCapabilityGaps = AgentPlanCanonicalCollections.Strings(
            (request.CurrentPlan.CapabilityGaps ?? [])
            .Concat(candidates
                .Where(candidate => candidate.CapabilityGap is not null)
                .Select(candidate => candidate.CapabilityGap!.Code))
            .Concat(request.RoutingConfiguration is null
                ? [AgentPlanCapabilityGapCodes.ExecutionSnapshotUnavailable]
                : [])
            .Concat(explicitEmptyCapabilitySelection
                ? [AgentPlanCapabilityGapCodes.CapabilitySelectionEmpty]
                : []));
        var artifactTargets = AgentPlanCanonicalCollections.Strings(request.CurrentPlan.ArtifactTargets ?? []);
        var dataSourceIds = AgentPlanCanonicalCollections.Guids(request.CurrentPlan.DataSourceIds ?? []);
        var businessDomains = AgentPlanCanonicalCollections.Strings(request.CurrentPlan.BusinessDomains ?? []);
        var dataSourceSummaries = (request.CurrentPlan.DataSourceSummaries ?? [])
            .OrderBy(summary => summary.Id.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        var toolRiskSummary = request.ToolCatalog.Tools
            .GroupBy(tool => tool.RiskLevel, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var executionSnapshot = AgentPlanCatalogSnapshotAuthority.CreateSnapshot(
            request.ToolCatalog,
            request.RoutingConfiguration,
            dataSourceIds,
            request.CurrentPlan.KnowledgeBaseIds,
            candidates);
        var compilation = unresolvedCapabilityGaps.Length == 0
            ? planCompiler.Compile(new AgentPlanCompilerRequest(
                Enum.Parse<AgentTaskType>(request.CurrentPlan.TaskType, ignoreCase: false),
                candidates,
                capabilityMode,
                requestedCapabilities,
                pluginMode,
                selectedPluginIds,
                request.ToolCatalog,
                AgentPlanCanonicalCollections.Guids(request.CurrentPlan.UploadIds),
                AgentPlanCanonicalCollections.Guids(request.CurrentPlan.KnowledgeBaseIds),
                (request.CurrentPlan.CloudReadonlyIntents ?? [])
                    .OrderBy(intent => intent.Intent, StringComparer.Ordinal)
                    .ToArray(),
                dataSourceIds,
                businessDomains,
                dataSourceSummaries,
                artifactTargets,
                request.CurrentPlan.RequiresDataApproval,
                request.CurrentPlan.PlannerSafetySummary?.IsSimulationOnly == true,
                executionSnapshot.DataContractDigest,
                request.ReasoningConfiguration))
            : new AgentPlanCompilation(
                "LinearV1",
                new AgentPlanConcurrencyPolicyDocument(
                    AgentPlanContractVersions.LinearConcurrencyPolicyV1,
                    1),
                [],
                [],
                [],
                unresolvedCapabilityGaps);
        executionSnapshot = executionSnapshot with
        {
            ConcurrencyPolicyVersion = compilation.ConcurrencyPolicy.PolicyVersion
        };
        var capabilityGaps = AgentPlanCanonicalCollections.Strings(
            unresolvedCapabilityGaps.Concat(compilation.CapabilityGaps));
        var nodes = capabilityGaps.Length == 0 ? compilation.Nodes : [];
        var steps = capabilityGaps.Length == 0
            ? compilation.Steps.ToArray()
            : Array.Empty<AgentTaskPlanStepDocument>();
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
        var plan = request.CurrentPlan with
        {
            Version = 2,
            Goal = BuildSafeGoalSummary(request.CurrentPlan.TaskType, request.RawGoal),
            UploadIds = AgentPlanCanonicalCollections.Guids(request.CurrentPlan.UploadIds),
            KnowledgeBaseIds = AgentPlanCanonicalCollections.Guids(request.CurrentPlan.KnowledgeBaseIds),
            CloudReadonlyIntents = (request.CurrentPlan.CloudReadonlyIntents ?? [])
                .OrderBy(intent => intent.Intent, StringComparer.Ordinal)
                .ToArray(),
            Steps = steps,
            DataSourceIds = dataSourceIds,
            BusinessDomains = businessDomains,
            PlannerToolCatalogVersion = request.ToolCatalog.Version,
            PlannerAvailableToolCount = request.ToolCatalog.AvailableToolCount,
            ForcedStepCodes = [],
            ApprovalCheckpoints = approvalCheckpoints,
            ToolApprovalCheckpoints = toolApprovalCheckpoints,
            DataSourceSummaries = dataSourceSummaries,
            ToolCatalogVersion = request.ToolCatalog.Version,
            VisibleToolCount = request.ToolCatalog.AvailableToolCount,
            ToolRiskSummary = toolRiskSummary,
            MockMcpOnly = PlannerToolCatalogMetadata.IsMockMcpOnly(request.ToolCatalog.Tools),
            PlannerModelId = request.RoutingConfiguration?.ModelId,
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
            PlanKind = AgentTaskPlanKinds.PlanDraft,
            IsExecutable = false,
            LifecycleSealPadding = "0000",
            CapabilityGaps = capabilityGaps,
            SchemaVersion = AgentPlanContractVersions.PlanV2,
            PlanId = Guid.NewGuid(),
            PlanVersion = 1,
            PlanDigest = null,
            TopologyProfile = compilation.TopologyProfile,
            IntentCandidates = candidates,
            CapabilitySelectionMode = capabilityMode,
            RequestedCapabilityCodes = requestedCapabilities,
            PluginSelectionMode = pluginMode,
            SelectedPluginIds = selectedPluginIds,
            ArtifactTargets = artifactTargets,
            Nodes = nodes,
            JoinPolicies = capabilityGaps.Length == 0 ? compilation.JoinPolicies : [],
            Budgets = new AgentPlanBudgetDocument(
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
            ConcurrencyPolicy = compilation.ConcurrencyPolicy,
            ApprovalSummary = new AgentPlanApprovalSummaryDocument(
                true,
                approvalCheckpoints),
            ExecutionSnapshot = executionSnapshot,
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

    private static string BuildSafeGoalSummary(string taskType, string rawGoal)
    {
        var digest = CanonicalJson.ComputeSha256(rawGoal.Trim());
        return $"{taskType} task; goalSha256={digest}";
    }

    private static Result<CanonicalAgentPlan> Invalid(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }
}
