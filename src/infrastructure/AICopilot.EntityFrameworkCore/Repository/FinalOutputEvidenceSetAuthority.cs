using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.SharedKernel.Ai;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal static class FinalOutputEvidenceSetAuthority
{
    private const string EvidenceSchemaVersion = "evidence:v1";

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false
        };

    private static readonly IReadOnlySet<string> DigestExcludedRootProperties =
        new HashSet<string>(["digest"], StringComparer.Ordinal);

    public static async Task<string?> ComputeAsync(
        AiGatewayDbContext context,
        AgentTask task,
        AgentTaskRunAttempt attempt,
        AgentNodeRun finalNode,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        string[] dependencies;
        try
        {
            dependencies = JsonSerializer.Deserialize<string[]>(
                               finalNode.DependenciesJson,
                               SerializerOptions)
                           ?? [];
        }
        catch (JsonException)
        {
            return null;
        }

        if (dependencies.Length == 0 ||
            dependencies.Any(string.IsNullOrWhiteSpace) ||
            dependencies.Distinct(StringComparer.Ordinal).Count() != dependencies.Length)
        {
            return null;
        }

        var nodes = await context.AgentNodeRuns
            .FromSqlInterpolated($$"""
                SELECT node.*, node.xmin
                FROM aigateway.agent_node_runs AS node
                WHERE run_attempt_id = {{attempt.Id.Value}}
                ORDER BY id
                FOR SHARE
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var evidence = await context.AgentEvidenceRecords
            .FromSqlInterpolated($$"""
                SELECT evidence.*, evidence.xmin
                FROM aigateway.agent_evidence_records AS evidence
                WHERE run_attempt_id = {{attempt.Id.Value}}
                  AND is_revoked = FALSE
                ORDER BY id
                FOR SHARE
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var selected = new List<AgentEvidenceRecord>(dependencies.Length);
        foreach (var dependency in dependencies)
        {
            var producers = nodes
                .Where(node => string.Equals(node.NodeId, dependency, StringComparison.Ordinal))
                .ToArray();
            if (producers.Length != 1)
            {
                return null;
            }

            var producer = producers[0];
            var matches = evidence
                .Where(item => string.Equals(item.NodeId, dependency, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0 &&
                finalNode.JoinPolicy == "OptionalBestEffort" &&
                !producer.IsRequired &&
                producer.Status is AgentNodeRunStatus.Failed or AgentNodeRunStatus.Cancelled)
            {
                continue;
            }

            if (matches.Length != 1 ||
                !MatchesEvidenceAuthority(
                    matches[0],
                    task,
                    attempt,
                    producer,
                    observedAtUtc,
                    out _))
            {
                return null;
            }

            selected.Add(matches[0]);
        }

        return TryComputeEffectiveDigest(selected, out var digest)
            ? digest
            : null;
    }

    private static bool MatchesEvidenceAuthority(
        AgentEvidenceRecord evidence,
        AgentTask task,
        AgentTaskRunAttempt attempt,
        AgentNodeRun producer,
        DateTimeOffset observedAtUtc,
        out string? inheritedEvidenceSetDigest)
    {
        inheritedEvidenceSetDigest = null;
        if (producer.Status != AgentNodeRunStatus.Succeeded ||
            producer.EvidenceId != evidence.Id ||
            evidence.TaskId != task.Id ||
            evidence.UserId != task.UserId ||
            evidence.SessionId != task.SessionId ||
            evidence.RunAttemptId != attempt.Id ||
            evidence.NodeRunId != producer.Id ||
            !string.Equals(evidence.NodeId, producer.NodeId, StringComparison.Ordinal) ||
            evidence.TaskFencingToken != task.RunFencingToken ||
            evidence.TaskFencingToken != attempt.TaskFencingToken ||
            evidence.TaskFencingToken != producer.TaskFencingToken ||
            evidence.NodeFencingToken != producer.NodeFencingToken ||
            evidence.IsRevoked ||
            evidence.ExpiresAt is { } expiresAt && expiresAt <= observedAtUtc ||
            !string.Equals(evidence.OutputDigest, producer.OutputDigest, StringComparison.Ordinal) ||
            !IsSha256(evidence.EnvelopeDigest) ||
            !IsSha256(evidence.OutputDigest) ||
            !IsSha256(evidence.PayloadSha256))
        {
            return false;
        }

        var expectedScopes = new[]
        {
            $"session:{task.SessionId.Value:D}",
            $"task:{task.Id.Value:D}",
            $"user:{task.UserId:D}"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();

        try
        {
            var canonicalScopes = AgentCanonicalJsonV1.Canonicalize(
                evidence.AllowedConsumerScopeJson);
            var expectedCanonicalScopes = AgentCanonicalJsonV1.Canonicalize(
                JsonSerializer.Serialize(expectedScopes, SerializerOptions));
            if (!string.Equals(
                    canonicalScopes,
                    evidence.AllowedConsumerScopeJson,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    canonicalScopes,
                    expectedCanonicalScopes,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var canonicalEnvelope = AgentCanonicalJsonV1.Canonicalize(
                evidence.CanonicalEnvelopeJson);
            var digestSource = AgentCanonicalJsonV1.Canonicalize(
                evidence.CanonicalEnvelopeJson,
                DigestExcludedRootProperties);
            if (!string.Equals(
                    canonicalEnvelope,
                    evidence.CanonicalEnvelopeJson,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    Hash(digestSource),
                    evidence.EnvelopeDigest,
                    StringComparison.Ordinal))
            {
                return false;
            }

            using var document = JsonDocument.Parse(canonicalEnvelope);
            var root = document.RootElement;
            var payload = root.GetProperty("payload");
            var lineage = root.GetProperty("lineage");
            var governance = root.GetProperty("governance");
            if (!string.Equals(
                    root.GetProperty("schemaVersion").GetString(),
                    EvidenceSchemaVersion,
                    StringComparison.Ordinal) ||
                root.GetProperty("evidenceId").GetGuid() != evidence.Id.Value ||
                !MatchesNullableGuid(root.GetProperty("tenantId"), evidence.TenantId) ||
                root.GetProperty("userId").GetGuid() != evidence.UserId ||
                root.GetProperty("sessionId").GetGuid() != evidence.SessionId.Value ||
                root.GetProperty("taskId").GetGuid() != evidence.TaskId.Value ||
                root.GetProperty("runAttemptId").GetGuid() != evidence.RunAttemptId.Value ||
                !string.Equals(
                    root.GetProperty("nodeId").GetString(),
                    evidence.NodeId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    root.GetProperty("evidenceKind").GetString(),
                    evidence.EvidenceKind.ToString(),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    root.GetProperty("truthClass").GetString(),
                    evidence.TruthClass.ToString(),
                    StringComparison.Ordinal) ||
                root.GetProperty("createdAtUtc").GetDateTimeOffset() == default ||
                !string.Equals(
                    root.GetProperty("digest").GetString(),
                    evidence.EnvelopeDigest,
                    StringComparison.Ordinal) ||
                !MatchesStringArray(
                    governance.GetProperty("allowedConsumerScope"),
                    expectedScopes) ||
                !string.Equals(
                    payload.GetProperty("storageMode").GetString(),
                    evidence.StorageMode.ToString(),
                    StringComparison.Ordinal) ||
                !MatchesNullableString(
                    payload.GetProperty("payloadRef"),
                    evidence.PayloadRef) ||
                !string.Equals(
                    payload.GetProperty("mediaType").GetString(),
                    evidence.MediaType,
                    StringComparison.Ordinal) ||
                payload.GetProperty("byteLength").GetInt32() != evidence.ByteLength ||
                !string.Equals(
                    payload.GetProperty("sha256").GetString(),
                    evidence.PayloadSha256,
                    StringComparison.Ordinal) ||
                !payload.GetProperty("isComplete").GetBoolean() ||
                !MatchesNullableString(
                    payload.GetProperty("inlineCanonicalJson"),
                    evidence.InlinePayloadJson) ||
                !string.Equals(
                    lineage.GetProperty("outputDigest").GetString(),
                    evidence.OutputDigest,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var inherited = lineage.GetProperty("evidenceSetDigest");
            inheritedEvidenceSetDigest = inherited.ValueKind == JsonValueKind.Null
                ? null
                : inherited.GetString();
            return inheritedEvidenceSetDigest is null ||
                   IsSha256(inheritedEvidenceSetDigest);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or KeyNotFoundException
                or FormatException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryComputeEffectiveDigest(
        IReadOnlyCollection<AgentEvidenceRecord> evidence,
        out string? digest)
    {
        digest = null;
        if (evidence.Count == 0 ||
            evidence.Select(item => item.Id).Distinct().Count() != evidence.Count)
        {
            return false;
        }

        var components = new List<(bool Inherited, string Digest)>(evidence.Count);
        foreach (var item in evidence)
        {
            try
            {
                using var document = JsonDocument.Parse(item.CanonicalEnvelopeJson);
                var inherited = document.RootElement
                    .GetProperty("lineage")
                    .GetProperty("evidenceSetDigest");
                var inheritedDigest = inherited.ValueKind == JsonValueKind.Null
                    ? null
                    : inherited.GetString();
                if (inheritedDigest is not null && !IsSha256(inheritedDigest))
                {
                    return false;
                }

                components.Add((
                    inheritedDigest is not null,
                    inheritedDigest ?? item.EnvelopeDigest));
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                return false;
            }
        }

        var inheritedDigests = components
            .Where(component => component.Inherited)
            .Select(component => component.Digest)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        digest = components.All(component => component.Inherited) && inheritedDigests.Length == 1
            ? inheritedDigests[0]
            : Hash(AgentCanonicalJsonV1.Canonicalize(JsonSerializer.Serialize(
                components
                    .Select(component => $"{(component.Inherited ? "set" : "evidence")}:{component.Digest}")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
                SerializerOptions)));
        return true;
    }

    private static bool MatchesNullableGuid(JsonElement element, Guid? expected) =>
        expected is null
            ? element.ValueKind == JsonValueKind.Null
            : element.ValueKind == JsonValueKind.String && element.GetGuid() == expected.Value;

    private static bool MatchesNullableString(JsonElement element, string? expected) =>
        expected is null
            ? element.ValueKind == JsonValueKind.Null
            : element.ValueKind == JsonValueKind.String &&
              string.Equals(element.GetString(), expected, StringComparison.Ordinal);

    private static bool MatchesStringArray(
        JsonElement element,
        IReadOnlyCollection<string> expected)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var actual = element.EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        return actual.All(item => item is not null) &&
               actual.Cast<string>().SequenceEqual(expected, StringComparer.Ordinal);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
