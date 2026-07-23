using System.Text;
using AICopilot.Services.Contracts;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace AICopilot.Infrastructure.Artifacts;

internal static class AgentPptxDocumentGenerator
{
    public static Task<byte[]> GenerateAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        using (var presentation = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            var presentationPart = presentation.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation(new P.SlideIdList());
            AddSlide(presentationPart, 256U, document.Title, BuildSlideBody(document));
            presentationPart.Presentation.Save();
        }

        return Task.FromResult(stream.ToArray());
    }

    private static void AddSlide(PresentationPart presentationPart, uint slideId, string title, string body)
    {
        var slidePart = presentationPart.AddNewPart<SlidePart>();
        slidePart.Slide = new P.Slide(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()),
                    CreateTextShape(2U, "Title", title, 457200, 365760, 8229600, 914400, 2800, true),
                    CreateTextShape(3U, "Body", body, 457200, 1371600, 8229600, 4572000, 1600, false))));
        slidePart.Slide.Save();

        var presentation = presentationPart.Presentation
                           ?? throw new InvalidOperationException("Presentation part is not initialized.");
        var slideIdList = presentation.SlideIdList ?? presentation.AppendChild(new P.SlideIdList());
        slideIdList.Append(new P.SlideId
        {
            Id = slideId,
            RelationshipId = presentationPart.GetIdOfPart(slidePart)
        });
    }

    private static P.Shape CreateTextShape(
        uint id,
        string name,
        string text,
        long x,
        long y,
        long cx,
        long cy,
        int fontSize,
        bool bold)
    {
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }),
            new P.TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(
                    new A.Run(
                        new A.RunProperties { FontSize = fontSize, Bold = bold },
                        new A.Text(text)))));
    }

    private static string BuildSlideBody(AgentReportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Goal:");
        builder.AppendLine(document.Goal);
        builder.AppendLine();
        builder.AppendLine($"EvidenceSetDigest: {document.EvidenceSetDigest ?? "NOT-RECORDED"}");
        builder.AppendLine($"TruthClasses: {string.Join(", ", document.TruthClasses ?? [])}");
        builder.AppendLine($"EvidenceAsOfUtc: {document.EvidenceAsOfUtc?.ToString("O") ?? "NOT-RECORDED"}");
        if (document.AiInference is { } inference)
        {
            builder.AppendLine($"AIInference: truthClass={inference.TruthClass}; confidence={inference.Confidence:0.###}; conflictStatus={inference.ConflictStatus}");
            builder.AppendLine(inference.SafeSummary);
            foreach (var finding in inference.Findings.Take(4))
            {
                builder.AppendLine($"- {finding}");
            }
        }
        if (document.CurrentHealthAssessment is { } health)
        {
            builder.AppendLine($"CurrentHealth: {health.HealthLevel}; score={health.HealthScore}/100; truthClass={health.TruthClass}; algorithm={health.AlgorithmVersion}");
            builder.AppendLine(health.SafeSummary);
        }

        builder.AppendLine();
        builder.AppendLine("Data Source:");
        builder.AppendLine(AgentArtifactDocumentFormatting.BuildSourceMarker(document));
        if (!string.IsNullOrWhiteSpace(document.CloudReadonlySummary))
        {
            builder.AppendLine(document.CloudReadonlySummary);
        }
        foreach (var query in (document.CloudReadonlyQueries ?? []).Take(6))
        {
            builder.AppendLine($"CloudReadonly: {query.Intent}; sourceMode={query.SourceMode}; isSimulation={AgentArtifactDocumentFormatting.FormatBool(query.IsSimulation)}; sourceLabel={query.SourceLabel}; rows={query.RowCount}; truncated={AgentArtifactDocumentFormatting.FormatBool(query.IsTruncated)}; semanticPlanDigest={query.SemanticPlanDigest}");
        }

        foreach (var result in document.BusinessQueryResults ?? [])
        {
            builder.AppendLine($"BusinessDatabase: {result.DataSourceName}; sourceMode={result.SourceMode}; isSimulation={AgentArtifactDocumentFormatting.FormatBool(result.IsSimulation)}; sourceLabel={result.SourceLabel}; queryHash={result.QueryHash}; rows={result.RowCount}; truncated={AgentArtifactDocumentFormatting.FormatBool(result.IsTruncated)}");
        }

        builder.AppendLine();
        builder.AppendLine("Metrics Summary:");
        foreach (var metric in (document.Metrics ?? []).Take(8))
        {
            builder.AppendLine($"- {metric.Name}: {metric.Value}{metric.Unit ?? string.Empty}");
        }

        builder.AppendLine();
        builder.AppendLine($"Uploads: {document.UploadSummaries.Count}");
        builder.AppendLine($"Tables: {document.Tables.Count}");
        builder.AppendLine($"Sources: {document.Sources.Count}");
        return builder.ToString();
    }
}
