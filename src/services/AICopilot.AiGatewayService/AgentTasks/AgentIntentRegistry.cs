using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentIntentRegistryContext(
    IReadOnlyCollection<Guid> UploadIds,
    IReadOnlyCollection<Guid> KnowledgeBaseIds,
    IReadOnlyCollection<BusinessDatabaseDescriptor> DataSources,
    IReadOnlyCollection<string> RequestedArtifacts,
    IReadOnlyCollection<string> KnownActionIntentCodes,
    IReadOnlyDictionary<string, string> AuthorizedDeviceIdsByCode,
    IReadOnlyCollection<string>? DerivedIntentCodes = null,
    AgentIntentRegistrySnapshot? RegistrySnapshot = null);

internal sealed record AgentIntentRegistryDescriptor(
    string IntentCode,
    AgentIntentClass IntentClass,
    AgentIntentAvailability Availability,
    string ProviderCode,
    IReadOnlyCollection<string> AllowedNodeKinds,
    IReadOnlyCollection<string> AllowedToolCodes,
    string InputRequirement,
    string OutputEvidenceKind,
    string? CapabilityGapCode);

internal sealed record AgentIntentRegistryPromptDefinition(
    string IntentCode,
    string Description,
    string? Example = null,
    string? QueryJsonExample = null,
    IReadOnlyCollection<string>? AllowedToolCodes = null);

internal sealed record AgentIntentRegistrySnapshot(
    string Version,
    string Digest,
    IReadOnlyDictionary<string, AgentIntentRegistryDescriptor> Descriptors,
    string PromptInventory)
{
    public bool TryGet(string intentCode, out AgentIntentRegistryDescriptor descriptor) =>
        Descriptors.TryGetValue(intentCode, out descriptor!);
}

internal static class AgentIntentRegistryV1
{
    public const string RegistryVersion = "intent-registry:v1";
    public const string RouterVersion = "intent-router:v2";
    public const string PromptVersion = "intent-prompt:v2";

    private static readonly Regex RegistryCodePattern = new(
        "^[A-Za-z][A-Za-z0-9_.-]{0,159}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, AgentIntentRegistryDescriptor> BaseDescriptors =
        BuildBaseDescriptors().ToDictionary(descriptor => descriptor.IntentCode, StringComparer.Ordinal);

    public static readonly string RegistryDigest = ComputeRegistryDigest(BaseDescriptors.Values);

    public static AgentIntentRegistrySnapshot FrozenSnapshot { get; } = new(
        RegistryVersion,
        RegistryDigest,
        BaseDescriptors,
        string.Empty);

    public static IReadOnlyCollection<string> FallbackIntentCodes { get; } =
    [
        "Analysis.Capacity.ByDevice",
        "Analysis.ClientRelease.List",
        "Analysis.Device.List",
        "Analysis.Device.Status",
        "Analysis.DeviceLog.ByLevel",
        "Analysis.DeviceLog.Latest",
        "Analysis.Process.List",
        "Analysis.ProductionData.ByDevice",
        "Analysis.ProductionData.Latest"
    ];

    static AgentIntentRegistryV1()
    {
        if (FallbackIntentCodes.Any(code => !BaseDescriptors.ContainsKey(code)))
        {
            throw new InvalidOperationException(
                "Intent fallback codes must be a subset of the versioned IntentRegistry.");
        }
    }

    public static AgentIntentRegistrySnapshot CreateRoutingSnapshot(
        IEnumerable<AgentIntentRegistryPromptDefinition> promptDefinitions,
        IEnumerable<string>? guidance = null)
    {
        var definitions = (promptDefinitions ?? [])
            .OrderBy(definition => definition.IntentCode, StringComparer.Ordinal)
            .ToArray();
        if (definitions.Length == 0 ||
            definitions.Any(definition =>
                string.IsNullOrWhiteSpace(definition.IntentCode) ||
                definition.IntentCode != definition.IntentCode.Trim() ||
                !RegistryCodePattern.IsMatch(definition.IntentCode) ||
                string.IsNullOrWhiteSpace(definition.Description) ||
                definition.Description != definition.Description.Trim()) ||
            definitions.GroupBy(definition => definition.IntentCode, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            throw new InvalidOperationException(
                "IntentRegistry prompt definitions must have unique canonical codes and non-empty descriptions.");
        }

        var descriptors = BaseDescriptors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (descriptors.ContainsKey(definition.IntentCode))
            {
                continue;
            }

            descriptors.Add(definition.IntentCode, CreateDynamicDescriptor(definition));
        }

        if (FallbackIntentCodes.Any(code => !descriptors.ContainsKey(code)))
        {
            throw new InvalidOperationException(
                "IntentRegistry routing snapshot is missing a deterministic fallback code.");
        }

        var promptBuilder = new StringBuilder();
        foreach (var definition in definitions)
        {
            promptBuilder.AppendLine($"- {definition.IntentCode}: {definition.Description}");
            if (!string.IsNullOrWhiteSpace(definition.Example))
            {
                promptBuilder.AppendLine($"  Query example: {definition.Example}");
            }

            if (!string.IsNullOrWhiteSpace(definition.QueryJsonExample))
            {
                promptBuilder.AppendLine($"  Query JSON example: {definition.QueryJsonExample}");
            }
        }

        foreach (var rule in (guidance ?? [])
                     .Where(rule => !string.IsNullOrWhiteSpace(rule))
                     .Select(rule => rule.Trim())
                     .Distinct(StringComparer.Ordinal))
        {
            promptBuilder.AppendLine($"  Routing rule: {rule}");
        }

        var promptInventory = promptBuilder.ToString();
        var descriptorDigest = ComputeRegistryDigest(descriptors.Values);
        var snapshotDigest = CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
        {
            version = RegistryVersion,
            descriptorDigest,
            promptInventory
        }));
        return new AgentIntentRegistrySnapshot(
            RegistryVersion,
            snapshotDigest,
            descriptors,
            promptInventory);
    }

    public static bool ValidateRoutedResults(
        AgentIntentRegistrySnapshot snapshot,
        IReadOnlyCollection<IntentResult> results)
    {
        return string.Equals(snapshot.Version, RegistryVersion, StringComparison.Ordinal) &&
               IsSha256(snapshot.Digest) &&
               results.Count is > 0 and <= 16 &&
               results.All(result =>
                   !string.IsNullOrWhiteSpace(result.Intent) &&
                   result.Intent == result.Intent.Trim() &&
                   snapshot.Descriptors.ContainsKey(result.Intent) &&
                   !CloudReadonlyAgentTextGuard.ContainsUnsafePersistedPayload(result.Query) &&
                   !double.IsNaN(result.Confidence) &&
                   !double.IsInfinity(result.Confidence) &&
                   result.Confidence is >= 0 and <= 1);
    }

    public static bool IsIntentClass(
        AgentIntentRegistrySnapshot snapshot,
        string intentCode,
        AgentIntentClass intentClass) =>
        snapshot.TryGet(intentCode, out var descriptor) &&
        descriptor.IntentClass == intentClass;

    public static bool TryResolve(
        string intentCode,
        AgentIntentRegistryContext context,
        out AgentIntentRegistryDescriptor descriptor)
    {
        if (context.RegistrySnapshot?.TryGet(intentCode, out descriptor!) == true)
        {
            if (descriptor.IntentClass != AgentIntentClass.PluginAction ||
                context.KnownActionIntentCodes.Any(code =>
                    string.Equals(code, intentCode, StringComparison.Ordinal) ||
                    string.Equals($"Action.{code}", intentCode, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        if (BaseDescriptors.TryGetValue(intentCode, out descriptor!))
        {
            return true;
        }

        if (intentCode.StartsWith("Action.", StringComparison.Ordinal) &&
            context.KnownActionIntentCodes.Any(code =>
                string.Equals(code, intentCode, StringComparison.Ordinal) ||
                string.Equals($"Action.{code}", intentCode, StringComparison.Ordinal)))
        {
            descriptor = CreateDescriptor(
                intentCode,
                AgentIntentClass.PluginAction,
                AgentIntentAvailability.KnownButUnavailable,
                "PluginActionRoster",
                ["PolicyValidationNode"],
                [],
                "RegisteredPlugin",
                "Recommendation",
                AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
            return true;
        }

        descriptor = CreateDescriptor(
            intentCode,
            AgentIntentClass.Unknown,
            AgentIntentAvailability.Unknown,
            "None",
            [],
            [],
            "Unknown",
            "None",
            AgentPlanCapabilityGapCodes.UnknownIntent);
        return false;
    }

    private static IEnumerable<AgentIntentRegistryDescriptor> BuildBaseDescriptors()
    {
        yield return Available(
            "General.Chat",
            AgentIntentClass.General,
            "BuiltIn",
            ["DeterministicComputeNode", "FileAnalysisNode", "JoinNode", "AgentReasoningNode", "ArtifactGenerationNode", "ApprovalCheckpointNode"],
            ["read_uploaded_file", "parse_table_file", "join_evidence", "agent_reasoning", "generate_chart_data", "generate_markdown_report", "generate_html_report", "generate_pdf", "generate_pptx", "generate_xlsx", "finalize_artifacts"],
            "None",
            "LlmInference");
        yield return Available(
            "Knowledge.Retrieve",
            AgentIntentClass.Knowledge,
            "KnowledgeBase",
            ["KnowledgeRetrievalNode"],
            ["rag_search"],
            "KnowledgeBaseId",
            "ObservedFact");
        yield return Available(
            "Analysis.GovernedQuery",
            AgentIntentClass.GovernedExploration,
            "BusinessDatabase",
            ["GovernedDataReadNode", "DeterministicComputeNode", "ArtifactGenerationNode"],
            ["query_business_database_readonly", "summarize_business_query_result", "generate_business_chart"],
            "DataSourceId",
            "ObservedFact");

        foreach (var policy in new[]
                 {
                     "Policy.BootstrapIdentity",
                     "Policy.DeviceLifecycle",
                     "Policy.DeviceRegistration",
                     "Policy.EmployeeAuthorization",
                     "Policy.RecipeVersioning"
                 })
        {
            yield return Available(
                policy,
                AgentIntentClass.Policy,
                "BusinessPolicy",
                ["PolicyValidationNode"],
                [],
                "None",
                "Recommendation");
        }

        foreach (var cloudIntent in new[]
                 {
                     "Analysis.Capacity.ByDevice",
                     "Analysis.Capacity.Range",
                     "Analysis.ClientRelease.List",
                     "Analysis.Device.Detail",
                     "Analysis.Device.List",
                     "Analysis.Device.Status",
                     "Analysis.DeviceLog.ByLevel",
                     "Analysis.DeviceLog.Latest",
                     "Analysis.DeviceLog.Range",
                     "Analysis.Process.Detail",
                     "Analysis.Process.List",
                     "Analysis.ProductionData.ByDevice",
                     "Analysis.ProductionData.Latest",
                     "Analysis.ProductionData.Range"
                 })
        {
            yield return Available(
                cloudIntent,
                AgentIntentClass.CloudOnly,
                "CloudAiRead",
                ["CloudReadNode", "DeterministicComputeNode"],
                ["query_business_database_readonly", "assess_cloud_health"],
                "TypedCloudQuery",
                "ObservedFact");
        }

        yield return Unavailable("Analysis.Recipe.Detail", "CloudAiReadDenied", "CloudReadNode", "ObservedFact");
        yield return Unavailable("Analysis.Recipe.List", "CloudAiReadDenied", "CloudReadNode", "ObservedFact");
        yield return Unavailable("Analysis.Recipe.VersionHistory", "CloudAiReadDenied", "CloudReadNode", "ObservedFact");

        yield return Unavailable("Prediction.Device.FailureRisk", "PredictionCatalog", "PredictionReadNode", "ModelPrediction");
        yield return Unavailable("Prediction.Device.RemainingUsefulLife", "PredictionCatalog", "PredictionReadNode", "ModelPrediction");
    }

    private static AgentIntentRegistryDescriptor Available(
        string intentCode,
        AgentIntentClass intentClass,
        string providerCode,
        IReadOnlyCollection<string> allowedNodeKinds,
        IReadOnlyCollection<string> allowedToolCodes,
        string inputRequirement,
        string outputEvidenceKind)
    {
        return CreateDescriptor(
            intentCode,
            intentClass,
            AgentIntentAvailability.Available,
            providerCode,
            allowedNodeKinds,
            allowedToolCodes,
            inputRequirement,
            outputEvidenceKind,
            null);
    }

    private static AgentIntentRegistryDescriptor Unavailable(
        string intentCode,
        string providerCode,
        string nodeKind,
        string outputEvidenceKind)
    {
        return CreateDescriptor(
            intentCode,
            AgentIntentClass.KnownButUnavailable,
            AgentIntentAvailability.KnownButUnavailable,
            providerCode,
            [nodeKind],
            [],
            "Unavailable",
            outputEvidenceKind,
            AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
    }

    public static bool TryGetDescriptor(
        string intentCode,
        out AgentIntentRegistryDescriptor descriptor)
    {
        return BaseDescriptors.TryGetValue(intentCode, out descriptor!);
    }

    private static AgentIntentRegistryDescriptor CreateDynamicDescriptor(
        AgentIntentRegistryPromptDefinition definition)
    {
        if (definition.IntentCode.StartsWith("Action.", StringComparison.Ordinal) &&
            definition.IntentCode.Length > "Action.".Length)
        {
            return CreateDescriptor(
                definition.IntentCode,
                AgentIntentClass.PluginAction,
                AgentIntentAvailability.Available,
                "PluginActionRoster",
                ["PolicyValidationNode"],
                definition.AllowedToolCodes ?? [],
                "RegisteredPlugin",
                "Recommendation",
                null);
        }

        if (definition.IntentCode.StartsWith("Knowledge.", StringComparison.Ordinal) &&
            definition.IntentCode.Length > "Knowledge.".Length)
        {
            return CreateDescriptor(
                definition.IntentCode,
                AgentIntentClass.Knowledge,
                AgentIntentAvailability.Available,
                "KnowledgeBase",
                ["KnowledgeRetrievalNode"],
                ["rag_search"],
                "KnowledgeBaseName",
                "ObservedFact",
                null);
        }

        if (definition.IntentCode.StartsWith("Analysis.", StringComparison.Ordinal) &&
            definition.IntentCode.Length > "Analysis.".Length)
        {
            return CreateDescriptor(
                definition.IntentCode,
                AgentIntentClass.GovernedExploration,
                AgentIntentAvailability.Available,
                "BusinessDatabase",
                ["GovernedDataReadNode"],
                ["query_business_database_readonly"],
                "DataSourceName",
                "ObservedFact",
                null);
        }

        throw new InvalidOperationException(
            $"Intent '{definition.IntentCode}' is not a supported versioned Registry definition.");
    }

    private static AgentIntentRegistryDescriptor CreateDescriptor(
        string intentCode,
        AgentIntentClass intentClass,
        AgentIntentAvailability availability,
        string providerCode,
        IReadOnlyCollection<string> allowedNodeKinds,
        IReadOnlyCollection<string> allowedToolCodes,
        string inputRequirement,
        string outputEvidenceKind,
        string? capabilityGapCode) =>
        new(
            intentCode,
            intentClass,
            availability,
            providerCode,
            allowedNodeKinds.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            allowedToolCodes.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            inputRequirement,
            outputEvidenceKind,
            capabilityGapCode);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string ComputeRegistryDigest(IEnumerable<AgentIntentRegistryDescriptor> descriptors)
    {
        var inventory = string.Join(
            '\n',
            descriptors
                .OrderBy(descriptor => descriptor.IntentCode, StringComparer.Ordinal)
                .Select(descriptor =>
                    $"{descriptor.IntentCode}|{descriptor.IntentClass}|{descriptor.Availability}|{descriptor.ProviderCode}|{string.Join(',', descriptor.AllowedNodeKinds)}|{string.Join(',', descriptor.AllowedToolCodes)}|{descriptor.InputRequirement}|{descriptor.OutputEvidenceKind}|{descriptor.CapabilityGapCode}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inventory)))
            .ToLowerInvariant();
    }
}

internal sealed class AgentIntentRegistryProjector
{
    private static readonly Regex CanonicalCodePattern = new(
        "^[A-Za-z][A-Za-z0-9_.-]{0,159}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> Operators =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["eq"] = "eq",
            ["equal"] = "eq",
            ["contains"] = "contains",
            ["gte"] = "gte",
            ["greaterorequal"] = "gte",
            ["lte"] = "lte",
            ["lessorequal"] = "lte",
            ["in"] = "in"
        };

    private static readonly IReadOnlySet<string> TypedRootProperties =
        new HashSet<string>(["filters", "timeRange", "queryText"], StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> FilterProperties =
        new HashSet<string>(["field", "operator", "value"], StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> TimeRangeProperties =
        new HashSet<string>(["start", "end", "fromUtc", "toUtc", "timeZone"], StringComparer.OrdinalIgnoreCase);

    internal static bool IsAllowedPredicateField(string intentCode, string fieldCode)
    {
        return CloudAiReadSemanticSchemaRegistry.IsAllowedField(intentCode, fieldCode);
    }

    internal static bool IsCanonicalPredicate(
        string intentCode,
        string fieldCode,
        string @operator,
        string value)
    {
        return CloudAiReadSemanticSchemaRegistry.TryNormalizeFilter(
                   intentCode,
                   fieldCode,
                   @operator,
                   value,
                   out var canonical) &&
               string.Equals(fieldCode, canonical.Field, StringComparison.Ordinal) &&
               string.Equals(@operator, canonical.Operator, StringComparison.Ordinal) &&
               string.Equals(value, canonical.Value, StringComparison.Ordinal);
    }

    internal static bool IsCanonicalTimeZone(string value)
    {
        return value.Length <= 80 &&
               string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
               SemanticTimeZonePolicyV1.IsCanonical(value);
    }

    internal static bool MatchesCanonicalCapabilityGap(AgentIntentCandidateDocument candidate)
    {
        if (candidate.CapabilityGap is null)
        {
            return candidate.Availability == AgentIntentAvailability.Available;
        }

        var expected = CreateCapabilityGap(candidate.IntentCode, candidate.CapabilityGap.Code);
        return expected is not null && candidate.CapabilityGap == expected;
    }

    public Result<IReadOnlyCollection<AgentIntentCandidateDocument>> Project(
        IEnumerable<IntentResult> results,
        AgentIntentRegistryContext context)
    {
        var candidates = new List<AgentIntentCandidateDocument>();
        foreach (var result in results ?? [])
        {
            var projected = ProjectOne(result, context);
            if (!projected.IsSuccess)
            {
                return Result.From(projected);
            }

            candidates.Add(projected.Value!);
        }

        var merged = new List<AgentIntentCandidateDocument>();
        foreach (var group in candidates
                     .GroupBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var merge = Merge(group);
            if (!merge.IsSuccess)
            {
                return Result.From(merge);
            }

            merged.Add(merge.Value!);
        }

        return Result.Success<IReadOnlyCollection<AgentIntentCandidateDocument>>(merged);
    }

    private static Result<AgentIntentCandidateDocument> ProjectOne(
        IntentResult result,
        AgentIntentRegistryContext context)
    {
        var intentCode = NormalizeIntentCode(result);
        if (intentCode is null)
        {
            return Invalid("IntentResult is missing a canonical intent code.");
        }

        if (double.IsNaN(result.Confidence) || double.IsInfinity(result.Confidence) || result.Confidence is < 0 or > 1)
        {
            return Invalid($"IntentResult '{intentCode}' confidence must be between 0 and 1.");
        }

        if (ContainsForbiddenAction(intentCode))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.ControlActionBlocked,
                "Intent routing requested a Cloud mutation or PLC/control action; Plan v2 compilation is blocked."));
        }

        if (CloudReadonlyAgentTextGuard.ContainsUnsafePersistedPayload(result.Query))
        {
            return Invalid($"IntentResult '{intentCode}' contains a secret, connection string, or local path.");
        }

        var isKnown = AgentIntentRegistryV1.TryResolve(intentCode, context, out var descriptor);
        if (descriptor.IntentClass == AgentIntentClass.PluginAction)
        {
            descriptor = descriptor with
            {
                Availability = AgentIntentAvailability.KnownButUnavailable,
                CapabilityGapCode = AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable
            };
        }

        var typedQuery = ParseTypedQuery(result.Query, intentCode, context);
        if (!typedQuery.IsSuccess)
        {
            return Result.From(typedQuery);
        }

        var typed = typedQuery.Value!;
        var capabilityGap = ResolveCapabilityGap(descriptor, isKnown, typed.ResourceResolutionRequired);
        var availability = capabilityGap?.Code == "resource_resolution_required"
            ? AgentIntentAvailability.Unknown
            : descriptor.Availability;

        var candidate = new AgentIntentCandidateDocument(
            AgentPlanContractVersions.IntentV1,
            descriptor.IntentCode,
            descriptor.IntentClass,
            availability,
            descriptor.ProviderCode,
            result.Confidence,
            new AgentIntentRequiredDocument(
                true,
                context.DerivedIntentCodes?.Contains(intentCode, StringComparer.Ordinal) == true
                    ? AgentIntentRequiredSource.DerivedDependency
                    : AgentIntentRequiredSource.ExplicitUserGoal,
                context.DerivedIntentCodes?.Contains(intentCode, StringComparer.Ordinal) == true
                    ? DeterministicAgentPlanCompiler.CompilerVersion
                    : null),
            new AgentIntentRequestedResourcesDocument(
                typed.Devices,
                descriptor.IntentClass == AgentIntentClass.GovernedExploration
                    ? AgentPlanCanonicalCollections.Guids(context.DataSources.Select(source => source.Id))
                    : [],
                descriptor.IntentClass == AgentIntentClass.Knowledge
                    ? AgentPlanCanonicalCollections.Guids(context.KnowledgeBaseIds)
                    : [],
                AgentPlanCanonicalCollections.Guids(context.UploadIds)),
            new AgentIntentFiltersDocument(
                typed.TimeRange,
                typed.Predicates),
            AgentPlanCanonicalCollections.Strings(context.RequestedArtifacts),
            new AgentIntentProvenanceDocument(
                AgentIntentRegistryV1.RouterVersion,
                AgentIntentRegistryV1.PromptVersion,
                context.RegistrySnapshot?.Version ?? AgentIntentRegistryV1.RegistryVersion,
                context.RegistrySnapshot?.Digest ?? AgentIntentRegistryV1.RegistryDigest),
            capabilityGap);
        return Result.Success(candidate);
    }

    private static Result<AgentIntentCandidateDocument> Merge(
        IGrouping<string, AgentIntentCandidateDocument> group)
    {
        var ordered = group
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.ProviderCode, StringComparer.Ordinal)
            .ThenBy(candidate => CanonicalJson.Serialize(candidate), StringComparer.Ordinal)
            .ToArray();
        var timeRangeContracts = ordered
            .Select(candidate => candidate.Filters.TimeRange is null
                ? "null"
                : CanonicalJson.Serialize(candidate.Filters.TimeRange))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (timeRangeContracts.Length > 1)
        {
            return Invalid(
                $"IntentResult '{group.Key}' has ambiguous duplicate typed time ranges; clarify the scope before generating a PlanDraft.");
        }

        var selected = ordered[0];
        var resources = new AgentIntentRequestedResourcesDocument(
            ordered.SelectMany(candidate => candidate.RequestedResources.Devices)
                .GroupBy(device => $"{device.ResourceType}\u001f{device.ResourceId}", StringComparer.Ordinal)
                .Select(device => device.First())
                .OrderBy(device => device.ResourceType, StringComparer.Ordinal)
                .ThenBy(device => device.ResourceId, StringComparer.Ordinal)
                .ToArray(),
            AgentPlanCanonicalCollections.Guids(ordered.SelectMany(candidate => candidate.RequestedResources.DataSourceIds)),
            AgentPlanCanonicalCollections.Guids(ordered.SelectMany(candidate => candidate.RequestedResources.KnowledgeBaseIds)),
            AgentPlanCanonicalCollections.Guids(ordered.SelectMany(candidate => candidate.RequestedResources.UploadIds)));
        var predicates = ordered.SelectMany(candidate => candidate.Filters.Predicates)
            .GroupBy(predicate => $"{predicate.FieldCode}\u001f{predicate.Operator}\u001f{predicate.Value}", StringComparer.Ordinal)
            .Select(predicate => predicate.First())
            .OrderBy(predicate => predicate.FieldCode, StringComparer.Ordinal)
            .ThenBy(predicate => predicate.Operator, StringComparer.Ordinal)
            .ThenBy(predicate => predicate.Value, StringComparer.Ordinal)
            .ToArray();
        var timeRange = ordered
            .Select(candidate => candidate.Filters.TimeRange)
            .FirstOrDefault(value => value is not null);
        return Result.Success(selected with
        {
            RequestedResources = resources,
            Filters = new AgentIntentFiltersDocument(timeRange, predicates),
            RequestedArtifacts = AgentPlanCanonicalCollections.Strings(ordered.SelectMany(candidate => candidate.RequestedArtifacts)),
            CapabilityGap = ordered.Select(candidate => candidate.CapabilityGap).FirstOrDefault(gap => gap is not null)
        });
    }

    private static string? NormalizeIntentCode(IntentResult result)
    {
        var code = result.Intent?.Trim();
        return !string.IsNullOrWhiteSpace(code) && CanonicalCodePattern.IsMatch(code)
            ? code
            : null;
    }

    private static bool ContainsForbiddenAction(string intentCode)
    {
        var normalized = intentCode.ToLowerInvariant();
        return normalized.Contains("plc", StringComparison.Ordinal) ||
               normalized.Contains("cloudwrite", StringComparison.Ordinal) ||
               normalized.Contains("cloud_write", StringComparison.Ordinal) ||
               normalized.Contains("mutation", StringComparison.Ordinal) ||
               normalized.Contains("recipe.update", StringComparison.Ordinal) ||
               normalized.Contains("recipe.write", StringComparison.Ordinal) ||
               normalized.Contains("device.disable", StringComparison.Ordinal) ||
               normalized.Contains("control", StringComparison.Ordinal);
    }

    private static AgentCapabilityGapDocument? ResolveCapabilityGap(
        AgentIntentRegistryDescriptor descriptor,
        bool isKnown,
        bool resourceResolutionRequired)
    {
        if (resourceResolutionRequired)
        {
            return CreateCapabilityGap(descriptor.IntentCode, AgentPlanCapabilityGapCodes.ResourceResolutionRequired);
        }

        if (descriptor.Availability == AgentIntentAvailability.KnownButUnavailable)
        {
            return CreateCapabilityGap(descriptor.IntentCode, AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
        }

        if (!isKnown || descriptor.Availability == AgentIntentAvailability.Unknown)
        {
            return CreateCapabilityGap(descriptor.IntentCode, AgentPlanCapabilityGapCodes.UnknownIntent);
        }

        return null;
    }

    private static AgentCapabilityGapDocument? CreateCapabilityGap(string intentCode, string code)
    {
        return code switch
        {
            AgentPlanCapabilityGapCodes.ResourceResolutionRequired => new AgentCapabilityGapDocument(
                code,
                "A device-directed resource reference cannot be authorized by the P0 snapshot contract.",
                "Remove the device-directed filter or wait for an authorized device roster contract."),
            AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable => new AgentCapabilityGapDocument(
                code,
                $"Capability '{intentCode}' is known but has no active production executor.",
                "Keep the request as a non-executable capability gap."),
            AgentPlanCapabilityGapCodes.UnknownIntent => new AgentCapabilityGapDocument(
                code,
                $"Intent '{intentCode}' is not in the frozen authorized catalog.",
                "Clarify the goal or select an available capability."),
            _ => null
        };
    }

    private static Result<ParsedTypedQuery> ParseTypedQuery(
        string? rawQuery,
        string intentCode,
        AgentIntentRegistryContext context)
    {
        if (string.IsNullOrWhiteSpace(rawQuery) || !rawQuery.TrimStart().StartsWith('{'))
        {
            return Result.Success(ParsedTypedQuery.Empty);
        }

        try
        {
            if (Encoding.UTF8.GetByteCount(rawQuery) > AgentStructuredPayloadPolicyV1.MaxNodeToolInputUtf8Bytes)
            {
                return InvalidTypedQuery(intentCode);
            }

            var canonicalQuery = CanonicalJson.Canonicalize(
                rawQuery,
                AgentStructuredPayloadPolicyV1.MaxNodeToolInputUtf8Bytes);
            using var document = JsonDocument.Parse(canonicalQuery);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return InvalidTypedQuery(intentCode);
            }

            var isCloudTypedIntent = CloudAiReadSemanticSchemaRegistry.TryGetIntentSchema(intentCode, out _);
            if (HasCaseInsensitiveDuplicateProperties(document.RootElement) ||
                isCloudTypedIntent &&
                document.RootElement.EnumerateObject().Any(property => !TypedRootProperties.Contains(property.Name)) ||
                TryGetProperty(document.RootElement, "queryText", out var queryText) &&
                queryText.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return InvalidTypedQuery(intentCode);
            }

            var predicates = ReadPredicates(document.RootElement, intentCode, out var invalidPredicates);
            var timeRange = ReadTimeRange(document.RootElement, out var invalidTimeRange);
            if (invalidPredicates || invalidTimeRange)
            {
                return InvalidTypedQuery(intentCode);
            }

            if (isCloudTypedIntent && !CloudAiReadSemanticSchemaRegistry.MatchesIntentScope(
                    intentCode,
                    predicates.Select(predicate => new CloudAiReadFilter(
                        predicate.FieldCode,
                        predicate.Operator,
                        predicate.Value)).ToArray(),
                    timeRange is not null))
            {
                return InvalidTypedQuery(intentCode);
            }
            var devices = new List<AgentIntentResourceReferenceDocument>();
            var resourceResolutionRequired = false;
            foreach (var predicate in predicates)
            {
                if (string.Equals(predicate.FieldCode, "deviceId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(predicate.FieldCode, "deviceCode", StringComparison.OrdinalIgnoreCase))
                {
                    // P0 has no independently fresh-readable device authorization roster in
                    // ExecutionSnapshot. Device-directed requests therefore remain a gap even
                    // when the router emitted a syntactically valid stable id.
                    resourceResolutionRequired = true;
                }
            }

            return Result.Success(new ParsedTypedQuery(
                devices
                    .DistinctBy(device => $"{device.ResourceType}\u001f{device.ResourceId}", StringComparer.Ordinal)
                    .OrderBy(device => device.ResourceType, StringComparer.Ordinal)
                    .ThenBy(device => device.ResourceId, StringComparer.Ordinal)
                    .ToArray(),
                predicates,
                timeRange,
                resourceResolutionRequired));
        }
        catch (JsonException)
        {
            return InvalidTypedQuery(intentCode);
        }
    }

    private static AgentIntentPredicateDocument[] ReadPredicates(
        JsonElement root,
        string intentCode,
        out bool invalid)
    {
        invalid = false;
        if (!TryGetProperty(root, "filters", out var filters) || filters.ValueKind != JsonValueKind.Array)
        {
            invalid = TryGetProperty(root, "filters", out _);
            return [];
        }

        var predicates = new List<AgentIntentPredicateDocument>();
        foreach (var filter in filters.EnumerateArray())
        {
            if (filter.ValueKind != JsonValueKind.Object ||
                filter.EnumerateObject().Any(property => !FilterProperties.Contains(property.Name)) ||
                !TryReadString(filter, "field", out var field) ||
                !TryReadString(filter, "value", out var value) ||
                !CanonicalCodePattern.IsMatch(field) ||
                value.Length is 0 or > 240)
            {
                invalid = true;
                return [];
            }

            var operatorValue = TryReadString(filter, "operator", out var rawOperator)
                ? rawOperator
                : "eq";
            if (!Operators.TryGetValue(operatorValue, out var canonicalOperator))
            {
                invalid = true;
                return [];
            }

            if (CloudReadonlyAgentTextGuard.ContainsUnsafePersistedPayload(value) ||
                !CloudAiReadSemanticSchemaRegistry.TryNormalizeFilter(
                    intentCode,
                    field,
                    canonicalOperator,
                    value,
                    out var canonicalFilter))
            {
                invalid = true;
                return [];
            }

            predicates.Add(new AgentIntentPredicateDocument(
                canonicalFilter.Field,
                canonicalFilter.Operator,
                canonicalFilter.Value));
        }

        return predicates
            .DistinctBy(predicate => $"{predicate.FieldCode}\u001f{predicate.Operator}\u001f{predicate.Value}", StringComparer.Ordinal)
            .OrderBy(predicate => predicate.FieldCode, StringComparer.Ordinal)
            .ThenBy(predicate => predicate.Operator, StringComparer.Ordinal)
            .ThenBy(predicate => predicate.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static AgentIntentTimeRangeDocument? ReadTimeRange(JsonElement root, out bool invalid)
    {
        invalid = false;
        if (!TryGetProperty(root, "timeRange", out var range))
        {
            return null;
        }

        if (range.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (range.ValueKind != JsonValueKind.Object ||
            range.EnumerateObject().Any(property => !TimeRangeProperties.Contains(property.Name)))
        {
            invalid = true;
            return null;
        }

        var hasStart = TryGetProperty(range, "start", out var startElement);
        var hasFromUtc = TryGetProperty(range, "fromUtc", out var fromUtcElement);
        var hasEnd = TryGetProperty(range, "end", out var endElement);
        var hasToUtc = TryGetProperty(range, "toUtc", out var toUtcElement);
        var usesLocalBoundary = hasStart || hasEnd;
        var usesUtcBoundary = hasFromUtc || hasToUtc;
        if (hasStart && hasFromUtc ||
            hasEnd && hasToUtc ||
            usesLocalBoundary && usesUtcBoundary)
        {
            invalid = true;
            return null;
        }

        if (!TryReadOptionalDateTime(hasStart ? startElement : fromUtcElement, hasStart || hasFromUtc, out var from) ||
            !TryReadOptionalDateTime(hasEnd ? endElement : toUtcElement, hasEnd || hasToUtc, out var to) ||
            from is null && to is null)
        {
            invalid = true;
            return null;
        }

        var hasTimeZone = TryGetProperty(range, "timeZone", out var timeZoneElement);
        var timeZone = hasTimeZone && timeZoneElement.ValueKind == JsonValueKind.String
            ? timeZoneElement.GetString()?.Trim()
            : null;
        if (hasTimeZone && timeZone is null)
        {
            invalid = true;
            return null;
        }

        var normalized = usesLocalBoundary
            ? SemanticTimeZonePolicyV1.TryNormalizeLocalRange(
                from,
                to,
                timeZone,
                out var fromUtc,
                out var toUtc,
                out var canonicalTimeZone)
            : SemanticTimeZonePolicyV1.TryNormalizeUtcRange(
                from,
                to,
                timeZone,
                out fromUtc,
                out toUtc,
                out canonicalTimeZone);
        if (!normalized)
        {
            invalid = true;
            return null;
        }

        return new AgentIntentTimeRangeDocument(fromUtc, toUtc, canonicalTimeZone);
    }

    private static Result<ParsedTypedQuery> InvalidTypedQuery(string intentCode)
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentPlanSchemaInvalid,
            $"IntentResult '{intentCode}' does not match its frozen typed filter/time-range schema."));
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        if (TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString()?.Trim() ?? string.Empty;
            return value.Length > 0;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadOptionalDateTime(
        JsonElement element,
        bool isPresent,
        out DateTimeOffset? value)
    {
        value = null;
        if (!isPresent || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String || !element.TryGetDateTimeOffset(out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool HasCaseInsensitiveDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasCaseInsensitiveDuplicateProperties(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(HasCaseInsensitiveDuplicateProperties);
        }

        return false;
    }

    private static Result<AgentIntentCandidateDocument> Invalid(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }

    private sealed record ParsedTypedQuery(
        IReadOnlyCollection<AgentIntentResourceReferenceDocument> Devices,
        IReadOnlyCollection<AgentIntentPredicateDocument> Predicates,
        AgentIntentTimeRangeDocument? TimeRange,
        bool ResourceResolutionRequired)
    {
        public static readonly ParsedTypedQuery Empty = new([], [], null, false);
    }
}
