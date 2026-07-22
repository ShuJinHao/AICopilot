using System.Text.Json;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentTaskRunStateCheckpointCodec
{
    private const string ContractVersion = "agent-run-state-checkpoint:v1";
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

        Restore(state, CanonicalJson.Canonicalize(payload.StateCheckpoint.GetRawText()));
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
                .ToArray());
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
        state.CloudReadonlyRows = snapshot.CloudReadonlyRows;
        state.CloudReadonlySourceMode = snapshot.CloudReadonlySourceMode;
        state.CloudReadonlyIsSimulation = snapshot.CloudReadonlyIsSimulation;
        state.CloudReadonlyRowCount = snapshot.CloudReadonlyRowCount;
        state.CloudReadonlyIsTruncated = snapshot.CloudReadonlyIsTruncated;
        state.BusinessQueryHash = snapshot.BusinessQueryHash;
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
        IReadOnlyCollection<AgentBusinessQueryCheckpoint> BusinessQueryResults);

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
