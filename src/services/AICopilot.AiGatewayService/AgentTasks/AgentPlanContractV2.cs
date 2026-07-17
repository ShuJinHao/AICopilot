using System.Text.Json.Serialization;

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
    public const string InlineEvidencePolicyV1 = "inline-evidence-policy:v1";

    public const int MaxPlanCanonicalBytes = 262_144;
    public const int MaxNodeInputCanonicalBytes = 8_000;
    public const int MaxInlineEvidenceCanonicalBytes = 65_536;
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

internal enum AgentPluginSelectionMode
{
    BuiltInOnly = 1,
    ExplicitAllowlist = 2
}

internal enum AgentCapabilitySelectionMode
{
    InferredFromGoal = 1,
    ExplicitAllowlist = 2
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
    [property: JsonPropertyName("toolCatalogVersion")] int ToolCatalogVersion,
    [property: JsonPropertyName("toolCatalogDigest")] string ToolCatalogDigest,
    [property: JsonPropertyName("providerCatalogDigest")] string ProviderCatalogDigest,
    [property: JsonPropertyName("intentCatalogVersion")] string IntentCatalogVersion,
    [property: JsonPropertyName("intentCatalogDigest")] string IntentCatalogDigest,
    [property: JsonPropertyName("promptTemplateId")] string PromptTemplateId,
    [property: JsonPropertyName("promptVersion")] string PromptVersion,
    [property: JsonPropertyName("promptHash")] string PromptHash,
    [property: JsonPropertyName("modelId")] Guid? ModelId,
    [property: JsonPropertyName("modelProvider")] string? ModelProvider,
    [property: JsonPropertyName("modelParametersHash")] string? ModelParametersHash,
    [property: JsonPropertyName("pluginCatalogDigest")] string PluginCatalogDigest,
    [property: JsonPropertyName("mcpCatalogDigest")] string McpCatalogDigest,
    [property: JsonPropertyName("dataContractVersion")] string DataContractVersion,
    [property: JsonPropertyName("knowledgeContractVersion")] string KnowledgeContractVersion,
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("guardVersion")] string GuardVersion,
    [property: JsonPropertyName("budgetPolicyVersion")] string BudgetPolicyVersion,
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
    [property: JsonPropertyName("lineage")] IReadOnlyCollection<Guid> Lineage,
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
    [property: JsonPropertyName("sanitizedScope")] IReadOnlyCollection<string> SanitizedScope);

internal sealed record AgentEvidenceQualityDocument(
    [property: JsonPropertyName("rowCount")] int? RowCount,
    [property: JsonPropertyName("isTruncated")] bool IsTruncated,
    [property: JsonPropertyName("freshness")] string Freshness,
    [property: JsonPropertyName("missingRate")] double? MissingRate,
    [property: JsonPropertyName("confidence")] double? Confidence,
    [property: JsonPropertyName("qualityFlags")] IReadOnlyCollection<string> QualityFlags);

internal sealed record AgentEvidencePayloadDocument(
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
