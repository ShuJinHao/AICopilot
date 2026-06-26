using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

internal static class ArtifactPreviewBuilder
{
    private const int MaxTextPreviewBytes = 2 * 1024 * 1024;
    private const int MaxBinaryPreviewBytes = 20 * 1024 * 1024;
    private static readonly Regex PdfPageRegex = new(@"/Type\s*/Page\b", RegexOptions.Compiled);

    public static async Task<Result<AgentArtifactPreviewDto>> BuildAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        CancellationToken cancellationToken)
    {
        var previewKind = ResolvePreviewKind(artifact);
        var content = await ResolveContentAsync(fileStore, workspaceCode, artifact, previewKind, cancellationToken);
        if (!content.IsSuccess)
        {
            return Result.From(content);
        }

        var value = content.Value!;
        return Result.Success(new AgentArtifactPreviewDto(
            artifact.Id.Value,
            artifact.Name,
            artifact.ArtifactType.ToString(),
            previewKind,
            ResolveArtifactStatus(artifact),
            artifact.Version,
            artifact.RelativePath,
            artifact.FileSize,
            artifact.MimeType,
            artifact.SourceMode,
            artifact.Boundary,
            artifact.IsSimulation,
            artifact.IsSandbox,
            artifact.SourceLabel,
            artifact.QueryHash,
            artifact.ResultHash,
            artifact.RowCount,
            artifact.IsTruncated,
            value.Content,
            value.Columns,
            value.Rows,
            value.Metadata));
    }

    private static async Task<Result<PreviewContent>> ResolveContentAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        string previewKind,
        CancellationToken cancellationToken)
    {
        if (previewKind is "markdown" or "html" or "json" or "chart" or "table")
        {
            var content = await ArtifactVersioningFiles.ReadTextAsync(
                fileStore,
                workspaceCode,
                artifact.RelativePath,
                artifact.MimeType,
                MaxTextPreviewBytes,
                cancellationToken);
            if (!content.IsSuccess)
            {
                return Result.From(content);
            }

            if (previewKind == "table")
            {
                var table = ParseCsvPreview(content.Value!);
                return Result.Success(new PreviewContent(
                    content.Value,
                    table.Columns,
                    table.Rows,
                    BuildBaseMetadata(artifact)));
            }

            return Result.Success(new PreviewContent(
                content.Value,
                [],
                [],
                BuildBaseMetadata(artifact)));
        }

        var binary = await ReadBinaryAsync(fileStore, workspaceCode, artifact, cancellationToken);
        if (!binary.IsSuccess)
        {
            return Result.From(binary);
        }

        var metadata = BuildBaseMetadata(artifact);
        IReadOnlyCollection<string> columns = [];
        IReadOnlyCollection<IReadOnlyDictionary<string, string>> rows = [];
        if (artifact.ArtifactType == ArtifactType.Pdf)
        {
            var text = Encoding.Latin1.GetString(binary.Value!);
            metadata["pageCount"] = PdfPageRegex.Matches(text).Count.ToString(CultureInfo.InvariantCulture);
        }
        else if (artifact.ArtifactType == ArtifactType.Pptx)
        {
            metadata["pageCount"] = CountZipEntries(binary.Value!, @"^ppt/slides/slide\d+\.xml$").ToString(CultureInfo.InvariantCulture);
        }
        else if (artifact.ArtifactType == ArtifactType.Xlsx)
        {
            var table = TryParseXlsxPreview(binary.Value!);
            columns = table.Columns;
            rows = table.Rows;
            metadata["previewRowCount"] = rows.Count.ToString(CultureInfo.InvariantCulture);
        }

        return Result.Success(new PreviewContent(null, columns, rows, metadata));
    }

    private static async Task<Result<byte[]>> ReadBinaryAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        CancellationToken cancellationToken)
    {
        var file = await fileStore.OpenReadAsync(
            workspaceCode,
            artifact.RelativePath,
            artifact.MimeType,
            cancellationToken);
        if (file is null)
        {
            return Result.NotFound("Artifact file does not exist.");
        }

        await using var stream = file.Stream;
        if (file.FileSize > MaxBinaryPreviewBytes)
        {
            return Result.Invalid($"Artifact binary preview exceeds the {MaxBinaryPreviewBytes} byte read limit.");
        }

        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return Result.Success(buffer.ToArray());
    }

    private static Dictionary<string, string> BuildBaseMetadata(Artifact artifact)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["downloadUrl"] = $"/api/aigateway/artifact/{artifact.Id.Value}/download",
            ["fileSize"] = artifact.FileSize.ToString(CultureInfo.InvariantCulture),
            ["mimeType"] = artifact.MimeType,
            ["finalizedAt"] = artifact.FinalizedAt?.ToString("O") ?? string.Empty,
            ["approvalStatus"] = artifact.Status == ArtifactStatus.Final ? "Approved" : "Pending",
            ["artifactVersion"] = artifact.Version.ToString(CultureInfo.InvariantCulture),
            ["artifactStatus"] = ResolveArtifactStatus(artifact)
        };
    }

    private static PreviewTable ParseCsvPreview(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Take(11)
            .ToArray();
        if (lines.Length == 0)
        {
            return new PreviewTable([], []);
        }

        var columns = SplitCsvLine(lines[0]);
        var rows = lines.Skip(1)
            .Select(line =>
            {
                var values = SplitCsvLine(line);
                return (IReadOnlyDictionary<string, string>)columns
                    .Select((column, index) => new { column, value = index < values.Count ? values[index] : string.Empty })
                    .ToDictionary(item => item.column, item => item.value, StringComparer.OrdinalIgnoreCase);
            })
            .ToArray();
        return new PreviewTable(columns, rows);
    }

    private static IReadOnlyList<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                builder.Append('"');
                i++;
                continue;
            }

            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        values.Add(builder.ToString());
        return values;
    }

    private static int CountZipEntries(byte[] content, string pattern)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return archive.Entries.Count(entry => regex.IsMatch(entry.FullName));
    }

    private static PreviewTable TryParseXlsxPreview(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var sharedStrings = ReadSharedStrings(archive);
            var sheet = archive.Entries
                .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (sheet is null)
            {
                return new PreviewTable([], []);
            }

            using var sheetStream = sheet.Open();
            var document = XDocument.Load(sheetStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var rows = document.Descendants(ns + "row").Take(11).ToArray();
            if (rows.Length == 0)
            {
                return new PreviewTable([], []);
            }

            var firstRowCells = rows[0].Elements(ns + "c").ToArray();
            var columns = firstRowCells
                .Select((cell, index) => ResolveCellText(cell, sharedStrings, ns) is { Length: > 0 } value ? value : $"Column{index + 1}")
                .ToArray();
            var previewRows = rows.Skip(1)
                .Select(row =>
                {
                    var values = row.Elements(ns + "c").Select(cell => ResolveCellText(cell, sharedStrings, ns)).ToArray();
                    return (IReadOnlyDictionary<string, string>)columns
                        .Select((column, index) => new { column, value = index < values.Length ? values[index] : string.Empty })
                        .ToDictionary(item => item.column, item => item.value, StringComparer.OrdinalIgnoreCase);
                })
                .ToArray();
            return new PreviewTable(columns, previewRows);
        }
        catch (InvalidDataException)
        {
            return new PreviewTable([], []);
        }
    }

    private static string[] ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(ns + "si")
            .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static string ResolveCellText(XElement cell, IReadOnlyList<string> sharedStrings, XNamespace ns)
    {
        var raw = cell.Element(ns + "v")?.Value ?? string.Empty;
        if (string.Equals(cell.Attribute("t")?.Value, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex) &&
            sharedIndex >= 0 &&
            sharedIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedIndex];
        }

        return raw;
    }

    private static string ResolvePreviewKind(Artifact artifact)
    {
        return artifact.ArtifactType switch
        {
            ArtifactType.Chart => "chart",
            ArtifactType.Json => "json",
            ArtifactType.Csv => "table",
            ArtifactType.Markdown => "markdown",
            ArtifactType.Html => "html",
            ArtifactType.Pdf => "pdf",
            ArtifactType.Pptx => "pptx",
            ArtifactType.Xlsx => "spreadsheet",
            _ => "download"
        };
    }

    private static string ResolveArtifactStatus(Artifact artifact)
    {
        return artifact.Status switch
        {
            ArtifactStatus.Draft => "Draft",
            ArtifactStatus.Reviewing or ArtifactStatus.Approved => "FinalPendingApproval",
            ArtifactStatus.Final => "Final",
            ArtifactStatus.Deleted => "Deleted",
            _ => artifact.Status.ToString()
        };
    }

    private sealed record PreviewContent(
        string? Content,
        IReadOnlyCollection<string> Columns,
        IReadOnlyCollection<IReadOnlyDictionary<string, string>> Rows,
        Dictionary<string, string> Metadata);

    private sealed record PreviewTable(
        IReadOnlyCollection<string> Columns,
        IReadOnlyCollection<IReadOnlyDictionary<string, string>> Rows);
}
