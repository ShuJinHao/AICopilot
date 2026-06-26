using AICopilot.Services.Contracts;

namespace AICopilot.Infrastructure.Artifacts;

public sealed class AgentTableFileParser : IAgentTableFileParser
{
    public Task<AgentReportTable?> ParseAsync(
        AgentTableFileParseRequest request,
        CancellationToken cancellationToken = default)
    {
        return AgentTableFileParserCore.ParseAsync(request, cancellationToken);
    }
}

public sealed class AgentArtifactDocumentGenerator : IAgentArtifactDocumentGenerator
{
    public Task<byte[]> GeneratePdfAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken = default)
    {
        return AgentPdfDocumentGenerator.GenerateAsync(document, cancellationToken);
    }

    public Task<byte[]> GeneratePptxAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken = default)
    {
        return AgentPptxDocumentGenerator.GenerateAsync(document, cancellationToken);
    }

    public Task<byte[]> GenerateXlsxAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken = default)
    {
        return AgentXlsxDocumentGenerator.GenerateAsync(document, cancellationToken);
    }
}
