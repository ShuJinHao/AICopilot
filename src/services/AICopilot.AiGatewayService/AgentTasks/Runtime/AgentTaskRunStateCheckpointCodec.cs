using System.Text.Json;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentTaskRunStateCheckpointCodec
{
    private const string ContractVersion = "agent-run-state-checkpoint:v3";
    private const string EvidencePayloadContractVersion = "agent-node-evidence-payload:v1";

    public static string CaptureEvidencePayload(
        AgentTaskRunState state,
        string durableOutputCanonicalJson)
    {
        using var stateDocument = JsonDocument.Parse(Capture(state));
        using var outputDocument = JsonDocument.Parse(durableOutputCanonicalJson);
        return CanonicalJson.Canonicalize(JsonSerializer.Serialize(
            new AgentNodeEvidencePayload(
                EvidencePayloadContractVersion,
                stateDocument.RootElement.Clone(),
                outputDocument.RootElement.Clone()),
            AgentRuntimeJson.Options));
    }

    public static string RestoreEvidencePayload(
        AgentTaskRunState state,
        string canonicalJson)
    {
        var payload = ReadEvidencePayload(canonicalJson);
        Restore(state, CanonicalJson.Canonicalize(payload.StateCheckpoint.GetRawText()));
        return CanonicalJson.Canonicalize(payload.DurableOutput.GetRawText());
    }

    public static string MergeEvidencePayload(
        AgentTaskRunState state,
        string canonicalJson)
    {
        var payload = ReadEvidencePayload(canonicalJson);
        var branchState = new AgentTaskRunState();
        Restore(branchState, CanonicalJson.Canonicalize(payload.StateCheckpoint.GetRawText()));
        Merge(state, branchState);
        return CanonicalJson.Canonicalize(payload.DurableOutput.GetRawText());
    }

    public static string Capture(AgentTaskRunState state)
    {
        var snapshot = new AgentTaskRunStateCheckpoint(
            ContractVersion,
            state.Uploads.Select(upload => new AgentUploadCheckpoint(
                    upload.Id,
                    upload.FileName,
                    upload.ContentType,
                    upload.FileSize,
                    upload.Sha256,
                    upload.Preview))
                .OrderBy(upload => upload.Id)
                .ToArray(),
            state.ParsedData.ToArray(),
            state.Tables.ToArray(),
            state.RagResults.ToArray(),
            state.CloudReadonlyRows,
            state.CloudReadonlySourceMode,
            state.CloudReadonlyIsSimulation,
            state.CloudReadonlyRowCount,
            state.CloudReadonlyIsTruncated,
            state.BusinessQueryHash,
            state.BusinessQueryResults.Select(result => new AgentBusinessQueryCheckpoint(
                    result.DataSourceId,
                    result.SourceMode,
                    result.IsSimulation,
                    result.QueryHash,
                    result.RowCount,
                    result.IsTruncated,
                    result.ArtifactId))
                .OrderBy(result => result.DataSourceId)
                .ToArray(),
            state.CloudReadonlyQueriedAtUtc,
            state.CloudHealthAssessment,
            state.ReportEvidenceSetDigest,
            state.ReportTruthClasses,
            state.ReportEvidenceAsOfUtc,
            state.CloudReadonlyResults
                .OrderBy(result => result.Intent, StringComparer.Ordinal)
                .ThenBy(result => result.SemanticPlanDigest, StringComparer.Ordinal)
                .ToArray(),
            state.ReasoningOutcome);
        return CanonicalJson.Canonicalize(JsonSerializer.Serialize(snapshot, AgentRuntimeJson.Options));
    }

    public static void Restore(AgentTaskRunState state, string canonicalJson)
    {
        var snapshot = JsonSerializer.Deserialize<AgentTaskRunStateCheckpoint>(
            canonicalJson,
            AgentRuntimeJson.Options)
            ?? throw new InvalidOperationException("Evidence state checkpoint deserialized to null.");
        if (!string.Equals(snapshot.ContractVersion, ContractVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Evidence state checkpoint contract version is unsupported.");
        }

        state.Uploads.Clear();
        state.Uploads.AddRange(snapshot.Uploads.Select(upload => new AgentUploadSummary(
            upload.Id,
            upload.FileName,
            upload.ContentType,
            upload.FileSize,
            upload.Sha256,
            StoragePath: null,
            upload.Preview)));
        state.ParsedData.Clear();
        state.ParsedData.AddRange(snapshot.ParsedData);
        state.Tables.Clear();
        state.Tables.AddRange(snapshot.Tables);
        state.RagResults.Clear();
        state.RagResults.AddRange(snapshot.RagResults);
        state.CloudReadonlyResults.Clear();
        state.CloudReadonlyResults.AddRange((snapshot.CloudReadonlyResults ?? [])
            .OrderBy(result => result.Intent, StringComparer.Ordinal)
            .ThenBy(result => result.SemanticPlanDigest, StringComparer.Ordinal));
        RestoreScalarState(state, snapshot);
        state.CloudReadonlySummary = snapshot.CloudReadonlySourceMode is null
            ? null
            : $"Authorized evidence restored. sourceMode={snapshot.CloudReadonlySourceMode}; rows={snapshot.CloudReadonlyRowCount}; truncated={snapshot.CloudReadonlyIsTruncated.ToString().ToLowerInvariant()}.";
        state.CloudReadonlySourceLabel = snapshot.CloudReadonlySourceMode switch
        {
            "SimulationBusiness" => "AI 独立模拟业务库",
            null => null,
            _ => "AuthorizedDataSource"
        };
        state.CloudReadonlySourcePath = null;
        if (state.CloudReadonlyResults.Count != 0)
        {
            ApplyCurrentCloudSnapshot(state, state.CloudReadonlyResults[^1]);
        }
        state.BusinessQueryResults.Clear();
        state.BusinessQueryResults.AddRange(snapshot.BusinessQueryResults.Select(result =>
            new AgentBusinessQuerySummary(
                result.DataSourceId,
                "AuthorizedDataSource",
                result.SourceMode,
                result.IsSimulation,
                result.IsSimulation ? "AI 独立模拟业务库" : "AuthorizedDataSource",
                result.QueryHash,
                result.RowCount,
                result.IsTruncated,
                result.ArtifactId)));
    }

    private static void Merge(AgentTaskRunState destination, AgentTaskRunState source)
    {
        foreach (var upload in source.Uploads.OrderBy(item => item.Id))
        {
            if (destination.Uploads.All(item => item.Id != upload.Id))
            {
                destination.Uploads.Add(upload);
            }
        }
        destination.Uploads.Sort((left, right) => left.Id.CompareTo(right.Id));

        foreach (var parsed in source.ParsedData
                     .OrderBy(item => item.FileName, StringComparer.Ordinal)
                     .ThenBy(item => item.Format, StringComparer.Ordinal)
                     .ThenBy(item => item.Preview, StringComparer.Ordinal))
        {
            if (!destination.ParsedData.Contains(parsed))
            {
                destination.ParsedData.Add(parsed);
            }
        }
        destination.ParsedData.Sort((left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(left.FileName, right.FileName);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(left.Format, right.Format);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.Preview, right.Preview);
        });

        MergeCanonical(destination.Tables, source.Tables);
        MergeCanonical(destination.RagResults, source.RagResults);

        destination.MergeCloudReadonlyResults(source.CloudReadonlyResults);

        foreach (var query in source.BusinessQueryResults
                     .OrderBy(item => item.DataSourceId)
                     .ThenBy(item => item.QueryHash, StringComparer.Ordinal))
        {
            destination.BusinessQueryResults.RemoveAll(item =>
                item.DataSourceId == query.DataSourceId &&
                string.Equals(item.QueryHash, query.QueryHash, StringComparison.Ordinal));
            destination.BusinessQueryResults.Add(query);
        }
        destination.BusinessQueryResults.Sort((left, right) =>
        {
            var comparison = left.DataSourceId.CompareTo(right.DataSourceId);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.QueryHash, right.QueryHash);
        });

        if (destination.CloudReadonlyResults.Count != 0)
        {
            ApplyCurrentCloudSnapshot(destination, destination.CloudReadonlyResults[^1]);
        }
        else if (source.CloudReadonlySourceMode is not null)
        {
            ApplyCurrentCloudSnapshot(
                destination, source.CloudReadonlySummary, source.CloudReadonlyRows,
                source.CloudReadonlySourceLabel, source.CloudReadonlySourceMode,
                source.CloudReadonlyIsSimulation, source.CloudReadonlyRowCount,
                source.CloudReadonlyIsTruncated, source.CloudReadonlyQueriedAtUtc);
        }

        destination.BusinessQueryHash = source.BusinessQueryHash ?? destination.BusinessQueryHash;
        destination.CloudHealthAssessment = source.CloudHealthAssessment ?? destination.CloudHealthAssessment;
        destination.ReasoningOutcome = source.ReasoningOutcome ?? destination.ReasoningOutcome;
        if (!string.IsNullOrWhiteSpace(source.ReportEvidenceSetDigest))
        {
            destination.ReportEvidenceSetDigest = source.ReportEvidenceSetDigest;
            destination.ReportTruthClasses = source.ReportTruthClasses;
            destination.ReportEvidenceAsOfUtc = source.ReportEvidenceAsOfUtc;
        }
    }

    private static AgentNodeEvidencePayload ReadEvidencePayload(string canonicalJson)
    {
        var payload = JsonSerializer.Deserialize<AgentNodeEvidencePayload>(
            canonicalJson,
            AgentRuntimeJson.Options)
            ?? throw new InvalidOperationException("Node Evidence payload deserialized to null.");
        if (!string.Equals(
                payload.ContractVersion,
                EvidencePayloadContractVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Node Evidence payload contract version is unsupported.");
        }

        return payload;
    }

    private static void MergeCanonical<T>(List<T> destination, IEnumerable<T> source)
    {
        var known = destination
            .Select(item => CanonicalJson.Serialize(item))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in source.OrderBy(item => CanonicalJson.Serialize(item), StringComparer.Ordinal))
        {
            if (known.Add(CanonicalJson.Serialize(item)))
            {
                destination.Add(item);
            }
        }
        destination.Sort((left, right) => StringComparer.Ordinal.Compare(
            CanonicalJson.Serialize(left),
            CanonicalJson.Serialize(right)));
    }

    private static void ApplyCurrentCloudSnapshot(
        AgentTaskRunState state,
        AgentCloudReadonlyQuerySnapshot snapshot)
    {
        ApplyCurrentCloudSnapshot(
            state, snapshot.Summary, snapshot.Rows, snapshot.SourceLabel, snapshot.SourceMode,
            snapshot.IsSimulation, snapshot.RowCount, snapshot.IsTruncated, snapshot.QueriedAtUtc);
    }

    private static void ApplyCurrentCloudSnapshot(
        AgentTaskRunState state,
        string? summary,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string? sourceLabel,
        string? sourceMode,
        bool isSimulation,
        int rowCount,
        bool isTruncated,
        DateTimeOffset? queriedAtUtc)
    {
        (state.CloudReadonlySummary, state.CloudReadonlyRows, state.CloudReadonlySourceLabel, state.CloudReadonlySourcePath) =
            (summary, rows, sourceLabel, null);
        (state.CloudReadonlySourceMode, state.CloudReadonlyIsSimulation, state.CloudReadonlyRowCount) =
            (sourceMode, isSimulation, rowCount);
        (state.CloudReadonlyIsTruncated, state.CloudReadonlyQueriedAtUtc) = (isTruncated, queriedAtUtc);
    }

    private static void RestoreScalarState(
        AgentTaskRunState state,
        AgentTaskRunStateCheckpoint snapshot)
    {
        (state.CloudReadonlyRows, state.CloudReadonlySourceMode, state.CloudReadonlyIsSimulation) =
            (snapshot.CloudReadonlyRows, snapshot.CloudReadonlySourceMode, snapshot.CloudReadonlyIsSimulation);
        (state.CloudReadonlyRowCount, state.CloudReadonlyIsTruncated, state.CloudReadonlyQueriedAtUtc) =
            (snapshot.CloudReadonlyRowCount, snapshot.CloudReadonlyIsTruncated, snapshot.CloudReadonlyQueriedAtUtc);
        (state.CloudHealthAssessment, state.ReasoningOutcome, state.BusinessQueryHash) =
            (snapshot.CloudHealthAssessment, snapshot.ReasoningOutcome, snapshot.BusinessQueryHash);
        (state.ReportEvidenceSetDigest, state.ReportTruthClasses, state.ReportEvidenceAsOfUtc) =
            (snapshot.ReportEvidenceSetDigest, snapshot.ReportTruthClasses ?? [], snapshot.ReportEvidenceAsOfUtc);
    }

    private sealed record AgentTaskRunStateCheckpoint(
        string ContractVersion,
        IReadOnlyCollection<AgentUploadCheckpoint> Uploads,
        IReadOnlyCollection<AgentParsedData> ParsedData,
        IReadOnlyCollection<AgentReportTable> Tables,
        IReadOnlyCollection<AgentRagResult> RagResults,
        IReadOnlyList<Dictionary<string, object?>> CloudReadonlyRows,
        string? CloudReadonlySourceMode,
        bool CloudReadonlyIsSimulation,
        int CloudReadonlyRowCount,
        bool CloudReadonlyIsTruncated,
        string? BusinessQueryHash,
        IReadOnlyCollection<AgentBusinessQueryCheckpoint> BusinessQueryResults,
        DateTimeOffset? CloudReadonlyQueriedAtUtc = null,
        AgentCloudHealthAssessmentOutput? CloudHealthAssessment = null,
        string? ReportEvidenceSetDigest = null,
        IReadOnlyCollection<string>? ReportTruthClasses = null,
        DateTimeOffset? ReportEvidenceAsOfUtc = null,
        IReadOnlyCollection<AgentCloudReadonlyQuerySnapshot>? CloudReadonlyResults = null,
        AgentReasoningToolOutput? ReasoningOutcome = null);

    private sealed record AgentNodeEvidencePayload(
        string ContractVersion,
        JsonElement StateCheckpoint,
        JsonElement DurableOutput);

    private sealed record AgentUploadCheckpoint(
        Guid Id,
        string FileName,
        string ContentType,
        long FileSize,
        string Sha256,
        string? Preview);

    private sealed record AgentBusinessQueryCheckpoint(
        Guid DataSourceId,
        string SourceMode,
        bool IsSimulation,
        string QueryHash,
        int RowCount,
        bool IsTruncated,
        Guid? ArtifactId);

}
