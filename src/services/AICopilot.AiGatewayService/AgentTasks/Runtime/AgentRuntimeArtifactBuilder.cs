using System.Text.Json;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeArtifactBuilder(
    IAgentArtifactWorkspaceService workspaceService,
    IAgentArtifactDocumentGenerator documentGenerator)
{
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
                business.IsTruncated);
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
                state.CloudReadonlyIsTruncated);
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
