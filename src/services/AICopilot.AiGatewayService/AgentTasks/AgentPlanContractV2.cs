using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentPlanContractVersions
{
    public const string LegacyV1 = "1.0";
    public const string PlanV2 = "2.0";
    public const string IntentV1 = "intent-candidate:v1";
    public const string NodeV1 = "agent-node:v1";
    public const string EvidenceV1 = "evidence:v1";
    public const string ExecutionSnapshotV1 = "execution-snapshot:v1";
    public const string PlanPolicyV1 = "plan-contract-policy:v1";
    public const string ConcurrencyPolicyV1 = "linear-sequential-concurrency:v1";
    public const string InlineEvidencePolicyV1 = "inline-evidence-policy:v1";

    public const int MaxPlanCanonicalBytes = 262_144;
    public const int MaxNodeInputCanonicalBytes = AgentStructuredPayloadPolicyV1.MaxNodeToolInputUtf8Bytes;
    public const int MaxInlineEvidenceCanonicalBytes = 65_536;
    public const int MaxStepTitleCharacters = 200;
    public const int MaxStepDescriptionCharacters = 1_000;
    public const int MaxStepToolCodeCharacters = 100;
}

internal static class AgentPlanRetiredSelectionContract
{
    public const string SkillCodeDetail =
        "skillCode is retired for Plan v2 and cannot be used as a planning selection input.";
    public const string PreferredToolCodesDetail =
        "preferredToolCodes is retired for Plan v2 and cannot be used as a planning selection input.";
    public const string UserFacingMessage =
        "当前计划入口不再接受 Skill 或工具选择，请清除旧选择后重试。";

    public static ApiProblemDescriptor? Validate(
        string? skillCode,
        IReadOnlyCollection<string>? preferredToolCodes)
    {
        if (!string.IsNullOrWhiteSpace(skillCode))
        {
            return new ApiProblemDescriptor(AppProblemCodes.AgentPlanSchemaInvalid, SkillCodeDetail);
        }

        return preferredToolCodes is { Count: > 0 }
            ? new ApiProblemDescriptor(AppProblemCodes.AgentPlanSchemaInvalid, PreferredToolCodesDetail)
            : null;
    }
}

internal static class AgentPlanContractSchemaAuthority
{
    public static readonly IReadOnlySet<string> DigestExcludedRootProperties = new HashSet<string>(
        ["planDigest", "planKind", "isExecutable", "lifecycleSealPadding"],
        StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> AllowedNodeKinds = new HashSet<string>(
    [
        "CloudReadNode",
        "GovernedDataReadNode",
        "KnowledgeRetrievalNode",
        "FileAnalysisNode",
        "DeterministicComputeNode",
        "ApprovalCheckpointNode",
        "PolicyValidationNode"
    ], StringComparer.Ordinal);

    private static readonly Type[] PlanContractTypes =
    [
        typeof(AgentTaskPlanDocument),
        typeof(AgentTaskPlanCloudReadonlyIntentDocument),
        typeof(AgentTaskPlanSemanticFilterDocument),
        typeof(AgentTaskPlanSemanticTimeRangeDocument),
        typeof(AgentTaskPlanSemanticSortDocument),
        typeof(AgentTaskPlanStepDocument),
        typeof(AgentTaskPlanRuntimeSettingsDocument),
        typeof(AgentTaskPlanSafetySummaryDocument),
        typeof(AgentTaskPlanDataSourceSummaryDocument),
        typeof(AgentIntentCandidateDocument),
        typeof(AgentIntentRequiredDocument),
        typeof(AgentIntentRequestedResourcesDocument),
        typeof(AgentIntentResourceReferenceDocument),
        typeof(AgentIntentFiltersDocument),
        typeof(AgentIntentTimeRangeDocument),
        typeof(AgentIntentPredicateDocument),
        typeof(AgentIntentProvenanceDocument),
        typeof(AgentCapabilityGapDocument),
        typeof(AgentPlanBudgetDocument),
        typeof(AgentPlanApprovalSummaryDocument),
        typeof(AgentExecutionSnapshotDocument),
        typeof(AgentPlanSecuritySummaryDocument)
    ];

    private static readonly Type[] NodeContractTypes =
    [
        typeof(AgentPlanNodeDocument),
        typeof(AgentPlanNodeInputDocument),
        typeof(AgentPlanModelPolicyDocument),
        typeof(AgentPlanTimeoutPolicyDocument),
        typeof(AgentPlanRetryPolicyDocument),
        typeof(AgentPlanNodeBudgetDocument),
        typeof(AgentPlanApprovalPolicyDocument),
        typeof(AgentPlanIdempotencyPolicyDocument)
    ];

    public static readonly string PlanContractDigest = CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
    {
        schemaVersion = AgentPlanContractVersions.PlanV2,
        typeSchema = DescribeTypes(PlanContractTypes),
        topologyProfiles = new[] { "LinearV1" },
        planKinds = new[] { "ExecutablePlan", "PlanDraft" },
        lifecycleTuples = new[] { "PlanDraft|false|0000", "ExecutablePlan|true|" },
        queryModes = new[] { "CloudReadonly", "TextToSql" },
        capabilitySelectionModes = new[] { "ExplicitAllowlist", "InferredFromGoal" },
        pluginSelectionModes = new[] { "BuiltInOnly" },
        artifactTargets = new[] { "chart", "html", "markdown", "pdf", "pptx", "xlsx" },
        canonicalSetProperties = new[]
        {
            "approvalCheckpoints", "artifactTargets", "artifactTypes", "businessDomains", "capabilityGaps",
            "dataSourceIds", "forcedStepCodes", "knowledgeBaseIds", "requestedCapabilityCodes",
            "selectedPluginIds", "toolApprovalCheckpoints", "uploadIds"
        },
        sequenceProperties = new[] { "intentCandidates", "nodes", "steps" },
        digestExcludedRootProperties = DigestExcludedRootProperties.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
        forbiddenLegacyProperties = new[] { "skillCode", "skillName", "skillRoutingReason" },
        goalFormat = "{taskType} task; goalSha256={lowercase-sha256}",
        plannerFallbackReason = "null",
        compilerPolicy = new
        {
            phase = "P0-contract-only",
            trustedCompilerAvailable = false,
            draftGap = AgentPlanCapabilityGapCodes.PlanCompilerUnavailable,
            executablePolicy = "fail-closed-until-single-p2-linear-compiler",
            requiredCandidateCoverage = "every-required-available-candidate-needs-producer-node",
            requestedCapabilities = "hard-upper-bound-not-synthetic-required-work"
        },
        budgetPolicy = new
        {
            version = "budget-policy:v1",
            maxNodes = 16,
            maxElapsedSeconds = 1800,
            maxCanonicalBytes = AgentPlanContractVersions.MaxPlanCanonicalBytes
        },
        maxCanonicalBytes = AgentPlanContractVersions.MaxPlanCanonicalBytes,
        canonicalJsonPolicy = new
        {
            version = AgentCanonicalJsonV1.PolicyVersion,
            maxDepth = AgentCanonicalJsonV1.MaxDepth,
            maxRawUtf8Bytes = AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes,
            maxObjectProperties = AgentCanonicalJsonV1.MaxObjectProperties,
            maxArrayItems = AgentCanonicalJsonV1.MaxArrayItems,
            maxTotalTokens = AgentCanonicalJsonV1.MaxTotalTokens,
            maxPropertyNameUtf8Bytes = AgentCanonicalJsonV1.MaxPropertyNameUtf8Bytes,
            maxStringUtf8Bytes = AgentCanonicalJsonV1.MaxStringUtf8Bytes,
            duplicateProperties = "reject-exact-and-case-ambiguous-at-typed-boundaries"
        },
        canonicalNumberPolicy = new
        {
            version = AgentCanonicalNumberPolicyV1.Version,
            maxLexicalCharacters = AgentCanonicalNumberPolicyV1.MaxLexicalCharacters,
            maxSignificantDigits = AgentCanonicalNumberPolicyV1.MaxSignificantDigits,
            maxExponentDigits = AgentCanonicalNumberPolicyV1.MaxExponentDigits,
            maxAbsoluteExponent = AgentCanonicalNumberPolicyV1.MaxAbsoluteExponent
        },
        nodeInputPolicy = new
        {
            version = AgentStructuredPayloadPolicyV1.PolicyVersion,
            maxUtf8Bytes = AgentStructuredPayloadPolicyV1.MaxNodeToolInputUtf8Bytes
        },
        toolInputSchemaContract = new
        {
            version = ToolInputSchemaContractV1.ContractVersion,
            digest = ToolInputSchemaContractV1.ContractDigest,
            plannerExactSchemaUtf8Bytes = PlannerToolCatalog.MaxInputSchemaUtf8Bytes,
            catalogVersion = BuiltInToolRegistrations.CurrentCatalogVersion,
            builtInSchemaVersion = BuiltInToolRegistrations.CurrentSchemaVersion
        },
        toolOutputSchemaContract = new
        {
            version = ToolOutputSchemaContractV1.ContractVersion,
            digest = ToolOutputSchemaContractV1.ContractDigest,
            plannerExactSchemaUtf8Bytes = PlannerToolCatalog.MaxOutputSchemaUtf8Bytes,
            inlineOutputUtf8Bytes = AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes,
            durableOutputVersion = AgentToolDurableOutputContractV1.ContractVersion,
            durableOutputDigest = AgentToolDurableOutputContractV1.ContractDigest,
            catalogVersion = BuiltInToolRegistrations.CurrentCatalogVersion,
            builtInSchemaVersion = BuiltInToolRegistrations.CurrentSchemaVersion
        },
        cloudAiReadSemanticSchema = new
        {
            version = CloudAiReadSemanticSchemaRegistry.ContractVersion,
            timeZonePolicy = SemanticTimeZonePolicyV1.ContractVersion,
            operations = CloudAiReadSemanticSchemaRegistry.GetOperationSchemas(),
            intents = CloudAiReadSemanticSchemaRegistry.GetIntentSchemas()
        },
        stepFieldLimits = new
        {
            titleCharacters = AgentPlanContractVersions.MaxStepTitleCharacters,
            descriptionCharacters = AgentPlanContractVersions.MaxStepDescriptionCharacters,
            toolCodeCharacters = AgentPlanContractVersions.MaxStepToolCodeCharacters,
            whitespacePolicy = "exact-trimmed-no-silent-normalization"
        }
    }));

    public static readonly string NodeContractDigest = CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
    {
        schemaVersion = AgentPlanContractVersions.NodeV1,
        typeSchema = DescribeTypes(NodeContractTypes),
        nodeKinds = AllowedNodeKinds.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
        disabledNodeKinds = new[] { "ArtifactGenerationNode" },
        dependencyPolicy = "strict-immediate-predecessor:v1",
        inputSchemaRef = "node-input:v1",
        outputSchemaPrefix = "evidence:",
        sideEffectClasses = new[] { "ArtifactDraftOnly", "ReadOnly" },
        canonicalSetProperties = new[]
        {
            "businessDomains", "dataScopes", "evidenceSelectors", "knowledgeScopes", "requestedCapabilityCodes",
            "requestedScope", "requestedToolCodes"
        },
        policyRanges = new
        {
            timeout = "timeout-policy:v1|1..3600",
            retry = "retry-policy:v1|1..5|None,Fixed,Exponential",
            idempotency = "idempotency-policy:v1|Deterministic,ReadOnly,Fenced",
            maxRows = "0..200"
        },
        cloudInput = $"CloudAiRead|semanticIntent+semanticPlanDigest+requestedScope+maxRows=1..{CloudAiReadRowLimitPolicy.MaxRows}|no-sql-no-dataSource",
        governedInput = "dataSourceId+GovernedSql/TextToSql+governedSchemaDigest+requestedPermission+maxRows|no-CloudAiRead",
        capabilityToolBinding = "strict-intersection:v1",
        canonicalJsonPolicy = AgentCanonicalJsonV1.PolicyVersion,
        canonicalNumberPolicy = AgentCanonicalNumberPolicyV1.Version,
        nodeInputPolicy = AgentStructuredPayloadPolicyV1.PolicyVersion,
        maxInputCanonicalBytes = AgentPlanContractVersions.MaxNodeInputCanonicalBytes
    }));

    private static object[] DescribeTypes(IEnumerable<Type> types)
    {
        var nullability = new NullabilityInfoContext();
        return types
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => new
            {
                name = type.FullName,
                properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(property =>
                    {
                        var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
                        var nullable = nullability.Create(property).ReadState.ToString();
                        var ignore = property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition.ToString() ?? "Never";
                        return new
                        {
                            jsonName,
                            clrType = DescribeClrType(property.PropertyType),
                            nullable,
                            ignore,
                            enumValues = GetEnumValues(property.PropertyType)
                        };
                    })
                    .OrderBy(property => property.jsonName, StringComparer.Ordinal)
                    .ToArray()
            })
            .Cast<object>()
            .ToArray();
    }

    private static string DescribeClrType(Type type)
    {
        if (type.IsArray)
        {
            return $"{DescribeClrType(type.GetElementType()!)}[]";
        }

        if (type.IsGenericType)
        {
            var name = type.GetGenericTypeDefinition().FullName!.Split('`')[0];
            return $"{name}<{string.Join(',', type.GetGenericArguments().Select(DescribeClrType))}>";
        }

        return type.FullName ?? type.Name;
    }

    private static string[] GetEnumValues(Type type)
    {
        var enumType = Nullable.GetUnderlyingType(type) ?? type;
        return enumType.IsEnum ? Enum.GetNames(enumType) : [];
    }
}

internal static class AgentPlanCapabilityGapCodes
{
    public const string SkillSelectionUnresolved = "skill_selection_unresolved";
    public const string ToolCatalogUnavailable = "tool_catalog_unavailable";
    public const string PlannedToolUnavailable = "planned_tool_unavailable";
    public const string CloudReadonlyResolverUnavailable = "cloud_readonly_resolver_unavailable";
    public const string CloudReadonlyIntentUnavailable = "cloud_readonly_intent_unavailable";
    public const string ExecutionSnapshotUnavailable = "execution_snapshot_unavailable";
    public const string ResourceResolutionRequired = "resource_resolution_required";
    public const string KnownCapabilityUnavailable = "known_capability_unavailable";
    public const string UnknownIntent = "unknown_intent";
    public const string CapabilitySelectionEmpty = "capability_selection_empty";
    public const string PlanCompilerUnavailable = "plan_compiler_unavailable";

    private static readonly IReadOnlySet<string> Frozen = new HashSet<string>(
    [
        SkillSelectionUnresolved,
        ToolCatalogUnavailable,
        PlannedToolUnavailable,
        CloudReadonlyResolverUnavailable,
        CloudReadonlyIntentUnavailable,
        ExecutionSnapshotUnavailable,
        ResourceResolutionRequired,
        KnownCapabilityUnavailable,
        UnknownIntent,
        CapabilitySelectionEmpty,
        PlanCompilerUnavailable
    ], StringComparer.Ordinal);

    public static bool IsFrozen(string value) => Frozen.Contains(value);
}

internal enum AgentIntentClass
{
    General = 1,
    TransitionSkill = 2,
    PluginAction = 3,
    Knowledge = 4,
    Policy = 5,
    CloudOnly = 6,
    GovernedExploration = 7,
    KnownButUnavailable = 8,
    Unknown = 9
}

internal enum AgentIntentAvailability
{
    Available = 1,
    KnownButUnavailable = 2,
    Unknown = 3
}

internal enum AgentIntentRequiredSource
{
    ExplicitUserGoal = 1,
    PolicyRequired = 2,
    DerivedDependency = 3
}

[JsonConverter(typeof(AgentStrictStringEnumJsonConverter<AgentPluginSelectionMode>))]
public enum AgentPluginSelectionMode
{
    BuiltInOnly = 1,
    ExplicitAllowlist = 2
}

[JsonConverter(typeof(AgentStrictStringEnumJsonConverter<AgentCapabilitySelectionMode>))]
public enum AgentCapabilitySelectionMode
{
    InferredFromGoal = 1,
    ExplicitAllowlist = 2
}

public sealed class AgentStrictStringEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"{typeof(TEnum).Name} must be an exact string enum name.");
        }

        var raw = reader.GetString();
        if (raw is null ||
            !Enum.TryParse<TEnum>(raw, ignoreCase: false, out var value) ||
            !Enum.IsDefined(value) ||
            !string.Equals(raw, value.ToString(), StringComparison.Ordinal))
        {
            throw new JsonException($"Unknown {typeof(TEnum).Name} value.");
        }

        return value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        TEnum value,
        JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
        {
            throw new JsonException($"Unknown {typeof(TEnum).Name} value.");
        }

        writer.WriteStringValue(value.ToString());
    }
}

internal sealed record AgentIntentCandidateDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("intentCode")] string IntentCode,
    [property: JsonPropertyName("intentClass")] AgentIntentClass IntentClass,
    [property: JsonPropertyName("availability")] AgentIntentAvailability Availability,
    [property: JsonPropertyName("providerCode")] string ProviderCode,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("required")] AgentIntentRequiredDocument Required,
    [property: JsonPropertyName("requestedResources")] AgentIntentRequestedResourcesDocument RequestedResources,
    [property: JsonPropertyName("filters")] AgentIntentFiltersDocument Filters,
    [property: JsonPropertyName("requestedArtifacts")] IReadOnlyCollection<string> RequestedArtifacts,
    [property: JsonPropertyName("provenance")] AgentIntentProvenanceDocument Provenance,
    [property: JsonPropertyName("capabilityGap")] AgentCapabilityGapDocument? CapabilityGap);

internal sealed record AgentIntentRequiredDocument(
    [property: JsonPropertyName("value")] bool Value,
    [property: JsonPropertyName("source")] AgentIntentRequiredSource Source,
    [property: JsonPropertyName("ruleId")] string? RuleId);

internal sealed record AgentIntentRequestedResourcesDocument(
    [property: JsonPropertyName("devices")] IReadOnlyCollection<AgentIntentResourceReferenceDocument> Devices,
    [property: JsonPropertyName("dataSourceIds")] IReadOnlyCollection<Guid> DataSourceIds,
    [property: JsonPropertyName("knowledgeBaseIds")] IReadOnlyCollection<Guid> KnowledgeBaseIds,
    [property: JsonPropertyName("uploadIds")] IReadOnlyCollection<Guid> UploadIds);

internal sealed record AgentIntentResourceReferenceDocument(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("resourceId")] string ResourceId);

internal sealed record AgentIntentFiltersDocument(
    [property: JsonPropertyName("timeRange")] AgentIntentTimeRangeDocument? TimeRange,
    [property: JsonPropertyName("predicates")] IReadOnlyCollection<AgentIntentPredicateDocument> Predicates);

internal sealed record AgentIntentTimeRangeDocument(
    [property: JsonPropertyName("fromUtc")] DateTimeOffset? FromUtc,
    [property: JsonPropertyName("toUtc")] DateTimeOffset? ToUtc,
    [property: JsonPropertyName("timeZone")] string TimeZone);

internal sealed record AgentIntentPredicateDocument(
    [property: JsonPropertyName("fieldCode")] string FieldCode,
    [property: JsonPropertyName("operator")] string Operator,
    [property: JsonPropertyName("value")] string Value);

internal sealed record AgentIntentProvenanceDocument(
    [property: JsonPropertyName("routerVersion")] string RouterVersion,
    [property: JsonPropertyName("promptVersion")] string PromptVersion,
    [property: JsonPropertyName("catalogVersion")] string CatalogVersion,
    [property: JsonPropertyName("catalogDigest")] string CatalogDigest);

internal sealed record AgentCapabilityGapDocument(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("suggestedAction")] string SuggestedAction);

internal sealed record AgentPlanNodeDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("nodeId")] string NodeId,
    [property: JsonPropertyName("nodeKind")] string NodeKind,
    [property: JsonPropertyName("dependsOn")] IReadOnlyCollection<string> DependsOn,
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("inputSchemaRef")] string InputSchemaRef,
    [property: JsonPropertyName("outputSchemaRef")] string OutputSchemaRef,
    [property: JsonPropertyName("requestedToolCodes")] IReadOnlyCollection<string> RequestedToolCodes,
    [property: JsonPropertyName("requestedCapabilityCodes")] IReadOnlyCollection<string> RequestedCapabilityCodes,
    [property: JsonPropertyName("dataScopes")] IReadOnlyCollection<Guid> DataScopes,
    [property: JsonPropertyName("knowledgeScopes")] IReadOnlyCollection<Guid> KnowledgeScopes,
    [property: JsonPropertyName("evidenceSelectors")] IReadOnlyCollection<string> EvidenceSelectors,
    [property: JsonPropertyName("input")] AgentPlanNodeInputDocument? Input,
    [property: JsonPropertyName("modelPolicy")] AgentPlanModelPolicyDocument? ModelPolicy,
    [property: JsonPropertyName("timeoutPolicy")] AgentPlanTimeoutPolicyDocument TimeoutPolicy,
    [property: JsonPropertyName("retryPolicy")] AgentPlanRetryPolicyDocument RetryPolicy,
    [property: JsonPropertyName("budget")] AgentPlanNodeBudgetDocument Budget,
    [property: JsonPropertyName("approvalPolicy")] AgentPlanApprovalPolicyDocument ApprovalPolicy,
    [property: JsonPropertyName("idempotencyPolicy")] AgentPlanIdempotencyPolicyDocument IdempotencyPolicy,
    [property: JsonPropertyName("sideEffectClass")] string SideEffectClass,
    [property: JsonPropertyName("joinPolicy")] string? JoinPolicy);

internal sealed record AgentPlanNodeInputDocument(
    [property: JsonPropertyName("semanticIntent")] string? SemanticIntent,
    [property: JsonPropertyName("semanticPlanDigest")] string? SemanticPlanDigest,
    [property: JsonPropertyName("typedProvider")] string? TypedProvider,
    [property: JsonPropertyName("requestedScope")] IReadOnlyCollection<string> RequestedScope,
    [property: JsonPropertyName("maxRows")] int? MaxRows,
    [property: JsonPropertyName("executionMode")] string? ExecutionMode,
    [property: JsonPropertyName("dataSourceId")] Guid? DataSourceId,
    [property: JsonPropertyName("businessDomains")] IReadOnlyCollection<string> BusinessDomains,
    [property: JsonPropertyName("governedSchemaDigest")] string? GovernedSchemaDigest,
    [property: JsonPropertyName("requestedPermission")] string? RequestedPermission,
    [property: JsonPropertyName("canonicalInputJson")] string? CanonicalInputJson);

internal sealed record AgentPlanModelPolicyDocument(
    [property: JsonPropertyName("modelId")] Guid? ModelId,
    [property: JsonPropertyName("policyVersion")] string PolicyVersion);

internal sealed record AgentPlanTimeoutPolicyDocument(
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("timeoutSeconds")] int TimeoutSeconds);

internal sealed record AgentPlanRetryPolicyDocument(
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("maxAttempts")] int MaxAttempts,
    [property: JsonPropertyName("backoffClass")] string BackoffClass);

internal sealed record AgentPlanNodeBudgetDocument(
    [property: JsonPropertyName("maxInputTokens")] int MaxInputTokens,
    [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens,
    [property: JsonPropertyName("maxRows")] int MaxRows);

internal sealed record AgentPlanApprovalPolicyDocument(
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("policyCode")] string PolicyCode);

internal sealed record AgentPlanIdempotencyPolicyDocument(
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("mode")] string Mode);

internal sealed record AgentPlanBudgetDocument(
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("maxNodes")] int MaxNodes,
    [property: JsonPropertyName("maxElapsedSeconds")] int MaxElapsedSeconds,
    [property: JsonPropertyName("maxCanonicalBytes")] int MaxCanonicalBytes);

internal sealed record AgentPlanApprovalSummaryDocument(
    [property: JsonPropertyName("requiresPlanConfirmation")] bool RequiresPlanConfirmation,
    [property: JsonPropertyName("approvalCheckpoints")] IReadOnlyCollection<string> ApprovalCheckpoints);

internal sealed record AgentExecutionSnapshotDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("planContractPolicyVersion")] string PlanContractPolicyVersion,
    [property: JsonPropertyName("planContractVersion")] string PlanContractVersion,
    [property: JsonPropertyName("planContractDigest")] string PlanContractDigest,
    [property: JsonPropertyName("nodeContractVersion")] string NodeContractVersion,
    [property: JsonPropertyName("nodeContractDigest")] string NodeContractDigest,
    [property: JsonPropertyName("toolCatalogVersion")] int ToolCatalogVersion,
    [property: JsonPropertyName("toolCatalogDigest")] string ToolCatalogDigest,
    [property: JsonPropertyName("providerCatalogDigest")] string ProviderCatalogDigest,
    [property: JsonPropertyName("intentCatalogVersion")] string IntentCatalogVersion,
    [property: JsonPropertyName("intentCatalogDigest")] string IntentCatalogDigest,
    [property: JsonPropertyName("authorizedIntentRosterDigest")] string AuthorizedIntentRosterDigest,
    [property: JsonPropertyName("promptTemplateId")] string? PromptTemplateId,
    [property: JsonPropertyName("promptVersion")] string? PromptVersion,
    [property: JsonPropertyName("promptHash")] string? PromptHash,
    [property: JsonPropertyName("modelId")] Guid? ModelId,
    [property: JsonPropertyName("modelProvider")] string? ModelProvider,
    [property: JsonPropertyName("modelProtocol")] string? ModelProtocol,
    [property: JsonPropertyName("modelParametersHash")] string? ModelParametersHash,
    [property: JsonPropertyName("modelContextWindowTokens")] int? ModelContextWindowTokens,
    [property: JsonPropertyName("pluginCatalogDigest")] string PluginCatalogDigest,
    [property: JsonPropertyName("mcpCatalogDigest")] string McpCatalogDigest,
    [property: JsonPropertyName("dataContractVersion")] string DataContractVersion,
    [property: JsonPropertyName("dataContractDigest")] string DataContractDigest,
    [property: JsonPropertyName("authorizedDataSourceRosterDigest")] string AuthorizedDataSourceRosterDigest,
    [property: JsonPropertyName("knowledgeContractVersion")] string KnowledgeContractVersion,
    [property: JsonPropertyName("knowledgeContractDigest")] string KnowledgeContractDigest,
    [property: JsonPropertyName("authorizedKnowledgeRosterDigest")] string AuthorizedKnowledgeRosterDigest,
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("policyDigest")] string PolicyDigest,
    [property: JsonPropertyName("guardVersion")] string GuardVersion,
    [property: JsonPropertyName("guardDigest")] string GuardDigest,
    [property: JsonPropertyName("budgetPolicyVersion")] string BudgetPolicyVersion,
    [property: JsonPropertyName("concurrencyPolicyVersion")] string ConcurrencyPolicyVersion,
    [property: JsonPropertyName("maxCanonicalBytes")] int MaxCanonicalBytes);

internal sealed record AgentPlanSecuritySummaryDocument(
    [property: JsonPropertyName("cloudReadOnly")] bool CloudReadOnly,
    [property: JsonPropertyName("cloudMutationAllowed")] bool CloudMutationAllowed,
    [property: JsonPropertyName("plcControlAllowed")] bool PlcControlAllowed,
    [property: JsonPropertyName("preConfirmationExecutionAllowed")] bool PreConfirmationExecutionAllowed,
    [property: JsonPropertyName("rawPromptPersisted")] bool RawPromptPersisted,
    [property: JsonPropertyName("rawRouterReasoningPersisted")] bool RawRouterReasoningPersisted);

internal sealed record AgentEvidenceEnvelopeDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("evidenceId")] Guid EvidenceId,
    [property: JsonPropertyName("tenantId")] Guid? TenantId,
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("sessionId")] Guid SessionId,
    [property: JsonPropertyName("taskId")] Guid TaskId,
    [property: JsonPropertyName("runAttemptId")] Guid? RunAttemptId,
    [property: JsonPropertyName("nodeId")] string NodeId,
    [property: JsonPropertyName("evidenceKind")] string EvidenceKind,
    [property: JsonPropertyName("truthClass")] string TruthClass,
    [property: JsonPropertyName("producer")] AgentEvidenceProducerDocument Producer,
    [property: JsonPropertyName("source")] AgentEvidenceSourceDocument Source,
    [property: JsonPropertyName("quality")] AgentEvidenceQualityDocument Quality,
    [property: JsonPropertyName("payload")] AgentEvidencePayloadDocument Payload,
    [property: JsonPropertyName("content")] AgentEvidenceContentDocument Content,
    [property: JsonPropertyName("lineage")] AgentEvidenceLineageDocument Lineage,
    [property: JsonPropertyName("governance")] AgentEvidenceGovernanceDocument Governance,
    [property: JsonPropertyName("prediction")] AgentEvidencePredictionDocument? Prediction,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("digest")] string Digest);

internal sealed record AgentEvidenceProducerDocument(
    [property: JsonPropertyName("nodeKind")] string NodeKind,
    [property: JsonPropertyName("executorId")] string ExecutorId,
    [property: JsonPropertyName("toolCode")] string? ToolCode,
    [property: JsonPropertyName("toolSchemaHash")] string? ToolSchemaHash,
    [property: JsonPropertyName("modelId")] Guid? ModelId,
    [property: JsonPropertyName("modelVersion")] string? ModelVersion,
    [property: JsonPropertyName("promptVersion")] string? PromptVersion);

internal sealed record AgentEvidenceSourceDocument(
    [property: JsonPropertyName("sourceDomain")] string SourceDomain,
    [property: JsonPropertyName("opaqueSourceRef")] string OpaqueSourceRef,
    [property: JsonPropertyName("sourceMode")] string SourceMode,
    [property: JsonPropertyName("isSimulation")] bool IsSimulation,
    [property: JsonPropertyName("observedAtUtc")] DateTimeOffset? ObservedAtUtc,
    [property: JsonPropertyName("asOfUtc")] DateTimeOffset? AsOfUtc,
    [property: JsonPropertyName("timeRange")] AgentIntentTimeRangeDocument? TimeRange,
    [property: JsonPropertyName("sanitizedScope")] IReadOnlyCollection<string> SanitizedScope,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("providerOperationCode")] string? ProviderOperationCode,
    [property: JsonPropertyName("semanticIntent")] string? SemanticIntent,
    [property: JsonPropertyName("queryScope")] IReadOnlyCollection<string> QueryScope);

internal sealed record AgentEvidenceQualityDocument(
    [property: JsonPropertyName("rowCount")] int? RowCount,
    [property: JsonPropertyName("isTruncated")] bool IsTruncated,
    [property: JsonPropertyName("freshness")] string Freshness,
    [property: JsonPropertyName("missingRate")] double? MissingRate,
    [property: JsonPropertyName("confidence")] double? Confidence,
    [property: JsonPropertyName("qualityFlags")] IReadOnlyCollection<string> QualityFlags);

internal sealed record AgentEvidencePayloadDocument(
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("storageMode")] string StorageMode,
    [property: JsonPropertyName("payloadRef")] string? PayloadRef,
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("byteLength")] int ByteLength,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("isComplete")] bool IsComplete,
    [property: JsonPropertyName("inlineCanonicalJson")] string? InlineCanonicalJson);

internal sealed record AgentEvidenceContentDocument(
    [property: JsonPropertyName("safeSummary")] string SafeSummary,
    [property: JsonPropertyName("typedMetrics")] IReadOnlyDictionary<string, decimal> TypedMetrics,
    [property: JsonPropertyName("findings")] IReadOnlyCollection<string> Findings,
    [property: JsonPropertyName("citationRefs")] IReadOnlyCollection<string> CitationRefs,
    [property: JsonPropertyName("artifactRefs")] IReadOnlyCollection<string> ArtifactRefs);

internal sealed record AgentEvidenceLineageDocument(
    [property: JsonPropertyName("parentEvidenceIds")] IReadOnlyCollection<Guid> ParentEvidenceIds,
    [property: JsonPropertyName("inputDigest")] string InputDigest,
    [property: JsonPropertyName("outputDigest")] string OutputDigest);

internal sealed record AgentEvidenceGovernanceDocument(
    [property: JsonPropertyName("sensitivity")] string Sensitivity,
    [property: JsonPropertyName("redactionStatus")] string RedactionStatus,
    [property: JsonPropertyName("allowedConsumerScope")] IReadOnlyCollection<string> AllowedConsumerScope,
    [property: JsonPropertyName("retentionClass")] string RetentionClass);

internal sealed record AgentEvidencePredictionDocument(
    [property: JsonPropertyName("modelVersion")] string ModelVersion,
    [property: JsonPropertyName("predictionWindow")] string PredictionWindow,
    [property: JsonPropertyName("validUntilUtc")] DateTimeOffset ValidUntilUtc);
