using System.Text;
using AICopilot.AiGatewayService.Uploads;
using AICopilot.RagService.Commands.Documents;
using AICopilot.Services.Contracts;

namespace AICopilot.UnitTests;

public sealed class UploadValidationTests
{
    [Theory]
    [InlineData("payload.exe", "application/octet-stream", "MZ")]
    [InlineData("script.ps1", "text/plain", "Write-Host test")]
    [InlineData("archive.zip", "application/zip", "PK")]
    [InlineData("page.html", "text/html", "<html></html>")]
    public async Task UploadPolicy_ShouldRejectDangerousExtensions(
        string fileName,
        string contentType,
        string content)
    {
        var file = BuildFile(fileName, contentType, Encoding.UTF8.GetBytes(content));

        var result = await AiGatewayUploadSecurityPolicy.ValidateAndNormalizeAsync(file, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not allowed");
    }

    [Fact]
    public async Task UploadPolicy_ShouldRejectContentThatDoesNotMatchExtension()
    {
        var file = BuildFile("photo.jpg", "image/jpeg", Encoding.UTF8.GetBytes("not an image"));

        var result = await AiGatewayUploadSecurityPolicy.ValidateAndNormalizeAsync(file, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not match");
    }

    [Theory]
    [InlineData("../line.csv")]
    [InlineData(@"C:\Users\alice\line.csv")]
    [InlineData(@"C:\fakepath\line.csv")]
    public async Task UploadPolicy_ShouldSanitizeClientPathAndAllowCsv(string fileName)
    {
        var file = BuildFile(fileName, "text/csv", Encoding.UTF8.GetBytes("station,count\nA,1\n"));

        var result = await AiGatewayUploadSecurityPolicy.ValidateAndNormalizeAsync(file, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.File.Should().NotBeNull();
        result.File!.FileName.Should().Be("line.csv");
        result.File.ContentType.Should().Be("text/csv");
    }

    [Fact]
    public void UploadFileNamePolicy_ShouldRejectControlOnlyNamesAndBoundUtf8Length()
    {
        UploadFileNamePolicy.Normalize("folder/bad\u0001.csv").Should().BeEmpty();

        var normalized = UploadFileNamePolicy.Normalize($"folder/{new string('数', 100)}.csv");

        Encoding.UTF8.GetByteCount(normalized).Should().BeLessThanOrEqualTo(
            UploadFileNamePolicy.MaximumUtf8Bytes);
        normalized.Should().EndWith(".csv");
    }

    [Theory]
    [InlineData("payload.exe", "application/octet-stream", "MZ")]
    [InlineData("script.ps1", "text/plain", "Write-Host test")]
    public async Task RagUploadPolicy_ShouldRejectDangerousExtensions(
        string fileName,
        string contentType,
        string content)
    {
        var file = await NormalizeRagFileAsync(fileName, contentType, Encoding.UTF8.GetBytes(content));

        var result = await RagDocumentUploadSecurityPolicy.ValidateAndNormalizeAsync(
            file,
            new FixedDocumentFormatPolicy([".txt", ".md", ".pdf", ".docx", ".xlsx", ".csv", ".json"]),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not allowed");
    }

    [Fact]
    public async Task RagUploadPolicy_ShouldRejectSpoofedMimeAndContent()
    {
        var file = await NormalizeRagFileAsync("report.pdf", "application/pdf", Encoding.UTF8.GetBytes("not a pdf"));

        var result = await RagDocumentUploadSecurityPolicy.ValidateAndNormalizeAsync(
            file,
            new FixedDocumentFormatPolicy([".pdf"]),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not match");
    }

    [Fact]
    public async Task RagUploadPolicy_ShouldRejectOversizedTextFiles()
    {
        var file = new FileUploadStream(
            "large.txt",
            new MemoryStream(Encoding.UTF8.GetBytes("content")),
            "text/plain",
            10_000_001);

        var result = await RagDocumentUploadSecurityPolicy.ValidateAndNormalizeAsync(
            file,
            new FixedDocumentFormatPolicy([".txt"]),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("byte limit");
    }

    private static AiGatewayUploadStream BuildFile(string fileName, string contentType, byte[] content)
    {
        return new AiGatewayUploadStream(fileName, contentType, content.Length, new MemoryStream(content));
    }

    private static async Task<FileUploadStream> NormalizeRagFileAsync(
        string fileName,
        string contentType,
        byte[] content)
    {
        return await RagDocumentUploadSecurityPolicy.NormalizeStreamAsync(
            new FileUploadStream(fileName, new MemoryStream(content), contentType, content.Length),
            CancellationToken.None);
    }

}
