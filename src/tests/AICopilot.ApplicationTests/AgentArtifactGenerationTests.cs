using System.IO.Compression;
using System.Text;
using AICopilot.Infrastructure.Artifacts;
using AICopilot.Services.Contracts;

namespace AICopilot.ApplicationTests;

public sealed class AgentArtifactGenerationTests
{
    private const string SimulationLabel = "\u6a21\u62df Cloud \u53ea\u8bfb\u6570\u636e";

    [Fact]
    public async Task AgentTableFileParser_ShouldParseCsvIntoNormalizedRows()
    {
        var parser = new AgentTableFileParser();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            Name,Value
            Line-A,12
            Line-B,34
            """));

        var table = await parser.ParseAsync(new AgentTableFileParseRequest(
            "capacity.csv",
            "text/csv",
            stream));

        table.Should().NotBeNull();
        table!.Columns.Should().Equal("Name", "Value");
        table.Rows.Should().HaveCount(2);
        table.Rows[0]["Name"].Should().Be("Line-A");
        table.Rows[1]["Value"].Should().Be("34");
    }

    [Fact]
    public async Task AgentArtifactDocumentGenerator_ShouldCreatePortableDraftFormats()
    {
        var generator = new AgentArtifactDocumentGenerator();
        var document = CreateSimulationDocument();

        var pdf = await generator.GeneratePdfAsync(document);
        var pptx = await generator.GeneratePptxAsync(document);
        var xlsx = await generator.GenerateXlsxAsync(document);

        Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
        ZipEntries(pptx).Should().Contain("ppt/presentation.xml");
        ZipEntries(xlsx).Should().Contain("xl/workbook.xml");
    }

    [Fact]
    public async Task AgentArtifactDocumentGenerator_ShouldCarrySimulationSourceAcrossPortableFormats()
    {
        var generator = new AgentArtifactDocumentGenerator();
        var document = CreateSimulationDocument();

        var pptx = await generator.GeneratePptxAsync(document);
        var xlsx = await generator.GenerateXlsxAsync(document);

        var pptxXml = ZipXmlText(pptx);
        pptxXml.Should().Contain("sourceMode=Simulation");
        pptxXml.Should().Contain("isSimulation=true");
        pptxXml.Should().Contain(SimulationLabel);

        var xlsxXml = ZipXmlText(xlsx);
        xlsxXml.Should().Contain("Summary");
        xlsxXml.Should().Contain("Data");
        xlsxXml.Should().Contain("Sources");
        xlsxXml.Should().Contain("sourceMode=Simulation");
        xlsxXml.Should().Contain("isSimulation=true");
        xlsxXml.Should().Contain(SimulationLabel);
        xlsxXml.Should().Contain("Metric:sourceMode");
        xlsxXml.Should().Contain("plannedOutput");
    }

    private static AgentReportDocument CreateSimulationDocument()
    {
        return new AgentReportDocument(
            "AICopilot artifact acceptance",
            "Generate controlled draft artifacts from simulation manufacturing data.",
            ["capacity.csv (32 bytes, sha256=abcdef123456)"],
            [
                new AgentReportTable(
                    "Simulation capacity",
                    ["Line", "plannedOutput", "actualOutput"],
                    [
                        new Dictionary<string, string> { ["Line"] = "Line-A", ["plannedOutput"] = "1200", ["actualOutput"] = "1174" },
                        new Dictionary<string, string> { ["Line"] = "Line-B", ["plannedOutput"] = "980", ["actualOutput"] = "955" }
                    ])
            ],
            [new AgentReportSource("RAG", "Acceptance document", "DocumentId=1, Chunk=0", 0.91)],
            $"sourceMode=Simulation; isSimulation=true; sourceLabel={SimulationLabel}; returned 2 rows.",
            DateTimeOffset.UtcNow,
            [
                new AgentReportMetric("sourceMode", "Simulation", Source: SimulationLabel),
                new AgentReportMetric("isSimulation", "true", Source: SimulationLabel),
                new AgentReportMetric("sourceLabel", SimulationLabel, Source: SimulationLabel),
                new AgentReportMetric("avg.plannedOutput", "1090", Source: SimulationLabel)
            ],
            new AgentReportSourceInfo(
                "Simulation",
                true,
                SimulationLabel,
                "/simulation/manufacturing/weekly-report",
                2,
                false));
    }

    private static IReadOnlyCollection<string> ZipEntries(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }

    private static string ZipXmlText(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var builder = new StringBuilder();
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            builder.AppendLine(reader.ReadToEnd());
        }

        return builder.ToString();
    }
}
