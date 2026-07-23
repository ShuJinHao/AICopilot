using System.Text;
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
    private static readonly ArtifactDraftTarget ChartTarget = new(ArtifactType.Chart, "chart-data.json", "charts/chart-data.json", "application/json", "chart");
    private static readonly ArtifactDraftTarget MarkdownTarget = new(ArtifactType.Markdown, "report.md", "draft/report.md", "text/markdown", "markdown");
    private static readonly ArtifactDraftTarget HtmlTarget = new(ArtifactType.Html, "report.html", "draft/report.html", "text/html", "html");
    private static readonly ArtifactDraftTarget PdfTarget = new(ArtifactType.Pdf, "report.pdf", "draft/report.pdf", "application/pdf", "pdf");
    private static readonly ArtifactDraftTarget PptxTarget = new(ArtifactType.Pptx, "report.pptx", "draft/report.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", "pptx");
    private static readonly ArtifactDraftTarget XlsxTarget = new(ArtifactType.Xlsx, "report.xlsx", "draft/report.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx");

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

    public async Task<object> GenerateArtifactAsync(
        string toolCode,
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var generation = toolCode switch
        {
            "generate_business_chart" or "generate_chart_data" => (ChartTarget, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(AgentReportComposer.BuildChartPayload(state), AgentRuntimeJson.Options))),
            "generate_markdown_report" => (MarkdownTarget, Encoding.UTF8.GetBytes(AgentReportComposer.BuildMarkdownReport(task, state))),
            "generate_html_report" => (HtmlTarget, Encoding.UTF8.GetBytes(AgentReportComposer.BuildHtmlReport(task, state))),
            "generate_pdf" => (PdfTarget, await documentGenerator.GeneratePdfAsync(AgentReportComposer.BuildReportDocument(task, state), cancellationToken)),
            "generate_pptx" => (PptxTarget, await documentGenerator.GeneratePptxAsync(AgentReportComposer.BuildReportDocument(task, state), cancellationToken)),
            "generate_xlsx" => (XlsxTarget, await documentGenerator.GenerateXlsxAsync(AgentReportComposer.BuildReportDocument(task, state), cancellationToken)),
            _ => throw new InvalidOperationException($"Unsupported artifact tool code: {toolCode}")
        };
        EnsureGeneratedContent(generation.Item2, generation.Item1.OutputType.ToUpperInvariant());
        return await WriteDraftAsync(workspace, step, state, generation.Item1, generation.Item2, cancellationToken);
    }

    private async Task<object> WriteDraftAsync(
        ArtifactWorkspace workspace,
        AgentStep step,
        AgentTaskRunState state,
        ArtifactDraftTarget target,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var sourceMetadata = BuildArtifactSourceMetadata(state);
        var artifact = await workspaceService.WriteDraftArtifactAsync(
            workspace,
            new AgentDraftArtifactWriteRequest(
                target.ArtifactType,
                target.FileName,
                target.RelativePath,
                content,
                target.ContentType,
                step.Id,
                sourceMetadata),
            cancellationToken);
        return BuildArtifactOutput(target.OutputType, artifact.Id.Value);
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
                EvidenceSetDigest: state.ReportEvidenceSetDigest,
                IsSimulation: business.IsSimulation,
                IsSandbox: false,
                SourceLabel: business.SourceLabel,
                QueryHash: business.QueryHash,
                ResultHash: null,
                RowCount: business.RowCount,
                IsTruncated: business.IsTruncated);
        }

        if (!string.IsNullOrWhiteSpace(state.CloudReadonlySourceMode) ||
            !string.IsNullOrWhiteSpace(state.CloudReadonlySourceLabel))
        {
            return new ArtifactSourceMetadata(
                state.CloudReadonlySourceMode,
                Boundary: null,
                EvidenceSetDigest: state.ReportEvidenceSetDigest,
                IsSimulation: state.CloudReadonlyIsSimulation,
                IsSandbox: false,
                SourceLabel: state.CloudReadonlySourceLabel,
                QueryHash: state.BusinessQueryHash,
                ResultHash: null,
                RowCount: state.CloudReadonlyRowCount,
                IsTruncated: state.CloudReadonlyIsTruncated);
        }

        if (!string.IsNullOrWhiteSpace(state.ReportEvidenceSetDigest))
        {
            return new ArtifactSourceMetadata(
                "Evidence",
                Boundary: "AuthorizedEvidenceSet",
                EvidenceSetDigest: state.ReportEvidenceSetDigest,
                IsSimulation: false,
                IsSandbox: false,
                SourceLabel: "Authorized task Evidence",
                QueryHash: null,
                ResultHash: null,
                RowCount: 0,
                IsTruncated: false);
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

    private sealed record ArtifactDraftTarget(
        ArtifactType ArtifactType,
        string FileName,
        string RelativePath,
        string ContentType,
        string OutputType);

}
