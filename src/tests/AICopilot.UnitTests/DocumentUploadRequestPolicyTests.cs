using AICopilot.Services.Contracts.Uploads;

namespace AICopilot.UnitTests;

public sealed class DocumentUploadRequestPolicyTests
{
    [Theory]
    [InlineData(null, "File is required.")]
    [InlineData(0L, "File is required.")]
    [InlineData(DocumentUploadRequestPolicy.MaxUploadBytes + 1, "File exceeds the 50 MB upload limit.")]
    public void Validate_ShouldRejectMissingEmptyOrOversizedContent(long? contentLength, string expectedError)
    {
        DocumentUploadRequestPolicy.Validate(contentLength).Should().Be(expectedError);
    }

    [Fact]
    public void Validate_ShouldAcceptContentWithinLimit()
    {
        DocumentUploadRequestPolicy.Validate(DocumentUploadRequestPolicy.MaxUploadBytes).Should().BeNull();
    }
}
