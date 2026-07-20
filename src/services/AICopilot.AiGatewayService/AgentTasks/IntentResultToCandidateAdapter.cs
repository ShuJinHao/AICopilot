using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentIntentAdapterContext(
    IReadOnlyCollection<Guid> UploadIds,
    IReadOnlyCollection<Guid> KnowledgeBaseIds,
    IReadOnlyCollection<BusinessDatabaseDescriptor> DataSources,
    IReadOnlyCollection<string> RequestedArtifacts,
    IReadOnlyCollection<string> KnownSkillCodes,
    IReadOnlyCollection<string> KnownActionIntentCodes,
    IReadOnlyDictionary<string, string> AuthorizedDeviceIdsByCode);

internal sealed record AgentIntentCatalogDescriptor(
    string IntentCode,
    AgentIntentClass IntentClass,
    AgentIntentAvailability Availability,
    string ProviderCode);

internal static class AgentIntentCatalogV1
{
    public const string CatalogVersion = "intent-catalog:v1";
    public const string RouterVersion = "intent-router:v1";
    public const string PromptVersion = "intent-prompt:v1";

    private static readonly IReadOnlyDictionary<string, AgentIntentCatalogDescriptor> BaseDescriptors =
        BuildBaseDescriptors().ToDictionary(descriptor => descriptor.IntentCode, StringComparer.Ordinal);

    public static readonly string CatalogDigest = ComputeCatalogDigest();

    public static bool TryResolve(
        string intentCode,
        AgentIntentAdapterContext context,
        out AgentIntentCatalogDescriptor descriptor)
    {
        if (BaseDescriptors.TryGetValue(intentCode, out descriptor!))
        {
            return true;
        }

        if (intentCode.StartsWith("Skill.", StringComparison.Ordinal) &&
            context.KnownSkillCodes.Contains(intentCode["Skill.".Length..], StringComparer.Ordinal))
        {
            descriptor = new AgentIntentCatalogDescriptor(
                intentCode,
                AgentIntentClass.TransitionSkill,
                AgentIntentAvailability.KnownButUnavailable,
                "TransitionSkillRoster");
            return true;
        }

        if (intentCode.StartsWith("Action.", StringComparison.Ordinal) &&
            context.KnownActionIntentCodes.Any(code =>
                string.Equals(code, intentCode, StringComparison.Ordinal) ||
                string.Equals($"Action.{code}", intentCode, StringComparison.Ordinal)))
        {
            descriptor = new AgentIntentCatalogDescriptor(
                intentCode,
                AgentIntentClass.PluginAction,
                AgentIntentAvailability.KnownButUnavailable,
                "PluginActionRoster");
            return true;
        }

        descriptor = new AgentIntentCatalogDescriptor(
            intentCode,
            AgentIntentClass.Unknown,
            AgentIntentAvailability.Unknown,
            "None");
        return false;
    }

    private static IEnumerable<AgentIntentCatalogDescriptor> BuildBaseDescriptors()
    {
        yield return Available("General.Chat", AgentIntentClass.General, "BuiltIn");
        yield return Available("Knowledge.Retrieve", AgentIntentClass.Knowledge, "KnowledgeBase");
        yield return Available("Analysis.GovernedQuery", AgentIntentClass.GovernedExploration, "BusinessDatabase");

        foreach (var policy in new[]
                 {
                     "Policy.BootstrapIdentity",
                     "Policy.DeviceLifecycle",
                     "Policy.DeviceRegistration",
                     "Policy.EmployeeAuthorization",
                     "Policy.RecipeVersioning"
                 })
        {
            yield return Available(policy, AgentIntentClass.Policy, "BusinessPolicy");
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
            yield return Available(cloudIntent, AgentIntentClass.CloudOnly, "CloudAiRead");
        }

        yield return Unavailable("Analysis.Recipe.Detail", "CloudAiReadDenied");
        yield return Unavailable("Analysis.Recipe.List", "CloudAiReadDenied");
        yield return Unavailable("Analysis.Recipe.VersionHistory", "CloudAiReadDenied");

        yield return Unavailable("Prediction.Device.FailureRisk", "PredictionCatalog");
        yield return Unavailable("Prediction.Device.RemainingUsefulLife", "PredictionCatalog");
    }

    private static AgentIntentCatalogDescriptor Available(
        string intentCode,
        AgentIntentClass intentClass,
        string providerCode)
    {
        return new AgentIntentCatalogDescriptor(
            intentCode,
            intentClass,
            AgentIntentAvailability.Available,
            providerCode);
    }

    private static AgentIntentCatalogDescriptor Unavailable(string intentCode, string providerCode)
    {
        return new AgentIntentCatalogDescriptor(
            intentCode,
            AgentIntentClass.KnownButUnavailable,
            AgentIntentAvailability.KnownButUnavailable,
            providerCode);
    }

    public static bool TryGetFrozenDescriptor(
        string intentCode,
        out AgentIntentCatalogDescriptor descriptor)
    {
        return BaseDescriptors.TryGetValue(intentCode, out descriptor!);
    }

    private static string ComputeCatalogDigest()
    {
        var inventory = string.Join(
            '\n',
            BaseDescriptors.Values
                .OrderBy(descriptor => descriptor.IntentCode, StringComparer.Ordinal)
                .Select(descriptor =>
                    $"{descriptor.IntentCode}|{descriptor.IntentClass}|{descriptor.Availability}|{descriptor.ProviderCode}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inventory)))
            .ToLowerInvariant();
    }
}

internal sealed class IntentResultToCandidateAdapter
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

    public Result<IReadOnlyCollection<AgentIntentCandidateDocument>> Adapt(
        IEnumerable<IntentResult> results,
        AgentIntentAdapterContext context)
    {
        var candidates = new List<AgentIntentCandidateDocument>();
        foreach (var result in results ?? [])
        {
            var adapted = AdaptOne(result, context);
            if (!adapted.IsSuccess)
            {
                return Result.From(adapted);
            }

            candidates.Add(adapted.Value!);
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

    private static Result<AgentIntentCandidateDocument> AdaptOne(
        IntentResult result,
        AgentIntentAdapterContext context)
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

        if (ContainsForbiddenAction(intentCode) ||
            CloudReadonlyAgentTextGuard.ContainsForbiddenWriteSemantic(result.Query))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.ControlActionBlocked,
                "Intent routing requested a Cloud mutation or PLC/control action; Plan v2 compilation is blocked."));
        }

        if (CloudReadonlyAgentTextGuard.ContainsUnsafePersistedPayload(result.Query))
        {
            return Invalid($"IntentResult '{intentCode}' contains a secret, SQL statement, connection string, or local path.");
        }

        var isKnown = AgentIntentCatalogV1.TryResolve(intentCode, context, out var descriptor);
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
                AgentIntentRequiredSource.ExplicitUserGoal,
                RuleId: null),
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
                AgentIntentCatalogV1.RouterVersion,
                AgentIntentCatalogV1.PromptVersion,
                AgentIntentCatalogV1.CatalogVersion,
                AgentIntentCatalogV1.CatalogDigest),
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
        var code = string.IsNullOrWhiteSpace(result.Intent) && !string.IsNullOrWhiteSpace(result.SkillCode)
            ? $"Skill.{result.SkillCode.Trim()}"
            : result.Intent?.Trim();
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
        AgentIntentCatalogDescriptor descriptor,
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
        AgentIntentAdapterContext context)
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
