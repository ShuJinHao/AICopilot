using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workflows;

public sealed record AgentTaskChatEvidenceContext(
    Guid TaskId,
    string EvidenceSetDigest,
    IReadOnlyCollection<string> TruthClasses,
    DateTimeOffset? EvidenceAsOfUtc,
    string SafeContext);

public interface IAgentTaskChatEvidenceProvider
{
    Task<Result<AgentTaskChatEvidenceContext>> BindCompletedTaskAsync(
        Guid sessionId,
        Guid userId,
        Guid taskId,
        CancellationToken cancellationToken = default);
}

internal sealed class AgentTaskChatEvidenceProvider(
    IReadRepository<AgentTask> taskRepository,
    IAgentTaskRunAttemptStore runAttemptStore,
    IAgentNodeRunStore nodeRunStore) : IAgentTaskChatEvidenceProvider
{
    private const int MaxEvidenceDocuments = 64;
    private const int MaxSafeContextCharacters = 16_000;
    private const int MaxSafeValueCharacters = 1_000;

    public async Task<Result<AgentTaskChatEvidenceContext>> BindCompletedTaskAsync(
        Guid sessionId,
        Guid userId,
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty || userId == Guid.Empty || taskId == Guid.Empty)
        {
            return Invalid(
                AppProblemCodes.RequestValidationFailed,
                "Session, user, and referenced AgentTask identities are required.");
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdForUserSpec(new AgentTaskId(taskId), userId),
            cancellationToken);
        if (task is null || task.SessionId.Value != sessionId)
        {
            return Invalid(
                AuthProblemCodes.MissingPermission,
                "The referenced AgentTask is unavailable in the current user and session scope.");
        }

        if (task.Status != AgentTaskStatus.Completed)
        {
            return Invalid(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Only a completed AgentTask can be reused as Chat evidence.");
        }

        var attempts = await runAttemptStore.ListByTaskAsync(task.Id, cancellationToken);
        var attempt = attempts
            .Where(item => item.Status == AgentTaskRunAttemptStatus.Succeeded)
            .OrderByDescending(item => item.AttemptNo)
            .ThenByDescending(item => item.CompletedAt)
            .FirstOrDefault();
        if (attempt is null)
        {
            return Invalid(
                AppProblemCodes.AgentNodeRunStateConflict,
                "The completed AgentTask has no succeeded durable RunAttempt.");
        }

        var nodes = await nodeRunStore.ListByAttemptAsync(attempt.Id, cancellationToken);
        var evidence = await nodeRunStore.ListEvidenceByAttemptAsync(attempt.Id, cancellationToken);
        if (nodes.Count == 0 || evidence.Count == 0)
        {
            return Invalid(
                AppProblemCodes.AgentNodeRunStateConflict,
                "The completed AgentTask has no durable NodeRun Evidence to bind.");
        }

        var duplicateNodeId = nodes
            .GroupBy(item => item.NodeId, StringComparer.Ordinal)
            .Any(group => group.Count() != 1);
        var duplicateEvidenceId = evidence
            .GroupBy(item => item.Id)
            .Any(group => group.Count() != 1);
        if (duplicateNodeId || duplicateEvidenceId)
        {
            return Invalid(
                AppProblemCodes.AgentNodeRunStateConflict,
                "The referenced AgentTask Evidence graph contains duplicate durable identities.");
        }

        var nodesById = nodes.ToDictionary(item => item.NodeId, StringComparer.Ordinal);
        var documentsByEvidenceId = new Dictionary<Guid, AgentEvidenceEnvelopeDocument>();
        var recordsByEvidenceId = evidence.ToDictionary(item => item.Id.Value);
        var nowUtc = DateTimeOffset.UtcNow;
        foreach (var record in evidence)
        {
            var access = AgentEvidenceAccessChecker.ValidateDurable(
                record,
                task,
                attempt.Id.Value,
                nodesById,
                nowUtc);
            if (!access.IsSuccess)
            {
                return Invalid(
                    AppProblemCodes.AgentNodeRunStateConflict,
                    "The referenced AgentTask contains Evidence outside its durable authority scope.");
            }

            AgentEvidenceEnvelopeDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<AgentEvidenceEnvelopeDocument>(
                    record.CanonicalEnvelopeJson,
                    CanonicalJson.SerializerOptions);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return Invalid(
                    AppProblemCodes.AgentNodeRunStateConflict,
                    "The referenced AgentTask contains an unreadable sealed Evidence envelope.");
            }

            if (document is null)
            {
                return Invalid(
                    AppProblemCodes.AgentNodeRunStateConflict,
                    "The referenced AgentTask contains a missing sealed Evidence envelope.");
            }

            documentsByEvidenceId.Add(record.Id.Value, document);
        }

        var finalizationNodes = nodes
            .Where(node =>
                node.Status == AgentNodeRunStatus.Succeeded &&
                string.Equals(node.NodeKind, "ApprovalCheckpointNode", StringComparison.Ordinal) &&
                string.Equals(
                    node.ToolCode,
                    BuiltInToolRegistrations.FinalizationCheckpointToolCode,
                    StringComparison.Ordinal))
            .OrderByDescending(node => node.CompletedAt)
            .ThenBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();
        if (finalizationNodes.Length != 1)
        {
            return Invalid(
                AppProblemCodes.AgentNodeRunStateConflict,
                "The completed AgentTask must have exactly one succeeded finalization checkpoint.");
        }

        var finalizationEvidence = evidence
            .Where(item => item.NodeRunId == finalizationNodes[0].Id)
            .ToArray();
        if (finalizationEvidence.Length != 1)
        {
            return Invalid(
                AppProblemCodes.AgentNodeRunStateConflict,
                "The finalization checkpoint must have exactly one sealed Evidence envelope.");
        }

        var finalDocument = documentsByEvidenceId[finalizationEvidence[0].Id.Value];
        var evidenceSetDigest = finalDocument.Lineage.EvidenceSetDigest;
        if (!string.Equals(finalDocument.EvidenceKind, AgentEvidenceKind.ArtifactReference.ToString(), StringComparison.Ordinal) ||
            !IsSha256(evidenceSetDigest) ||
            finalDocument.Lineage.ParentEvidenceIds.Count == 0)
        {
            return Invalid(
                AppProblemCodes.AgentNodeRunStateConflict,
                "The finalization checkpoint is not bound to an authoritative EvidenceSetDigest.");
        }

        var directParentRecords = new List<AgentEvidenceRecord>(finalDocument.Lineage.ParentEvidenceIds.Count);
        foreach (var parentEvidenceId in finalDocument.Lineage.ParentEvidenceIds)
        {
            if (!recordsByEvidenceId.TryGetValue(parentEvidenceId, out var parentRecord))
            {
                return Invalid(
                    AppProblemCodes.AgentNodeRunStateConflict,
                    "The finalization checkpoint references missing parent Evidence.");
            }

            directParentRecords.Add(parentRecord);
        }

        if (!AgentEvidenceSetDigestAuthority.TryComputeEffective(directParentRecords, out var effectiveDigest) ||
            !string.Equals(effectiveDigest, evidenceSetDigest, StringComparison.Ordinal))
        {
            return Invalid(
                AppProblemCodes.AgentNodeRunStateConflict,
                "The finalization checkpoint EvidenceSetDigest does not match its authoritative parent Evidence.");
        }

        var closure = new HashSet<Guid>();
        var visiting = new HashSet<Guid>();
        foreach (var parentEvidenceId in finalDocument.Lineage.ParentEvidenceIds)
        {
            if (!TryCollectEvidenceClosure(
                    parentEvidenceId,
                    documentsByEvidenceId,
                    closure,
                    visiting) ||
                closure.Count > MaxEvidenceDocuments)
            {
                return Invalid(
                    AppProblemCodes.AgentNodeRunStateConflict,
                    "The referenced AgentTask Evidence lineage is missing, cyclic, or exceeds the bounded Chat context.");
            }
        }

        var boundDocuments = closure
            .Select(id => documentsByEvidenceId[id])
            .Where(document => !string.Equals(
                document.EvidenceKind,
                AgentEvidenceKind.ArtifactReference.ToString(),
                StringComparison.Ordinal))
            .OrderBy(document => document.CreatedAtUtc)
            .ThenBy(document => document.NodeId, StringComparer.Ordinal)
            .ToArray();
        var safeContext = BuildSafeContext(boundDocuments);
        if (string.IsNullOrWhiteSpace(safeContext))
        {
            return Invalid(
                AppProblemCodes.AgentNodeRunStateConflict,
                "The referenced AgentTask has no bounded safe Evidence context for Chat reuse.");
        }

        var truthClasses = boundDocuments
            .Select(document => document.TruthClass)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var evidenceAsOfUtc = boundDocuments
            .Select(document => document.Source.AsOfUtc)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Min();
        return Result.Success(new AgentTaskChatEvidenceContext(
            task.Id.Value,
            evidenceSetDigest!,
            truthClasses,
            evidenceAsOfUtc == default ? null : evidenceAsOfUtc,
            safeContext));
    }

    private static bool TryCollectEvidenceClosure(
        Guid evidenceId,
        IReadOnlyDictionary<Guid, AgentEvidenceEnvelopeDocument> documents,
        ISet<Guid> closure,
        ISet<Guid> visiting)
    {
        if (closure.Contains(evidenceId))
        {
            return true;
        }

        if (!documents.TryGetValue(evidenceId, out var document) || !visiting.Add(evidenceId))
        {
            return false;
        }

        foreach (var parentId in document.Lineage.ParentEvidenceIds)
        {
            if (!TryCollectEvidenceClosure(parentId, documents, closure, visiting))
            {
                return false;
            }
        }

        visiting.Remove(evidenceId);
        closure.Add(evidenceId);
        return true;
    }

    private static string BuildSafeContext(IEnumerable<AgentEvidenceEnvelopeDocument> documents)
    {
        var builder = new StringBuilder();
        foreach (var document in documents)
        {
            var block = new StringBuilder();
            block.AppendLine($"truth_class={document.TruthClass}");
            block.AppendLine($"safe_summary={Bound(document.Content.SafeSummary)}");
            if (document.Source.AsOfUtc is { } asOfUtc)
            {
                block.AppendLine($"source_as_of_utc={asOfUtc:O}");
            }

            if (document.Source.TimeRange is { } timeRange)
            {
                block.AppendLine($"time_range_from_utc={timeRange.FromUtc?.ToString("O") ?? "not-recorded"}");
                block.AppendLine($"time_range_to_utc={timeRange.ToUtc?.ToString("O") ?? "not-recorded"}");
                block.AppendLine($"time_zone={Bound(timeRange.TimeZone)}");
            }

            if (document.Quality.QualityFlags.Count > 0)
            {
                block.AppendLine($"quality_flags={string.Join(",", document.Quality.QualityFlags)}");
            }

            foreach (var finding in document.Content.Findings.Take(4))
            {
                block.AppendLine($"finding={Bound(finding)}");
            }

            foreach (var citation in document.Content.CitationRefs.Take(4))
            {
                block.AppendLine($"citation_ref={Bound(citation)}");
            }

            var candidate = block.ToString().TrimEnd();
            if (candidate.Length == 0)
            {
                continue;
            }

            var separatorLength = builder.Length == 0 ? 0 : Environment.NewLine.Length * 2;
            if (builder.Length + separatorLength + candidate.Length > MaxSafeContextCharacters)
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(candidate);
        }

        return builder.ToString().Trim();
    }

    private static string Bound(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= MaxSafeValueCharacters
            ? normalized
            : normalized[..MaxSafeValueCharacters];
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static Result<AgentTaskChatEvidenceContext> Invalid(string code, string detail) =>
        Result.Failure(new ApiProblemDescriptor(code, detail));
}
