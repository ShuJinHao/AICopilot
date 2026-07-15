using System.Globalization;
using System.Text;
using System.Text.Json;
using AICopilot.Services.Contracts;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace AICopilot.Infrastructure.Artifacts;

internal static class AgentTableFileParserCore
{
    private const int MaxRows = 200;

    public static async Task<AgentReportTable?> ParseAsync(
        AgentTableFileParseRequest request,
        CancellationToken cancellationToken)
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
        if (rows.Length == 0)
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
