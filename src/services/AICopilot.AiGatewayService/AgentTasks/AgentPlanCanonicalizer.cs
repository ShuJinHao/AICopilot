using System.Text;
using System.Text.Json;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record AgentPlanContractMetadata(
    string SchemaVersion,
    string PlanDigest,
    string TopologyProfile,
    bool IsExecutable,
    int CanonicalByteCount);

public interface IAgentPlanIntegrityValidator
{
    Result<AgentPlanContractMetadata> ValidatePersisted(
        string planJson,
        bool requireExecutable = false);
}

internal sealed record CanonicalAgentPlan(
    AgentTaskPlanDocument Document,
    string CanonicalJson,
    string Digest,
    int CanonicalByteCount);

internal sealed class AgentPlanCanonicalizer : IAgentPlanIntegrityValidator
{
    private static readonly IReadOnlySet<string> DigestExcludedRootProperties = new HashSet<string>(
        ["planDigest", "planKind", "isExecutable"],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> AllowedNodeKinds = new HashSet<string>(
        [
            "CloudReadNode",
            "GovernedDataReadNode",
            "KnowledgeReadNode",
            "FileReadNode",
            "DeterministicTransformNode",
            "ArtifactBuildNode",
            "ApprovalCheckpointNode",
            "PolicyNode"
        ],
        StringComparer.Ordinal);

    public Result<CanonicalAgentPlan> Seal(AgentTaskPlanDocument plan)
    {
        var structure = ValidateStructure(plan, verifyDigest: false, requireExecutable: false);
        if (!structure.IsSuccess)
        {
            return Result.From(structure);
        }

        try
        {
            var withoutDigest = plan with { PlanDigest = null };
            var digestSource = CanonicalJson.Canonicalize(
                JsonSerializer.Serialize(withoutDigest, CanonicalJson.SerializerOptions),
                DigestExcludedRootProperties);
            var digest = CanonicalJson.ComputeSha256(digestSource);
            var sealedPlan = plan with { PlanDigest = digest };
            var canonicalJson = CanonicalJson.Serialize(sealedPlan);
            var byteCount = Encoding.UTF8.GetByteCount(canonicalJson);
            if (byteCount > AgentPlanContractVersions.MaxPlanCanonicalBytes)
            {
                return PayloadTooLarge(byteCount);
            }

            var roundTrip = DeserializeStrict(canonicalJson);
            if (!roundTrip.IsSuccess)
            {
                return Result.From(roundTrip);
            }

            var roundTripJson = CanonicalJson.Serialize(roundTrip.Value!);
            if (!string.Equals(canonicalJson, roundTripJson, StringComparison.Ordinal))
            {
                return Invalid("Plan v2 failed canonical serialize/deserialize round-trip verification.");
            }

            return Result.Success(new CanonicalAgentPlan(
                roundTrip.Value!,
                canonicalJson,
                digest,
                byteCount));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Invalid("Plan v2 cannot be canonicalized with the frozen contract.");
        }
    }

    public Result<AgentPlanContractMetadata> ValidatePersisted(
        string planJson,
        bool requireExecutable = false)
    {
        if (string.IsNullOrWhiteSpace(planJson))
        {
            return InvalidMetadata("Plan JSON is required.");
        }

        var byteCount = Encoding.UTF8.GetByteCount(planJson);
        if (byteCount > AgentPlanContractVersions.MaxPlanCanonicalBytes)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.PlanPayloadTooLarge,
                $"Plan v2 canonical payload is {byteCount} UTF-8 bytes; maximum is {AgentPlanContractVersions.MaxPlanCanonicalBytes}."));
        }

        try
        {
            var canonicalJson = CanonicalJson.Canonicalize(planJson);
            if (!string.Equals(planJson, canonicalJson, StringComparison.Ordinal))
            {
                return InvalidMetadata("Persisted Plan v2 JSON is not in canonical form.");
            }

            var deserialized = DeserializeStrict(canonicalJson);
            if (!deserialized.IsSuccess)
            {
                return Result.From(deserialized);
            }

            var plan = deserialized.Value!;
            var structure = ValidateStructure(plan, verifyDigest: true, requireExecutable);
            if (!structure.IsSuccess)
            {
                return Result.From(structure);
            }

            var roundTripJson = CanonicalJson.Serialize(plan);
            if (!string.Equals(canonicalJson, roundTripJson, StringComparison.Ordinal))
            {
                return InvalidMetadata("Persisted Plan v2 failed fresh deserialize/canonicalize round-trip verification.");
            }

            var digestSource = CanonicalJson.Canonicalize(canonicalJson, DigestExcludedRootProperties);
            var actualDigest = CanonicalJson.ComputeSha256(digestSource);
            if (!string.Equals(plan.PlanDigest, actualDigest, StringComparison.Ordinal))
            {
                return InvalidMetadata("Persisted Plan v2 digest does not match its canonical execution contract.");
            }

            return Result.Success(new AgentPlanContractMetadata(
                plan.SchemaVersion,
                actualDigest,
                plan.TopologyProfile!,
                plan.IsExecutable,
                byteCount));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return InvalidMetadata("Persisted Plan v2 JSON is invalid or contains duplicate/unsupported fields.");
        }
    }

    private static Result ValidateStructure(
        AgentTaskPlanDocument plan,
        bool verifyDigest,
        bool requireExecutable)
    {
        if (!string.Equals(plan.SchemaVersion, AgentPlanContractVersions.PlanV2, StringComparison.Ordinal))
        {
            return InvalidResult($"Unsupported agent plan schemaVersion '{plan.SchemaVersion}'. Active plans require 2.0.");
        }

        if (plan.PlanId is null || plan.PlanId == Guid.Empty || plan.PlanVersion <= 0)
        {
            return InvalidResult("Plan v2 requires a non-empty planId and positive planVersion.");
        }

        if (verifyDigest && !IsSha256(plan.PlanDigest))
        {
            return InvalidResult("Active Plan v2 requires a lowercase SHA-256 planDigest.");
        }

        if (!string.Equals(plan.TopologyProfile, "LinearV1", StringComparison.Ordinal))
        {
            return InvalidResult("P0-P3 only support topologyProfile=LinearV1; DagV1 and implicit DAG plans are rejected.");
        }

        if (requireExecutable && (!plan.IsExecutable || !string.Equals(plan.PlanKind, AgentTaskPlanKinds.ExecutablePlan, StringComparison.Ordinal)))
        {
            return InvalidResult("Runtime requires a confirmed ExecutablePlan v2.");
        }

        if (plan.IntentCandidates is null ||
            plan.RequestedCapabilityCodes is null ||
            plan.SelectedPluginIds is null ||
            plan.ArtifactTargets is null ||
            plan.Nodes is null ||
            plan.JoinPolicies is null ||
            plan.Budgets is null ||
            plan.ApprovalSummary is null ||
            plan.ExecutionSnapshot is null ||
            plan.SecuritySummary is null ||
            plan.CapabilitySelectionMode is null ||
            plan.PluginSelectionMode is null)
        {
            return InvalidResult("Plan v2 arrays, selection modes, budgets, approval, snapshot, and security summaries must be explicit and non-null.");
        }

        var capabilities = ValidateCanonicalStringSet(plan.RequestedCapabilityCodes, "requestedCapabilityCodes");
        if (!capabilities.IsSuccess)
        {
            return capabilities;
        }

        var plugins = ValidateCanonicalGuidSet(plan.SelectedPluginIds, "selectedPluginIds");
        if (!plugins.IsSuccess)
        {
            return plugins;
        }

        var artifacts = ValidateCanonicalStringSet(plan.ArtifactTargets, "artifactTargets");
        if (!artifacts.IsSuccess)
        {
            return artifacts;
        }

        if (plan.PluginSelectionMode == AgentPluginSelectionMode.BuiltInOnly && plan.SelectedPluginIds.Count != 0)
        {
            return InvalidResult("PluginSelectionMode=BuiltInOnly requires selectedPluginIds=[].");
        }

        if (plan.PluginSelectionMode == AgentPluginSelectionMode.ExplicitAllowlist && plan.SelectedPluginIds.Count == 0)
        {
            return InvalidResult("PluginSelectionMode=ExplicitAllowlist requires at least one stable plugin id.");
        }

        if (plan.CapabilitySelectionMode == AgentCapabilitySelectionMode.InferredFromGoal &&
            plan.Nodes.Count > 0 &&
            plan.RequestedCapabilityCodes.Count == 0)
        {
            return InvalidResult("InferredFromGoal executable nodes require a non-empty canonical capability set.");
        }

        if (plan.CapabilitySelectionMode == AgentCapabilitySelectionMode.ExplicitAllowlist &&
            plan.RequestedCapabilityCodes.Count == 0 &&
            plan.Nodes.Count > 0)
        {
            return InvalidResult("An explicit empty capability allowlist cannot contain executable nodes.");
        }

        if (plan.JoinPolicies.Count != 0)
        {
            return InvalidResult("LinearV1 requires joinPolicies=[].");
        }

        if (plan.Budgets.MaxCanonicalBytes != AgentPlanContractVersions.MaxPlanCanonicalBytes ||
            plan.ExecutionSnapshot.MaxCanonicalBytes != AgentPlanContractVersions.MaxPlanCanonicalBytes ||
            !string.Equals(plan.ExecutionSnapshot.PlanContractPolicyVersion, AgentPlanContractVersions.PlanPolicyV1, StringComparison.Ordinal))
        {
            return InvalidResult("Plan v2 must freeze the 262144-byte limit and plan-contract-policy:v1 in its execution snapshot.");
        }

        if (!string.Equals(plan.ExecutionSnapshot.SchemaVersion, AgentPlanContractVersions.ExecutionSnapshotV1, StringComparison.Ordinal) ||
            !string.Equals(plan.ExecutionSnapshot.IntentCatalogVersion, AgentIntentCatalogV1.CatalogVersion, StringComparison.Ordinal) ||
            !string.Equals(plan.ExecutionSnapshot.IntentCatalogDigest, AgentIntentCatalogV1.CatalogDigest, StringComparison.Ordinal))
        {
            return InvalidResult("ExecutionSnapshot is missing the frozen v1 snapshot or Intent catalog identity.");
        }

        if (!plan.SecuritySummary.CloudReadOnly ||
            plan.SecuritySummary.CloudMutationAllowed ||
            plan.SecuritySummary.PlcControlAllowed ||
            plan.SecuritySummary.PreConfirmationExecutionAllowed ||
            plan.SecuritySummary.RawPromptPersisted ||
            plan.SecuritySummary.RawRouterReasoningPersisted)
        {
            return InvalidResult("Plan v2 securitySummary violates the permanent Cloud-readonly/PLC/raw-prompt boundary.");
        }

        var candidateCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in plan.IntentCandidates)
        {
            var candidateResult = ValidateCandidate(candidate);
            if (!candidateResult.IsSuccess)
            {
                return candidateResult;
            }

            if (!candidateCodes.Add(candidate.IntentCode))
            {
                return InvalidResult($"Duplicate IntentCandidate '{candidate.IntentCode}' is not canonical.");
            }
        }

        var nodesResult = ValidateNodes(plan.Nodes, plan.Steps);
        if (!nodesResult.IsSuccess)
        {
            return nodesResult;
        }

        foreach (var candidate in plan.IntentCandidates)
        {
            var matchingNodes = plan.Nodes
                .Where(node => node.RequestedCapabilityCodes.Contains(candidate.IntentCode, StringComparer.Ordinal))
                .ToArray();
            if (candidate.IntentClass == AgentIntentClass.CloudOnly &&
                matchingNodes.Any(node => !string.Equals(node.NodeKind, "CloudReadNode", StringComparison.Ordinal)))
            {
                return InvalidResult($"Cloud-only intent '{candidate.IntentCode}' can only map to CloudReadNode.");
            }

            if (candidate.IntentClass == AgentIntentClass.GovernedExploration &&
                matchingNodes.Any(node => !string.Equals(node.NodeKind, "GovernedDataReadNode", StringComparison.Ordinal)))
            {
                return InvalidResult($"Governed exploration intent '{candidate.IntentCode}' can only map to GovernedDataReadNode.");
            }

            if (candidate.Availability != AgentIntentAvailability.Available && matchingNodes.Length > 0)
            {
                return InvalidResult($"Unavailable or unknown intent '{candidate.IntentCode}' cannot map to an executable node.");
            }
        }

        return Result.Success();
    }

    private static Result ValidateCandidate(AgentIntentCandidateDocument candidate)
    {
        if (!string.Equals(candidate.SchemaVersion, AgentPlanContractVersions.IntentV1, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(candidate.IntentCode) ||
            string.IsNullOrWhiteSpace(candidate.ProviderCode) ||
            double.IsNaN(candidate.Confidence) ||
            double.IsInfinity(candidate.Confidence) ||
            candidate.Confidence is < 0 or > 1)
        {
            return InvalidResult("IntentCandidate has an invalid version, identity, provider, or confidence.");
        }

        if (ContainsForbiddenExecutionSemantic(candidate.IntentCode) || ContainsForbiddenExecutionSemantic(candidate.ProviderCode))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' requests Cloud mutation or PLC/control execution.");
        }

        if (candidate.Required.Source != AgentIntentRequiredSource.ExplicitUserGoal &&
            string.IsNullOrWhiteSpace(candidate.Required.RuleId))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' requires a stable ruleId for {candidate.Required.Source}.");
        }

        if (candidate.RequestedResources is null || candidate.Filters is null || candidate.Provenance is null || candidate.RequestedArtifacts is null)
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' has null typed resources, filters, artifacts, or provenance.");
        }

        var artifacts = ValidateCanonicalStringSet(candidate.RequestedArtifacts, $"{candidate.IntentCode}.requestedArtifacts");
        if (!artifacts.IsSuccess)
        {
            return artifacts;
        }

        var resourceSets = new[]
        {
            ValidateCanonicalGuidSet(candidate.RequestedResources.DataSourceIds, $"{candidate.IntentCode}.dataSourceIds"),
            ValidateCanonicalGuidSet(candidate.RequestedResources.KnowledgeBaseIds, $"{candidate.IntentCode}.knowledgeBaseIds"),
            ValidateCanonicalGuidSet(candidate.RequestedResources.UploadIds, $"{candidate.IntentCode}.uploadIds")
        };
        var failedResourceSet = resourceSets.FirstOrDefault(result => !result.IsSuccess);
        if (failedResourceSet is not null)
        {
            return failedResourceSet;
        }

        if (candidate.RequestedResources.Devices.Any(device =>
                string.IsNullOrWhiteSpace(device.ResourceType) || string.IsNullOrWhiteSpace(device.ResourceId)))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' contains an unresolved device resource.");
        }

        if (candidate.Filters.TimeRange is { } timeRange &&
            (string.IsNullOrWhiteSpace(timeRange.TimeZone) ||
             timeRange.FromUtc is null && timeRange.ToUtc is null ||
             timeRange.FromUtc > timeRange.ToUtc))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' has an invalid typed time range.");
        }

        if (candidate.Filters.Predicates.Any(predicate =>
                string.IsNullOrWhiteSpace(predicate.FieldCode) ||
                string.IsNullOrWhiteSpace(predicate.Operator) ||
                string.IsNullOrWhiteSpace(predicate.Value)))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' contains an invalid typed predicate.");
        }

        if (!IsCanonicalOrder(
                candidate.Filters.Predicates.Select(predicate => $"{predicate.FieldCode}\u001f{predicate.Operator}\u001f{predicate.Value}")))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' predicates are not in canonical order.");
        }

        var expectedProvider = candidate.IntentClass switch
        {
            AgentIntentClass.CloudOnly => "CloudAiRead",
            AgentIntentClass.KnownButUnavailable => "PredictionCatalog",
            AgentIntentClass.Unknown => "None",
            _ => null
        };
        if (expectedProvider is not null && !string.Equals(candidate.ProviderCode, expectedProvider, StringComparison.Ordinal))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' does not match its frozen provider family.");
        }

        if (candidate.IntentClass is AgentIntentClass.KnownButUnavailable or AgentIntentClass.Unknown)
        {
            if (candidate.Availability == AgentIntentAvailability.Available || candidate.CapabilityGap is null)
            {
                return InvalidResult($"Unavailable/unknown IntentCandidate '{candidate.IntentCode}' requires a structured capability gap.");
            }
        }

        if (!string.Equals(candidate.Provenance.CatalogVersion, AgentIntentCatalogV1.CatalogVersion, StringComparison.Ordinal) ||
            !string.Equals(candidate.Provenance.CatalogDigest, AgentIntentCatalogV1.CatalogDigest, StringComparison.Ordinal))
        {
            return InvalidResult($"IntentCandidate '{candidate.IntentCode}' has stale or unknown catalog provenance.");
        }

        return Result.Success();
    }

    private static Result ValidateNodes(
        IReadOnlyCollection<AgentPlanNodeDocument> nodes,
        IReadOnlyCollection<AgentTaskPlanStepDocument> steps)
    {
        var orderedNodes = nodes.ToArray();
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < orderedNodes.Length; index++)
        {
            var node = orderedNodes[index];
            if (!string.Equals(node.SchemaVersion, AgentPlanContractVersions.NodeV1, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(node.NodeId) ||
                !nodeIds.Add(node.NodeId) ||
                !AllowedNodeKinds.Contains(node.NodeKind) ||
                node.JoinPolicy is not null)
            {
                return InvalidResult($"Node at index {index} violates the frozen Node v1/LinearV1 contract.");
            }

            if (ContainsForbiddenExecutionSemantic(node.NodeKind) ||
                node.RequestedToolCodes.Any(ContainsForbiddenExecutionSemantic) ||
                node.RequestedCapabilityCodes.Any(ContainsForbiddenExecutionSemantic))
            {
                return InvalidResult($"Node '{node.NodeId}' requests Cloud mutation or PLC/control execution.");
            }

            var expectedDependencies = index == 0
                ? Array.Empty<string>()
                : [orderedNodes[index - 1].NodeId];
            if (!node.DependsOn.SequenceEqual(expectedDependencies, StringComparer.Ordinal))
            {
                return InvalidResult($"Node '{node.NodeId}' is not a strict immediate-predecessor LinearV1 node.");
            }

            var sets = new[]
            {
                ValidateCanonicalStringSet(node.RequestedToolCodes, $"{node.NodeId}.requestedToolCodes"),
                ValidateCanonicalStringSet(node.RequestedCapabilityCodes, $"{node.NodeId}.requestedCapabilityCodes"),
                ValidateCanonicalGuidSet(node.DataScopes, $"{node.NodeId}.dataScopes"),
                ValidateCanonicalGuidSet(node.KnowledgeScopes, $"{node.NodeId}.knowledgeScopes"),
                ValidateCanonicalStringSet(node.EvidenceSelectors, $"{node.NodeId}.evidenceSelectors")
            };
            var failedSet = sets.FirstOrDefault(result => !result.IsSuccess);
            if (failedSet is not null)
            {
                return failedSet;
            }

            if (node.Input?.CanonicalInputJson is { } inputJson)
            {
                var inputResult = ValidateCanonicalNodeInput(inputJson, node.NodeId);
                if (!inputResult.IsSuccess)
                {
                    return inputResult;
                }
            }

            if (node.NodeKind == "CloudReadNode" &&
                (node.Input is null ||
                 !string.Equals(node.Input.TypedProvider, "CloudAiRead", StringComparison.Ordinal) ||
                 !string.IsNullOrWhiteSpace(node.Input.ExecutionMode) ||
                 node.Input.DataSourceId is not null ||
                 !string.Equals(node.SideEffectClass, "ReadOnly", StringComparison.Ordinal)))
            {
                return InvalidResult($"CloudReadNode '{node.NodeId}' must use the typed CloudAiRead input without SQL/data-source fallback.");
            }

            if (node.NodeKind == "GovernedDataReadNode" &&
                (node.Input is null ||
                 node.Input.DataSourceId is null ||
                 node.Input.ExecutionMode is not ("GovernedSql" or "TextToSql") ||
                 !string.IsNullOrWhiteSpace(node.Input.TypedProvider) ||
                 !string.Equals(node.SideEffectClass, "ReadOnly", StringComparison.Ordinal)))
            {
                return InvalidResult($"GovernedDataReadNode '{node.NodeId}' must use its distinct governed data input contract.");
            }
        }

        foreach (var step in steps)
        {
            if (step.InputJson is { } inputJson)
            {
                var inputResult = ValidateCanonicalNodeInput(inputJson, step.Title);
                if (!inputResult.IsSuccess)
                {
                    return inputResult;
                }
            }
        }

        return Result.Success();
    }

    private static Result ValidateCanonicalNodeInput(string inputJson, string owner)
    {
        var byteCount = Encoding.UTF8.GetByteCount(inputJson);
        if (byteCount > AgentPlanContractVersions.MaxNodeInputCanonicalBytes)
        {
            return InvalidResult($"Node/tool input '{owner}' is {byteCount} UTF-8 bytes; maximum is {AgentPlanContractVersions.MaxNodeInputCanonicalBytes}.");
        }

        try
        {
            var canonical = CanonicalJson.Canonicalize(inputJson);
            return string.Equals(inputJson, canonical, StringComparison.Ordinal)
                ? Result.Success()
                : InvalidResult($"Node/tool input '{owner}' is not canonical JSON.");
        }
        catch (JsonException)
        {
            return InvalidResult($"Node/tool input '{owner}' is not valid canonical JSON.");
        }
    }

    private static Result<AgentTaskPlanDocument> DeserializeStrict(string canonicalJson)
    {
        try
        {
            var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(canonicalJson, CanonicalJson.SerializerOptions);
            return plan is null
                ? Invalid<AgentTaskPlanDocument>("Plan v2 JSON deserialized to null.")
                : Result.Success(plan);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Invalid<AgentTaskPlanDocument>("Plan v2 JSON does not match the strict frozen schema.");
        }
    }

    private static bool ContainsForbiddenExecutionSemantic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("cloudwrite", StringComparison.Ordinal) ||
               normalized.Contains("cloud_write", StringComparison.Ordinal) ||
               normalized.Contains("cloudmutation", StringComparison.Ordinal) ||
               normalized.Contains("cloud_mutation", StringComparison.Ordinal) ||
               normalized.Contains("plc", StringComparison.Ordinal) ||
               normalized.Contains("controlnode", StringComparison.Ordinal) ||
               normalized.Contains("control_action", StringComparison.Ordinal) ||
               normalized.Contains("recipe_update", StringComparison.Ordinal) ||
               normalized.Contains("recipe_write", StringComparison.Ordinal) ||
               normalized.Contains("device_disable", StringComparison.Ordinal) ||
               normalized.Contains("delete", StringComparison.Ordinal);
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static Result ValidateCanonicalStringSet(IReadOnlyCollection<string> values, string name)
    {
        if (values.Any(string.IsNullOrWhiteSpace) || !IsCanonicalOrder(values))
        {
            return InvalidResult($"{name} must be a non-null, duplicate-free ordinal canonical string array.");
        }

        return Result.Success();
    }

    private static Result ValidateCanonicalGuidSet(IReadOnlyCollection<Guid> values, string name)
    {
        if (values.Any(value => value == Guid.Empty) ||
            values.Select(value => value.ToString("D")).Distinct(StringComparer.Ordinal).Count() != values.Count ||
            !values.SequenceEqual(values.OrderBy(value => value.ToString("D"), StringComparer.Ordinal)))
        {
            return InvalidResult($"{name} must be a non-null, duplicate-free ordinal canonical stable-id array.");
        }

        return Result.Success();
    }

    private static bool IsCanonicalOrder(IEnumerable<string> values)
    {
        var array = values.ToArray();
        return array.Distinct(StringComparer.Ordinal).Count() == array.Length &&
               array.SequenceEqual(array.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static Result<CanonicalAgentPlan> PayloadTooLarge(int byteCount)
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.PlanPayloadTooLarge,
            $"Plan v2 canonical payload is {byteCount} UTF-8 bytes; maximum is {AgentPlanContractVersions.MaxPlanCanonicalBytes}."));
    }

    private static Result<CanonicalAgentPlan> Invalid(string detail) => Invalid<CanonicalAgentPlan>(detail);

    private static Result<AgentPlanContractMetadata> InvalidMetadata(string detail) => Invalid<AgentPlanContractMetadata>(detail);

    private static Result InvalidResult(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }

    private static Result<T> Invalid<T>(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }
}

internal sealed record CanonicalEvidenceEnvelope(
    AgentEvidenceEnvelopeDocument Document,
    string CanonicalJson,
    string Digest,
    int CanonicalByteCount);

internal static class AgentEvidenceCanonicalizer
{
    private static readonly IReadOnlySet<string> DigestExcludedRootProperties = new HashSet<string>(
        ["digest"],
        StringComparer.Ordinal);

    public static Result<CanonicalEvidenceEnvelope> Seal(AgentEvidenceEnvelopeDocument evidence)
    {
        if (!string.Equals(evidence.SchemaVersion, AgentPlanContractVersions.EvidenceV1, StringComparison.Ordinal) ||
            evidence.EvidenceId == Guid.Empty ||
            evidence.UserId == Guid.Empty ||
            evidence.SessionId == Guid.Empty ||
            evidence.TaskId == Guid.Empty ||
            string.IsNullOrWhiteSpace(evidence.NodeId) ||
            evidence.Payload is null)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Evidence v1 identity and envelope fields are required."));
        }

        try
        {
            if (string.Equals(evidence.Payload.StorageMode, "InlineCanonicalJson", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(evidence.Payload.InlineCanonicalJson))
                {
                    return InvalidEvidence("InlineCanonicalJson evidence requires a complete inline payload.");
                }

                var canonicalPayload = CanonicalJson.Canonicalize(evidence.Payload.InlineCanonicalJson);
                var payloadBytes = Encoding.UTF8.GetByteCount(canonicalPayload);
                if (payloadBytes > AgentPlanContractVersions.MaxInlineEvidenceCanonicalBytes)
                {
                    return InvalidEvidence($"Inline evidence is {payloadBytes} UTF-8 bytes; maximum is {AgentPlanContractVersions.MaxInlineEvidenceCanonicalBytes}.");
                }

                if (!string.Equals(canonicalPayload, evidence.Payload.InlineCanonicalJson, StringComparison.Ordinal) ||
                    evidence.Payload.ByteLength != payloadBytes ||
                    !string.Equals(evidence.Payload.Sha256, CanonicalJson.ComputeSha256(canonicalPayload), StringComparison.Ordinal) ||
                    !evidence.Payload.IsComplete ||
                    evidence.Payload.PayloadRef is not null)
                {
                    return InvalidEvidence("Inline evidence payload metadata does not match its canonical bytes.");
                }
            }
            else if (string.Equals(evidence.Payload.StorageMode, "ArtifactReference", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(evidence.Payload.PayloadRef) ||
                    evidence.Payload.InlineCanonicalJson is not null ||
                    !evidence.Payload.IsComplete ||
                    evidence.Payload.ByteLength < 0 ||
                    evidence.Payload.Sha256.Length != 64)
                {
                    return InvalidEvidence("ArtifactReference evidence requires a complete opaque ref, byte length, and SHA-256 digest.");
                }
            }
            else
            {
                return InvalidEvidence("Evidence storageMode must be InlineCanonicalJson or ArtifactReference.");
            }

            var unsigned = evidence with { Digest = string.Empty };
            var digestSource = CanonicalJson.Canonicalize(
                JsonSerializer.Serialize(unsigned, CanonicalJson.SerializerOptions),
                DigestExcludedRootProperties);
            var digest = CanonicalJson.ComputeSha256(digestSource);
            var sealedEvidence = evidence with { Digest = digest };
            var canonical = CanonicalJson.Serialize(sealedEvidence);
            return Result.Success(new CanonicalEvidenceEnvelope(
                sealedEvidence,
                canonical,
                digest,
                Encoding.UTF8.GetByteCount(canonical)));
        }
        catch (JsonException)
        {
            return InvalidEvidence("Evidence v1 contains invalid canonical JSON.");
        }
    }

    private static Result<CanonicalEvidenceEnvelope> InvalidEvidence(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentPlanInvalid,
            detail));
    }
}
