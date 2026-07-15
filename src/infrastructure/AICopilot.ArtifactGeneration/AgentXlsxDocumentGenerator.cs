using AICopilot.Services.Contracts;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace AICopilot.Infrastructure.Artifacts;

internal static class AgentXlsxDocumentGenerator
{
    public static Task<byte[]> GenerateAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken)
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
                AgentArtifactDocumentFormatting.BuildSummaryTable(document),
                AgentArtifactDocumentFormatting.BuildDataTable(document),
                AgentArtifactDocumentFormatting.BuildSourcesTable(document)
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
