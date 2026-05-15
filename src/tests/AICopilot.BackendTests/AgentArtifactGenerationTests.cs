using System.IO.Compression;
using System.Text;
using AICopilot.Infrastructure.Artifacts;
using AICopilot.Services.Contracts;

namespace AICopilot.BackendTests;

[Trait("Suite", "AgentArtifact")]
public sealed class AgentArtifactGenerationTests
{
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
        var document = new AgentReportDocument(
            "A助理产物生成验收",
            "根据上传数据生成受控草稿产物。",
            ["capacity.csv (32 bytes, sha256=abcdef123456)"],
            [
                new AgentReportTable(
                    "capacity.csv",
                    ["Line", "Value"],
                    [
                        new Dictionary<string, string> { ["Line"] = "Line-A", ["Value"] = "12" },
                        new Dictionary<string, string> { ["Line"] = "Line-B", ["Value"] = "34" }
                    ])
            ],
            [new AgentReportSource("RAG", "验收文档", "DocumentId=1, Chunk=0", 0.91)],
            "Cloud AiRead 未启用，本步骤未访问 Cloud。",
            DateTimeOffset.UtcNow);

        var pdf = await generator.GeneratePdfAsync(document);
        var pptx = await generator.GeneratePptxAsync(document);
        var xlsx = await generator.GenerateXlsxAsync(document);

        Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
        ZipEntries(pptx).Should().Contain("ppt/presentation.xml");
        ZipEntries(xlsx).Should().Contain("xl/workbook.xml");
    }

    private static IReadOnlyCollection<string> ZipEntries(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }
}
