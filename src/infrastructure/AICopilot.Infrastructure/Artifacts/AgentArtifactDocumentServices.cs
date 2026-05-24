using System.Globalization;
using System.Text;
using System.Text.Json;
using AICopilot.Services.Contracts;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace AICopilot.Infrastructure.Artifacts;

public sealed class AgentTableFileParser : IAgentTableFileParser
{
    private const int MaxRows = 200;

    public async Task<AgentReportTable?> ParseAsync(
        AgentTableFileParseRequest request,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(request.FileName);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return await ParseCsvAsync(request.FileName, request.Content, cancellationToken);
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return await ParseJsonAsync(request.FileName, request.Content, cancellationToken);
        }

        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return await ParseXlsxAsync(request.FileName, request.Content, cancellationToken);
        }

        return null;
    }

    private static async Task<AgentReportTable?> ParseCsvAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var lines = new List<IReadOnlyList<string>>();
        while (lines.Count <= MaxRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(ParseCsvLine(line));
            }
        }

        return BuildTable(fileName, lines);
    }

    private static async Task<AgentReportTable?> ParseJsonAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var rows = ExtractJsonRows(document.RootElement).Take(MaxRows).ToArray();
        if (rows.Length == 0)
        {
            return null;
        }

        var columns = rows
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedRows = rows
            .Select(row => columns.ToDictionary(
                column => column,
                column => row.TryGetValue(column, out var value) ? value : string.Empty,
                StringComparer.OrdinalIgnoreCase))
            .Cast<IReadOnlyDictionary<string, string>>()
            .ToArray();
        return new AgentReportTable(fileName, columns, normalizedRows);
    }

    private static async Task<AgentReportTable?> ParseXlsxAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using var spreadsheet = SpreadsheetDocument.Open(buffer, false);
        var workbookPart = spreadsheet.WorkbookPart;
        if (workbookPart?.Workbook?.Sheets is null)
        {
            return null;
        }

        var sheet = workbookPart.Workbook.Sheets.Elements<Sheet>().FirstOrDefault();
        if (sheet?.Id is null)
        {
            return null;
        }

        var worksheetPart = workbookPart.GetPartById(sheet.Id!.Value!) as WorksheetPart;
        if (worksheetPart?.Worksheet is null)
        {
            return null;
        }

        var rows = worksheetPart.Worksheet.Descendants<Row>().Take(MaxRows + 1).ToArray();
        if (rows is null || rows.Length == 0)
        {
            return null;
        }

        var values = rows
            .Select(row => row.Elements<Cell>()
                .Select(cell => ReadCellValue(workbookPart, cell))
                .ToArray())
            .Cast<IReadOnlyList<string>>()
            .ToArray();
        return BuildTable(fileName, values);
    }

    private static IEnumerable<IReadOnlyDictionary<string, string>> ExtractJsonRows(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (TryJsonObjectToRow(item, out var row))
                {
                    yield return row;
                }
            }
        }
        else if (TryJsonObjectToRow(root, out var singleRow))
        {
            yield return singleRow;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in property.Value.EnumerateArray())
                {
                    if (TryJsonObjectToRow(item, out var row))
                    {
                        yield return row;
                    }
                }
            }
        }
    }

    private static bool TryJsonObjectToRow(JsonElement element, out IReadOnlyDictionary<string, string> row)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            row = new Dictionary<string, string>();
            return false;
        }

        row = element.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => JsonValueToString(property.Value),
                StringComparer.OrdinalIgnoreCase);
        return row.Count > 0;
    }

    private static string JsonValueToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => value.GetRawText()
        };
    }

    private static AgentReportTable? BuildTable(string fileName, IReadOnlyList<IReadOnlyList<string>> lines)
    {
        var nonEmpty = lines.Where(line => line.Count > 0 && line.Any(value => !string.IsNullOrWhiteSpace(value))).ToArray();
        if (nonEmpty.Length == 0)
        {
            return null;
        }

        var headerWidth = nonEmpty.Max(line => line.Count);
        var rawHeaders = nonEmpty[0];
        var columns = MakeUniqueColumns(Enumerable.Range(0, headerWidth)
            .Select(index =>
            {
                var header = index < rawHeaders.Count ? rawHeaders[index].Trim() : string.Empty;
                return string.IsNullOrWhiteSpace(header) ? $"Column{index + 1}" : header;
            }));

        var rows = nonEmpty
            .Skip(1)
            .Take(MaxRows)
            .Select(line => columns.ToDictionary(
                column => column,
                column =>
                {
                    var index = Array.IndexOf(columns, column);
                    return index >= 0 && index < line.Count ? line[index] : string.Empty;
                },
                StringComparer.OrdinalIgnoreCase))
            .Cast<IReadOnlyDictionary<string, string>>()
            .ToArray();
        return new AgentReportTable(fileName, columns, rows);
    }

    private static string[] MakeUniqueColumns(IEnumerable<string> columns)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return columns.Select(column =>
            {
                if (!counts.TryAdd(column, 1))
                {
                    counts[column]++;
                    return $"{column}_{counts[column]}";
                }

                return column;
            })
            .ToArray();
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                values.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        values.Add(builder.ToString().Trim());
        return values;
    }

    private static string ReadCellValue(WorkbookPart workbookPart, Cell cell)
    {
        var value = cell.CellValue?.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return workbookPart.SharedStringTablePart?.SharedStringTable?
                .Elements<SharedStringItem>()
                .ElementAtOrDefault(index)
                ?.InnerText ?? string.Empty;
        }

        return value;
    }
}

public sealed class AgentArtifactDocumentGenerator : IAgentArtifactDocumentGenerator
{
    public Task<byte[]> GeneratePdfAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken = default)
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
        DrawLine(graphics, BuildSourceMarker(document), textFont, 55, ref y, page.Width.Point - 95);
        DrawLine(graphics, document.CloudReadonlySummary ?? "CloudReadonly was not accessed.", textFont, 55, ref y, page.Width.Point - 95);
        foreach (var result in document.BusinessQueryResults ?? [])
        {
            DrawLine(
                graphics,
                $"- BusinessDatabase: {result.DataSourceName}; sourceMode={result.SourceMode}; isSimulation={FormatBool(result.IsSimulation)}; sourceLabel={result.SourceLabel}; queryHash={result.QueryHash}; rows={result.RowCount}; truncated={FormatBool(result.IsTruncated)}",
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

    public Task<byte[]> GeneratePptxAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken = default)
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

    public Task<byte[]> GenerateXlsxAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        using (var spreadsheet = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = spreadsheet.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());

            var allTables = new[]
            {
                BuildSummaryTable(document),
                BuildDataTable(document),
                BuildSourcesTable(document)
            };

            uint sheetId = 1;
            foreach (var table in allTables)
            {
                AddWorksheet(workbookPart, sheets, table, sheetId++);
            }

            workbookPart.Workbook.Save();
        }

        return Task.FromResult(stream.ToArray());
    }

    private static void DrawLine(XGraphics graphics, string text, XFont font, double x, ref double y, double width)
    {
        graphics.DrawString(text, font, XBrushes.Black, new XRect(x, y, width, 20), XStringFormats.TopLeft);
        y += 22;
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
        builder.AppendLine("Data Source:");
        builder.AppendLine(BuildSourceMarker(document));
        if (!string.IsNullOrWhiteSpace(document.CloudReadonlySummary))
        {
            builder.AppendLine(document.CloudReadonlySummary);
        }
        foreach (var result in document.BusinessQueryResults ?? [])
        {
            builder.AppendLine($"BusinessDatabase: {result.DataSourceName}; sourceMode={result.SourceMode}; isSimulation={FormatBool(result.IsSimulation)}; sourceLabel={result.SourceLabel}; queryHash={result.QueryHash}; rows={result.RowCount}; truncated={FormatBool(result.IsTruncated)}");
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

    private static AgentReportTable BuildSummaryTable(AgentReportDocument document)
    {
        var rows = new List<IReadOnlyDictionary<string, string>>
        {
            SummaryRow("Title", document.Title),
            SummaryRow("Goal", document.Goal),
            SummaryRow("GeneratedAt", document.GeneratedAt.ToString("O", CultureInfo.InvariantCulture)),
            SummaryRow("SourceMarker", BuildSourceMarker(document)),
            SummaryRow("CloudReadonly", document.CloudReadonlySummary ?? string.Empty)
        };

        if (document.CloudReadonlySource is not null)
        {
            rows.Add(SummaryRow("SourceMode", document.CloudReadonlySource.SourceMode ?? string.Empty));
            rows.Add(SummaryRow("IsSimulation", FormatBool(document.CloudReadonlySource.IsSimulation)));
            rows.Add(SummaryRow("SourceLabel", document.CloudReadonlySource.SourceLabel ?? string.Empty));
            rows.Add(SummaryRow("SourcePath", document.CloudReadonlySource.SourcePath ?? string.Empty));
            rows.Add(SummaryRow("RowCount", document.CloudReadonlySource.RowCount.ToString(CultureInfo.InvariantCulture)));
            rows.Add(SummaryRow("Truncated", FormatBool(document.CloudReadonlySource.IsTruncated)));
            rows.Add(SummaryRow("QueryHash", document.CloudReadonlySource.QueryHash ?? string.Empty));
        }

        foreach (var result in document.BusinessQueryResults ?? [])
        {
            rows.Add(SummaryRow(
                $"BusinessQuery:{result.DataSourceName}",
                $"sourceMode={result.SourceMode}; isSimulation={FormatBool(result.IsSimulation)}; sourceLabel={result.SourceLabel}; queryHash={result.QueryHash}; rows={result.RowCount}; truncated={FormatBool(result.IsTruncated)}"));
        }

        foreach (var metric in document.Metrics ?? [])
        {
            rows.Add(SummaryRow($"Metric:{metric.Name}", metric.Value + (metric.Unit ?? string.Empty)));
        }

        return new AgentReportTable("Summary", ["Field", "Value"], rows);
    }

    private static AgentReportTable BuildDataTable(AgentReportDocument document)
    {
        if (document.Tables.Count == 0)
        {
            return new AgentReportTable(
                "Data",
                ["Message"],
                [new Dictionary<string, string> { ["Message"] = "No table data." }]);
        }

        var columns = new[] { "Table" }
            .Concat(document.Tables.SelectMany(table => table.Columns).Distinct(StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var table in document.Tables)
        {
            foreach (var sourceRow in table.Rows)
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Table"] = table.Name
                };
                foreach (var column in columns.Skip(1))
                {
                    row[column] = sourceRow.TryGetValue(column, out var value) ? value : string.Empty;
                }

                rows.Add(row);
            }
        }

        if (rows.Count == 0)
        {
            rows.Add(new Dictionary<string, string> { ["Table"] = "No table rows" });
        }

        return new AgentReportTable("Data", columns, rows);
    }

    private static AgentReportTable BuildSourcesTable(AgentReportDocument document)
    {
        var columns = new[]
        {
            "SourceType",
            "Name",
            "Detail",
            "Score",
            "IsLowConfidence",
            "SourceMode",
            "IsSimulation",
            "SourceLabel",
            "SourcePath",
            "RowCount",
            "Truncated",
            "QueryHash",
            "Marker"
        };
        var rows = new List<IReadOnlyDictionary<string, string>>();
        if (document.CloudReadonlySource is not null)
        {
            rows.Add(new Dictionary<string, string>
            {
                ["SourceType"] = "CloudReadonly",
                ["Name"] = document.CloudReadonlySource.SourceLabel ?? "CloudReadonly",
                ["Detail"] = document.CloudReadonlySummary ?? string.Empty,
                ["Score"] = string.Empty,
                ["IsLowConfidence"] = "false",
                ["SourceMode"] = document.CloudReadonlySource.SourceMode ?? string.Empty,
                ["IsSimulation"] = FormatBool(document.CloudReadonlySource.IsSimulation),
                ["SourceLabel"] = document.CloudReadonlySource.SourceLabel ?? string.Empty,
                ["SourcePath"] = document.CloudReadonlySource.SourcePath ?? string.Empty,
                ["RowCount"] = document.CloudReadonlySource.RowCount.ToString(CultureInfo.InvariantCulture),
                ["Truncated"] = FormatBool(document.CloudReadonlySource.IsTruncated),
                ["QueryHash"] = document.CloudReadonlySource.QueryHash ?? string.Empty,
                ["Marker"] = BuildSourceMarker(document)
            });
        }

        foreach (var result in document.BusinessQueryResults ?? [])
        {
            rows.Add(new Dictionary<string, string>
            {
                ["SourceType"] = "BusinessDatabase",
                ["Name"] = result.DataSourceName,
                ["Detail"] = $"queryHash={result.QueryHash}",
                ["Score"] = string.Empty,
                ["IsLowConfidence"] = "false",
                ["SourceMode"] = result.SourceMode,
                ["IsSimulation"] = FormatBool(result.IsSimulation),
                ["SourceLabel"] = result.SourceLabel,
                ["SourcePath"] = "BusinessDataSourceCenter/TextToSql",
                ["RowCount"] = result.RowCount.ToString(CultureInfo.InvariantCulture),
                ["Truncated"] = FormatBool(result.IsTruncated),
                ["QueryHash"] = result.QueryHash,
                ["Marker"] = $"sourceMode={result.SourceMode}; isSimulation={FormatBool(result.IsSimulation)}; sourceLabel={result.SourceLabel}; rowCount={result.RowCount}; truncated={FormatBool(result.IsTruncated)}; queryHash={result.QueryHash}"
            });
        }

        foreach (var source in document.Sources)
        {
            rows.Add(new Dictionary<string, string>
            {
                ["SourceType"] = source.SourceType,
                ["Name"] = source.Name,
                ["Detail"] = source.Detail,
                ["Score"] = source.Score?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
                ["IsLowConfidence"] = FormatBool(source.IsLowConfidence),
                ["SourceMode"] = string.Empty,
                ["IsSimulation"] = string.Empty,
                ["SourceLabel"] = string.Empty,
                ["SourcePath"] = string.Empty,
                ["RowCount"] = string.Empty,
                ["Truncated"] = string.Empty,
                ["QueryHash"] = string.Empty,
                ["Marker"] = string.Empty
            });
        }

        if (rows.Count == 0)
        {
            rows.Add(new Dictionary<string, string>
            {
                ["SourceType"] = "Local",
                ["Name"] = "Local workspace data",
                ["Detail"] = string.Empty,
                ["Score"] = string.Empty,
                ["IsLowConfidence"] = "false",
                ["SourceMode"] = "Local",
                ["IsSimulation"] = "false",
                ["SourceLabel"] = "Local workspace data",
                ["SourcePath"] = string.Empty,
                ["RowCount"] = "0",
                ["Truncated"] = "false",
                ["QueryHash"] = string.Empty,
                ["Marker"] = BuildSourceMarker(document)
            });
        }

        return new AgentReportTable("Sources", columns, rows);
    }

    private static IReadOnlyDictionary<string, string> SummaryRow(string field, string value)
    {
        return new Dictionary<string, string>
        {
            ["Field"] = field,
            ["Value"] = value
        };
    }

    private static string BuildSourceMarker(AgentReportDocument document)
    {
        var source = document.CloudReadonlySource;
        if (source is null)
        {
            return "sourceMode=Local; isSimulation=false; sourceLabel=Local workspace data";
        }

        var queryHash = string.IsNullOrWhiteSpace(source.QueryHash) ? string.Empty : $"; queryHash={source.QueryHash}";
        return $"sourceMode={source.SourceMode ?? "Unknown"}; isSimulation={FormatBool(source.IsSimulation)}; sourceLabel={source.SourceLabel ?? string.Empty}; rowCount={source.RowCount}; truncated={FormatBool(source.IsTruncated)}{queryHash}";
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static void AddWorksheet(WorkbookPart workbookPart, Sheets sheets, AgentReportTable table, uint sheetId)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);

        sheetData.AppendChild(new Row(table.Columns.Select(CreateCell)));
        foreach (var row in table.Rows)
        {
            sheetData.AppendChild(new Row(table.Columns.Select(column =>
                CreateCell(row.TryGetValue(column, out var value) ? value : string.Empty))));
        }

        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = SanitizeSheetName(table.Name, sheetId)
        });
    }

    private static Cell CreateCell(string value)
    {
        return new Cell
        {
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(value ?? string.Empty))
        };
    }

    private static string SanitizeSheetName(string name, uint sheetId)
    {
        var invalid = new HashSet<char>(['[', ']', '*', '?', '/', '\\', ':']);
        var sanitized = new string((name ?? string.Empty)
            .Where(ch => !invalid.Contains(ch))
            .Take(31)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? $"Sheet{sheetId}" : sanitized;
    }
}

internal sealed class AgentPdfFontResolver : IFontResolver
{
    public const string FamilyName = "AICopilotDefault";

    private static readonly object Sync = new();
    private static readonly string FontPath = ResolveFontPath();

    public static void EnsureRegistered()
    {
        if (GlobalFontSettings.FontResolver is not null)
        {
            return;
        }

        lock (Sync)
        {
            GlobalFontSettings.FontResolver ??= new AgentPdfFontResolver();
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        return new FontResolverInfo(FamilyName);
    }

    public byte[] GetFont(string faceName)
    {
        return File.ReadAllBytes(FontPath);
    }

    private static string ResolveFontPath()
    {
        var windowsFonts = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Fonts");
        var candidates = new[]
        {
            Path.Combine(windowsFonts, "NotoSansSC-VF.ttf"),
            Path.Combine(windowsFonts, "msyh.ttc"),
            Path.Combine(windowsFonts, "simhei.ttf"),
            Path.Combine(windowsFonts, "arial.ttf"),
            "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf"
        };

        return candidates.FirstOrDefault(File.Exists)
               ?? throw new InvalidOperationException("No TrueType/OpenType font is available for PDF artifact generation.");
    }
}
