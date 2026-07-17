using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentIntentAdapterContext(
    IReadOnlyCollection<Guid> UploadIds,
    IReadOnlyCollection<Guid> KnowledgeBaseIds,
    IReadOnlyCollection<BusinessDatabaseDescriptor> DataSources,
    IReadOnlyCollection<string> RequestedArtifacts,
    IReadOnlyCollection<string> KnownSkillCodes,
    IReadOnlyCollection<string> KnownActionIntentCodes,
    IReadOnlyDictionary<string, string> AuthorizedDeviceIdsByCode,
    string RouterVersion = "intent-router:v1",
    string PromptVersion = "intent-prompt:v1");

internal sealed record AgentIntentCatalogDescriptor(
    string IntentCode,
    AgentIntentClass IntentClass,
    AgentIntentAvailability Availability,
    string ProviderCode);

internal static class AgentIntentCatalogV1
{
    public const string CatalogVersion = "intent-catalog:v1";

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
            context.KnownSkillCodes.Contains(intentCode["Skill.".Length..], StringComparer.OrdinalIgnoreCase))
        {
            descriptor = new AgentIntentCatalogDescriptor(
                intentCode,
                AgentIntentClass.TransitionSkill,
                AgentIntentAvailability.Available,
                "LegacySkillCatalog");
            return true;
        }

        if (intentCode.StartsWith("Action.", StringComparison.Ordinal) &&
            context.KnownActionIntentCodes.Contains(intentCode, StringComparer.OrdinalIgnoreCase))
        {
            descriptor = new AgentIntentCatalogDescriptor(
                intentCode,
                AgentIntentClass.PluginAction,
                AgentIntentAvailability.Available,
                "PluginCatalog");
            return true;
        }

        if (intentCode.StartsWith("Knowledge.", StringComparison.Ordinal) && context.KnowledgeBaseIds.Count == 1)
        {
            descriptor = new AgentIntentCatalogDescriptor(
                intentCode,
                AgentIntentClass.Knowledge,
                AgentIntentAvailability.Available,
                "KnowledgeBase");
            return true;
        }

        if (intentCode.StartsWith("Analysis.", StringComparison.Ordinal))
        {
            var sourceName = intentCode["Analysis.".Length..];
            if (context.DataSources.Any(source => string.Equals(source.Name, sourceName, StringComparison.OrdinalIgnoreCase)))
            {
                descriptor = new AgentIntentCatalogDescriptor(
                    intentCode,
                    AgentIntentClass.GovernedExploration,
                    AgentIntentAvailability.Available,
                    "BusinessDatabase");
                return true;
            }
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
                     "Analysis.ProductionData.Range",
                     "Analysis.Recipe.Detail",
                     "Analysis.Recipe.List",
                     "Analysis.Recipe.VersionHistory"
                 })
        {
            yield return Available(cloudIntent, AgentIntentClass.CloudOnly, "CloudAiRead");
        }

        yield return Unavailable("Prediction.Device.FailureRisk");
        yield return Unavailable("Prediction.Device.RemainingUsefulLife");
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

    private static AgentIntentCatalogDescriptor Unavailable(string intentCode)
    {
        return new AgentIntentCatalogDescriptor(
            intentCode,
            AgentIntentClass.KnownButUnavailable,
            AgentIntentAvailability.KnownButUnavailable,
            "PredictionCatalog");
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

        var merged = candidates
            .GroupBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
            .Select(Merge)
            .OrderBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
            .ToArray();
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

        var isKnown = AgentIntentCatalogV1.TryResolve(intentCode, context, out var descriptor);
        var typedQuery = ParseTypedQuery(result.Query, context);
        var capabilityGap = ResolveCapabilityGap(descriptor, isKnown, typedQuery.ResourceResolutionRequired);
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
                typedQuery.Devices,
                ResolveDataSourceIds(descriptor, context),
                descriptor.IntentClass == AgentIntentClass.Knowledge
                    ? CanonicalGuids(context.KnowledgeBaseIds)
                    : [],
                CanonicalGuids(context.UploadIds)),
            new AgentIntentFiltersDocument(
                typedQuery.TimeRange,
                typedQuery.Predicates),
            CanonicalStrings(context.RequestedArtifacts),
            new AgentIntentProvenanceDocument(
                context.RouterVersion,
                context.PromptVersion,
                AgentIntentCatalogV1.CatalogVersion,
                AgentIntentCatalogV1.CatalogDigest),
            capabilityGap);
        return Result.Success(candidate);
    }

    private static AgentIntentCandidateDocument Merge(
        IGrouping<string, AgentIntentCandidateDocument> group)
    {
        var ordered = group
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.ProviderCode, StringComparer.Ordinal)
            .ToArray();
        var selected = ordered[0];
        var resources = new AgentIntentRequestedResourcesDocument(
            ordered.SelectMany(candidate => candidate.RequestedResources.Devices)
                .GroupBy(device => $"{device.ResourceType}\u001f{device.ResourceId}", StringComparer.Ordinal)
                .Select(device => device.First())
                .OrderBy(device => device.ResourceType, StringComparer.Ordinal)
                .ThenBy(device => device.ResourceId, StringComparer.Ordinal)
                .ToArray(),
            CanonicalGuids(ordered.SelectMany(candidate => candidate.RequestedResources.DataSourceIds)),
            CanonicalGuids(ordered.SelectMany(candidate => candidate.RequestedResources.KnowledgeBaseIds)),
            CanonicalGuids(ordered.SelectMany(candidate => candidate.RequestedResources.UploadIds)));
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
        return selected with
        {
            RequestedResources = resources,
            Filters = new AgentIntentFiltersDocument(timeRange, predicates),
            RequestedArtifacts = CanonicalStrings(ordered.SelectMany(candidate => candidate.RequestedArtifacts)),
            CapabilityGap = ordered.Select(candidate => candidate.CapabilityGap).FirstOrDefault(gap => gap is not null)
        };
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
            return new AgentCapabilityGapDocument(
                "resource_resolution_required",
                "A natural-language resource reference was not resolved to an authorized stable id before confirmation.",
                "Select the target resource explicitly and generate a new PlanDraft.");
        }

        if (descriptor.Availability == AgentIntentAvailability.KnownButUnavailable)
        {
            return new AgentCapabilityGapDocument(
                "known_capability_unavailable",
                $"Capability '{descriptor.IntentCode}' is known but has no active production executor.",
                "Keep the request as a non-executable capability gap.");
        }

        if (!isKnown || descriptor.Availability == AgentIntentAvailability.Unknown)
        {
            return new AgentCapabilityGapDocument(
                "unknown_intent",
                $"Intent '{descriptor.IntentCode}' is not in the frozen authorized catalog.",
                "Clarify the goal or select an available capability.");
        }

        return null;
    }

    private static Guid[] ResolveDataSourceIds(
        AgentIntentCatalogDescriptor descriptor,
        AgentIntentAdapterContext context)
    {
        if (descriptor.IntentClass != AgentIntentClass.GovernedExploration)
        {
            return [];
        }

        var sourceName = descriptor.IntentCode["Analysis.".Length..];
        return CanonicalGuids(context.DataSources
            .Where(source => string.Equals(source.Name, sourceName, StringComparison.OrdinalIgnoreCase))
            .Select(source => source.Id));
    }

    private static ParsedTypedQuery ParseTypedQuery(
        string? rawQuery,
        AgentIntentAdapterContext context)
    {
        if (string.IsNullOrWhiteSpace(rawQuery) || !rawQuery.TrimStart().StartsWith('{'))
        {
            return ParsedTypedQuery.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(rawQuery);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ParsedTypedQuery.Empty;
            }

            var predicates = ReadPredicates(document.RootElement);
            var devices = new List<AgentIntentResourceReferenceDocument>();
            var resourceResolutionRequired = false;
            foreach (var predicate in predicates)
            {
                if (string.Equals(predicate.FieldCode, "deviceId", StringComparison.OrdinalIgnoreCase) &&
                    Guid.TryParse(predicate.Value, out var deviceId) &&
                    deviceId != Guid.Empty)
                {
                    devices.Add(new AgentIntentResourceReferenceDocument("Device", deviceId.ToString("D")));
                }
                else if (string.Equals(predicate.FieldCode, "deviceCode", StringComparison.OrdinalIgnoreCase))
                {
                    if (context.AuthorizedDeviceIdsByCode.TryGetValue(predicate.Value, out var stableId) &&
                        !string.IsNullOrWhiteSpace(stableId))
                    {
                        devices.Add(new AgentIntentResourceReferenceDocument("Device", stableId.Trim()));
                    }
                    else
                    {
                        resourceResolutionRequired = true;
                    }
                }
            }

            return new ParsedTypedQuery(
                devices
                    .DistinctBy(device => $"{device.ResourceType}\u001f{device.ResourceId}", StringComparer.Ordinal)
                    .OrderBy(device => device.ResourceType, StringComparer.Ordinal)
                    .ThenBy(device => device.ResourceId, StringComparer.Ordinal)
                    .ToArray(),
                predicates,
                ReadTimeRange(document.RootElement),
                resourceResolutionRequired);
        }
        catch (JsonException)
        {
            return ParsedTypedQuery.Empty;
        }
    }

    private static AgentIntentPredicateDocument[] ReadPredicates(JsonElement root)
    {
        if (!TryGetProperty(root, "filters", out var filters) || filters.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var predicates = new List<AgentIntentPredicateDocument>();
        foreach (var filter in filters.EnumerateArray())
        {
            if (filter.ValueKind != JsonValueKind.Object ||
                !TryReadString(filter, "field", out var field) ||
                !TryReadString(filter, "value", out var value) ||
                !CanonicalCodePattern.IsMatch(field) ||
                value.Length is 0 or > 240)
            {
                continue;
            }

            var operatorValue = TryReadString(filter, "operator", out var rawOperator)
                ? rawOperator
                : "eq";
            if (!Operators.TryGetValue(operatorValue, out var canonicalOperator))
            {
                continue;
            }

            predicates.Add(new AgentIntentPredicateDocument(field, canonicalOperator, value));
        }

        return predicates
            .DistinctBy(predicate => $"{predicate.FieldCode}\u001f{predicate.Operator}\u001f{predicate.Value}", StringComparer.Ordinal)
            .OrderBy(predicate => predicate.FieldCode, StringComparer.Ordinal)
            .ThenBy(predicate => predicate.Operator, StringComparer.Ordinal)
            .ThenBy(predicate => predicate.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static AgentIntentTimeRangeDocument? ReadTimeRange(JsonElement root)
    {
        if (!TryGetProperty(root, "timeRange", out var range) || range.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var from = TryReadDateTime(range, "start") ?? TryReadDateTime(range, "fromUtc");
        var to = TryReadDateTime(range, "end") ?? TryReadDateTime(range, "toUtc");
        if (from is null && to is null)
        {
            return null;
        }

        var timeZone = TryReadString(range, "timeZone", out var value) && value.Length <= 80
            ? value
            : "UTC";
        return new AgentIntentTimeRangeDocument(from?.ToUniversalTime(), to?.ToUniversalTime(), timeZone);
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

    private static DateTimeOffset? TryReadDateTime(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               property.TryGetDateTimeOffset(out var value)
            ? value
            : null;
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
