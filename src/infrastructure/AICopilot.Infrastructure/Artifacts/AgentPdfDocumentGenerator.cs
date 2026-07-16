using AICopilot.Services.Contracts;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace AICopilot.Infrastructure.Artifacts;

internal static class AgentPdfDocumentGenerator
{
    public static Task<byte[]> GenerateAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AgentPdfFontResolver.EnsureRegistered();

        using var pdf = new PdfDocument();
        pdf.Info.Title = document.Title;
        var page = pdf.AddPage();
        using var graphics = XGraphics.FromPdfPage(page);
        var titleFont = new XFont(AgentPdfFontResolver.FamilyName, 16, XFontStyleEx.Bold);
        var textFont = new XFont(AgentPdfFontResolver.FamilyName, 10, XFontStyleEx.Regular);
        var y = 40d;

        DrawLine(graphics, document.Title, titleFont, 40, ref y, page.Width.Point - 80);
        DrawLine(graphics, $"Goal: {document.Goal}", textFont, 40, ref y, page.Width.Point - 80);
        DrawLine(graphics, $"Generated: {document.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}", textFont, 40, ref y, page.Width.Point - 80);
        DrawLine(graphics, "Data Source:", titleFont, 40, ref y, page.Width.Point - 80);
        DrawLine(graphics, AgentArtifactDocumentFormatting.BuildSourceMarker(document), textFont, 55, ref y, page.Width.Point - 95);
        DrawLine(graphics, document.CloudReadonlySummary ?? "CloudReadonly was not accessed.", textFont, 55, ref y, page.Width.Point - 95);
        foreach (var result in document.BusinessQueryResults ?? [])
        {
            DrawLine(
                graphics,
                $"- BusinessDatabase: {result.DataSourceName}; sourceMode={result.SourceMode}; isSimulation={AgentArtifactDocumentFormatting.FormatBool(result.IsSimulation)}; sourceLabel={result.SourceLabel}; queryHash={result.QueryHash}; rows={result.RowCount}; truncated={AgentArtifactDocumentFormatting.FormatBool(result.IsTruncated)}",
                textFont,
                55,
                ref y,
                page.Width.Point - 95);
        }

        DrawLine(graphics, "Metrics Summary:", titleFont, 40, ref y, page.Width.Point - 80);
        foreach (var metric in document.Metrics ?? [])
        {
            DrawLine(
                graphics,
                $"- {metric.Name}: {metric.Value}{metric.Unit ?? string.Empty}{(string.IsNullOrWhiteSpace(metric.Source) ? string.Empty : $" ({metric.Source})")}",
                textFont,
                55,
                ref y,
                page.Width.Point - 95);
        }

        DrawLine(graphics, "Inputs:", titleFont, 40, ref y, page.Width.Point - 80);
        foreach (var upload in document.UploadSummaries.DefaultIfEmpty("No upload files."))
        {
            DrawLine(graphics, "- " + upload, textFont, 55, ref y, page.Width.Point - 95);
        }

        DrawLine(graphics, "Tables:", titleFont, 40, ref y, page.Width.Point - 80);
        foreach (var table in document.Tables.DefaultIfEmpty(new AgentReportTable("No table data", [], [])))
        {
            DrawLine(graphics, $"{table.Name}: {table.Rows.Count} rows", textFont, 55, ref y, page.Width.Point - 95);
        }

        DrawLine(graphics, "Knowledge Sources:", titleFont, 40, ref y, page.Width.Point - 80);
        foreach (var source in document.Sources.DefaultIfEmpty(new AgentReportSource("None", "No knowledge source", string.Empty)))
        {
            DrawLine(graphics, $"- {source.SourceType}: {source.Name} {source.Detail}", textFont, 55, ref y, page.Width.Point - 95);
        }

        using var stream = new MemoryStream();
        pdf.Save(stream, false);
        return Task.FromResult(stream.ToArray());
    }

    private static void DrawLine(XGraphics graphics, string text, XFont font, double x, ref double y, double width)
    {
        graphics.DrawString(text, font, XBrushes.Black, new XRect(x, y, width, 20), XStringFormats.TopLeft);
        y += 22;
    }
}
