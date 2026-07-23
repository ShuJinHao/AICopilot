using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentEvidenceAccessChecker
{
    public static string[] BuildChatScopes(Guid sessionId) =>
        [$"session:{sessionId:D}"];

    public static string[] BuildDurableScopes(
        Guid sessionId,
        Guid taskId,
        Guid userId) =>
        new[]
        {
            $"session:{sessionId:D}",
            $"task:{taskId:D}",
            $"user:{userId:D}"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();

    public static bool HasExactScopes(
        IReadOnlyCollection<string> actual,
        IReadOnlyCollection<string> expected)
    {
        return actual.Count == expected.Count &&
               actual.SequenceEqual(expected, StringComparer.Ordinal);
    }

    public static bool TryReadScopes(
        string canonicalJson,
        out string[] scopes)
    {
        try
        {
            scopes = JsonSerializer.Deserialize<string[]>(
                canonicalJson,
                CanonicalJson.SerializerOptions) ?? [];
            return scopes.Length > 0 &&
                   scopes.All(value => !string.IsNullOrWhiteSpace(value)) &&
                   scopes.Distinct(StringComparer.Ordinal).Count() == scopes.Length &&
                   scopes.SequenceEqual(
                       scopes.OrderBy(value => value, StringComparer.Ordinal),
                       StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            scopes = [];
            return false;
        }
    }

    public static Result ValidateDurable(
        AgentEvidenceRecord evidence,
        AgentTask task,
        Guid runAttemptId,
        IReadOnlyDictionary<string, AgentNodeRun> producerNodes,
        DateTimeOffset nowUtc)
    {
        producerNodes.TryGetValue(evidence.NodeId, out var producerNode);
        return ValidateDurable(evidence, task, runAttemptId, producerNode, nowUtc);
    }

    public static Result ValidateDurable(
        AgentEvidenceRecord evidence,
        AgentTask task,
        Guid runAttemptId,
        AgentNodeRun? producerNode,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(task);

        if (runAttemptId == Guid.Empty ||
            evidence.TaskId != task.Id ||
            evidence.UserId != task.UserId ||
            evidence.SessionId != task.SessionId ||
            evidence.RunAttemptId.Value != runAttemptId ||
            producerNode is null ||
            evidence.NodeRunId != producerNode.Id ||
            !string.Equals(evidence.NodeId, producerNode.NodeId, StringComparison.Ordinal) ||
            evidence.IsRevoked ||
            evidence.ExpiresAt is { } expiresAt && expiresAt <= nowUtc)
        {
            return Invalid("Evidence identity, producer NodeRun, or lifetime is invalid for this task attempt.");
        }

        if (!TryReadScopes(evidence.AllowedConsumerScopeJson, out var scopes) ||
            !HasExactScopes(
                scopes,
                BuildDurableScopes(task.SessionId.Value, task.Id.Value, task.UserId)))
        {
            return Invalid("Evidence consumer authority does not exactly match the current session, task, and user.");
        }

        try
        {
            var document = JsonSerializer.Deserialize<AgentEvidenceEnvelopeDocument>(
                evidence.CanonicalEnvelopeJson,
                CanonicalJson.SerializerOptions);
            if (document is null)
            {
                return Invalid("Evidence canonical envelope is missing.");
            }

            var sealedEnvelope = AgentEvidenceCanonicalizer.Seal(document);
            if (!sealedEnvelope.IsSuccess)
            {
                return Invalid("Evidence canonical envelope failed schema or digest validation.");
            }

            var canonical = sealedEnvelope.Value!;
            if (!string.Equals(canonical.CanonicalJson, evidence.CanonicalEnvelopeJson, StringComparison.Ordinal) ||
                !string.Equals(canonical.Digest, evidence.EnvelopeDigest, StringComparison.Ordinal) ||
                canonical.Document.EvidenceId != evidence.Id.Value ||
                canonical.Document.UserId != evidence.UserId ||
                canonical.Document.SessionId != evidence.SessionId.Value ||
                canonical.Document.TaskId != evidence.TaskId.Value ||
                canonical.Document.RunAttemptId != evidence.RunAttemptId.Value ||
                !string.Equals(canonical.Document.NodeId, evidence.NodeId, StringComparison.Ordinal) ||
                !string.Equals(canonical.Document.EvidenceKind, evidence.EvidenceKind.ToString(), StringComparison.Ordinal) ||
                !string.Equals(canonical.Document.TruthClass, evidence.TruthClass.ToString(), StringComparison.Ordinal) ||
                !HasExactScopes(canonical.Document.Governance.AllowedConsumerScope, scopes) ||
                !string.Equals(canonical.Document.Payload.StorageMode, evidence.StorageMode.ToString(), StringComparison.Ordinal) ||
                !string.Equals(canonical.Document.Payload.PayloadRef, evidence.PayloadRef, StringComparison.Ordinal) ||
                !string.Equals(canonical.Document.Payload.MediaType, evidence.MediaType, StringComparison.Ordinal) ||
                canonical.Document.Payload.ByteLength != evidence.ByteLength ||
                !string.Equals(canonical.Document.Payload.Sha256, evidence.PayloadSha256, StringComparison.Ordinal) ||
                !string.Equals(canonical.Document.Lineage.OutputDigest, evidence.OutputDigest, StringComparison.Ordinal))
            {
                return Invalid("Evidence record does not match its sealed canonical envelope.");
            }

            if (evidence.StorageMode == AgentEvidenceStorageMode.InlineCanonicalJson &&
                !string.Equals(
                    canonical.Document.Payload.InlineCanonicalJson,
                    evidence.InlinePayloadJson,
                    StringComparison.Ordinal))
            {
                return Invalid("Inline Evidence payload does not match its sealed canonical envelope.");
            }

            return Result.Success();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Invalid("Evidence canonical envelope cannot be parsed safely.");
        }
    }

    private static Result Invalid(string detail)
    {
        AgentRuntimeTelemetry.RecordEvidenceAccessReject();
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentNodeRunStateConflict,
            detail));
    }
}

internal static class AgentEvidenceSelector
{
    public static Result<IReadOnlyCollection<AgentEvidenceRecord>> SelectForNode(
        AgentPlanNodeDocument nodeContract,
        IReadOnlyCollection<AgentEvidenceRecord> availableEvidence,
        AgentTask task,
        Guid runAttemptId,
        IReadOnlyDictionary<string, AgentNodeRun> producerNodes,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(nodeContract);

        if (!nodeContract.EvidenceSelectors.SequenceEqual(
                nodeContract.DependsOn,
                StringComparer.Ordinal))
        {
            return Invalid("Evidence selectors must exactly match dependency NodeIds.");
        }

        var selected = new List<AgentEvidenceRecord>(nodeContract.EvidenceSelectors.Count);
        foreach (var selector in nodeContract.EvidenceSelectors)
        {
            var matches = availableEvidence
                .Where(evidence => string.Equals(evidence.NodeId, selector, StringComparison.Ordinal))
                .ToArray();
            if (!producerNodes.TryGetValue(selector, out var producerNode))
            {
                return Invalid($"Evidence selector '{selector}' has no authoritative producer NodeRun.");
            }

            if (matches.Length == 0 &&
                nodeContract.JoinPolicy == "OptionalBestEffort" &&
                !producerNode.IsRequired &&
                (producerNode.Status is AgentNodeRunStatus.Failed or AgentNodeRunStatus.Cancelled))
            {
                continue;
            }

            if (matches.Length != 1)
            {
                return Invalid($"Evidence selector '{selector}' did not resolve to exactly one authoritative producer.");
            }

            var access = AgentEvidenceAccessChecker.ValidateDurable(
                matches[0],
                task,
                runAttemptId,
                producerNode,
                nowUtc);
            if (!access.IsSuccess)
            {
                return Result.From(access);
            }

            selected.Add(matches[0]);
        }

        return Result.Success<IReadOnlyCollection<AgentEvidenceRecord>>(selected);
    }

    private static Result<IReadOnlyCollection<AgentEvidenceRecord>> Invalid(string detail)
    {
        AgentRuntimeTelemetry.RecordEvidenceAccessReject();
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentNodeRunStateConflict,
            detail));
    }
}

internal static class AgentEvidenceSetDigestAuthority
{
    public static Guid[] OrderedIds(IEnumerable<AgentEvidenceRecord> evidence) => evidence
        .Select(item => item.Id.Value)
        .Distinct()
        .Order()
        .ToArray();

    public static bool TryComputeEffective(
        IReadOnlyCollection<AgentEvidenceRecord> evidence,
        out string? digest)
    {
        digest = null;
        if (evidence.Count == 0)
        {
            return true;
        }

        if (evidence.Select(item => item.Id).Distinct().Count() != evidence.Count)
        {
            return false;
        }

        var components = new List<(bool Inherited, string Digest)>(evidence.Count);
        foreach (var item in evidence)
        {
            if (!IsSha256(item.EnvelopeDigest))
            {
                return false;
            }

            AgentEvidenceEnvelopeDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<AgentEvidenceEnvelopeDocument>(
                    item.CanonicalEnvelopeJson,
                    CanonicalJson.SerializerOptions);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return false;
            }

            if (document is null)
            {
                return false;
            }

            var sealedEnvelope = AgentEvidenceCanonicalizer.Seal(document);
            if (!sealedEnvelope.IsSuccess ||
                !string.Equals(sealedEnvelope.Value!.CanonicalJson, item.CanonicalEnvelopeJson, StringComparison.Ordinal) ||
                !string.Equals(sealedEnvelope.Value.Digest, item.EnvelopeDigest, StringComparison.Ordinal))
            {
                return false;
            }

            var inherited = document.Lineage.EvidenceSetDigest;
            if (inherited is not null && !IsSha256(inherited))
            {
                return false;
            }

            components.Add((inherited is not null, inherited ?? item.EnvelopeDigest));
        }

        var inheritedDigests = components
            .Where(component => component.Inherited)
            .Select(component => component.Digest)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (components.All(component => component.Inherited) && inheritedDigests.Length == 1)
        {
            digest = inheritedDigests[0];
            return true;
        }

        digest = CanonicalJson.ComputeSha256(CanonicalJson.Serialize(
            components
                .Select(component => $"{(component.Inherited ? "set" : "evidence")}:{component.Digest}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()));
        return true;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
