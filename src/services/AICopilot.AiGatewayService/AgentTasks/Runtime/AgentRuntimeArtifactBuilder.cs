using System.Text.Json;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeArtifactBuilder(
    IAgentArtifactWorkspaceService workspaceService,
    IAgentArtifactDocumentGenerator documentGenerator)
{
    public void BindEvidenceSet(
        AgentTaskRunState state,
        IReadOnlyCollection<AgentEvidenceRecord> inputEvidence)
    {
        if (inputEvidence.Count == 0)
        {
            state.ReportEvidenceSetDigest = null;
            state.ReportTruthClasses = [];
            state.ReportEvidenceAsOfUtc = null;
            return;
        }

        if (!AgentEvidenceSetDigestAuthority.TryComputeEffective(
                inputEvidence,
                out var evidenceSetDigest) ||
            evidenceSetDigest is null)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Artifact generation could not bind its authoritative input Evidence set.");
        }

        var documents = inputEvidence.Select(evidence =>
        {
            try
            {
                return JsonSerializer.Deserialize<AgentEvidenceEnvelopeDocument>(
                    evidence.CanonicalEnvelopeJson,
                    CanonicalJson.SerializerOptions);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return null;
            }
        }).ToArray();
        if (documents.Any(document => document is null))
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Artifact generation could not read its authorized Evidence metadata.");
        }

        var isSameEvidenceSet = string.Equals(
            state.ReportEvidenceSetDigest,
            evidenceSetDigest,
            StringComparison.Ordinal);
        var inheritedTruthClasses = isSameEvidenceSet
            ? state.ReportTruthClasses
            : [];

        state.ReportEvidenceSetDigest = evidenceSetDigest;
        state.ReportTruthClasses = inheritedTruthClasses
            .Concat(documents.Select(document => document!.TruthClass))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        state.ReportEvidenceAsOfUtc = documents
            .Select(document => document!.Source.AsOfUtc ?? document.CreatedAtUtc)
            .OrderByDescending(value => value)
            .FirstOrDefault();
    }

    public async Task<object> GenerateChartDataAsync(
    ArtifactWorkspace workspace,
    AgentStep step,
    AgentTaskRunState state,
    CancellationToken cancellationToken)
    {
        var payload = AgentReportComposer.BuildChartPayload(state);
        var content = JsonSerializer.Serialize(payload, AgentRuntimeJson.Options);
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftTextArtifactAsync(
            workspace,
            ArtifactType.Chart,
            "chart-data.json",
            "charts/chart-data.json",
            content,
            "application/json",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return BuildArtifactOutput("chart", artifact.Id.Value);
    }

    public async Task<object> GenerateMarkdownReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var markdown = AgentReportComposer.BuildMarkdownReport(task, state);
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftTextArtifactAsync(
            workspace,
            ArtifactType.Markdown,
            "report.md",
            "draft/report.md",
            markdown,
            "text/markdown",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return BuildArtifactOutput("markdown", artifact.Id.Value);
    }

    public async Task<object> GenerateHtmlReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var html = AgentReportComposer.BuildHtmlReport(task, state);
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftTextArtifactAsync(
            workspace,
            ArtifactType.Html,
            "report.html",
            "draft/report.html",
            html,
            "text/html",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return BuildArtifactOutput("html", artifact.Id.Value);
    }

    public async Task<object> GeneratePdfReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var content = await documentGenerator.GeneratePdfAsync(AgentReportComposer.BuildReportDocument(task, state), cancellationToken);
        EnsureGeneratedContent(content, "PDF");
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftBinaryArtifactAsync(
            workspace,
            ArtifactType.Pdf,
            "report.pdf",
            "draft/report.pdf",
            content,
            "application/pdf",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return BuildArtifactOutput("pdf", artifact.Id.Value);
    }

    public async Task<object> GeneratePptxReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var content = await documentGenerator.GeneratePptxAsync(AgentReportComposer.BuildReportDocument(task, state), cancellationToken);
        EnsureGeneratedContent(content, "PPTX");
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftBinaryArtifactAsync(
            workspace,
            ArtifactType.Pptx,
            "report.pptx",
            "draft/report.pptx",
            content,
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return BuildArtifactOutput("pptx", artifact.Id.Value);
    }

    public async Task<object> GenerateXlsxReportAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var content = await documentGenerator.GenerateXlsxAsync(AgentReportComposer.BuildReportDocument(task, state), cancellationToken);
        EnsureGeneratedContent(content, "XLSX");
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftBinaryArtifactAsync(
            workspace,
            ArtifactType.Xlsx,
            "report.xlsx",
            "draft/report.xlsx",
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            step.Id,
            sourceMetadata,
            cancellationToken);
        return BuildArtifactOutput("xlsx", artifact.Id.Value);
    }

    private static object BuildArtifactOutput(string artifactType, Guid artifactId)
    {
        return new
        {
            status = "completed",
            resultType = "artifact",
            artifactType,
            artifactId
        };
    }

    private static ArtifactSourceMetadata? BuildArtifactSourceMetadata(AgentTaskRunState state)
    {
        var business = state.BusinessQueryResults.LastOrDefault();
        if (business is not null)
        {
            return new ArtifactSourceMetadata(
                business.SourceMode,
                Boundary: null,
                business.IsSimulation,
                IsSandbox: false,
                business.SourceLabel,
                business.QueryHash,
                ResultHash: null,
                business.RowCount,
                business.IsTruncated,
                EvidenceSetDigest: state.ReportEvidenceSetDigest);
        }

        if (!string.IsNullOrWhiteSpace(state.CloudReadonlySourceMode) ||
            !string.IsNullOrWhiteSpace(state.CloudReadonlySourceLabel))
        {
            return new ArtifactSourceMetadata(
                state.CloudReadonlySourceMode,
                Boundary: null,
                state.CloudReadonlyIsSimulation,
                IsSandbox: false,
                state.CloudReadonlySourceLabel,
                state.BusinessQueryHash,
                ResultHash: null,
                state.CloudReadonlyRowCount,
                state.CloudReadonlyIsTruncated,
                EvidenceSetDigest: state.ReportEvidenceSetDigest);
        }

        if (!string.IsNullOrWhiteSpace(state.ReportEvidenceSetDigest))
        {
            return new ArtifactSourceMetadata(
                "Evidence",
                Boundary: "AuthorizedEvidenceSet",
                IsSimulation: false,
                IsSandbox: false,
                SourceLabel: "Authorized task Evidence",
                QueryHash: null,
                ResultHash: null,
                RowCount: 0,
                IsTruncated: false,
                EvidenceSetDigest: state.ReportEvidenceSetDigest);
        }

        return null;
    }

    private static void EnsureGeneratedContent(byte[] content, string artifactType)
    {
        if (content.Length == 0)
        {
            throw new InvalidOperationException($"{AppProblemCodes.ArtifactGenerationFailed}: {artifactType} generator returned an empty artifact.");
        }
    }

}
