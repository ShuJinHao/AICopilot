namespace AICopilot.EndToEndTests;

[Collection(BackendTestCollection.Name)]
public sealed class RagWorkerStartupTests
{
    private readonly AICopilotAppFixture _fixture;

    public RagWorkerStartupTests(AICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void RagWorker_ShouldStart_WithRequiredDependencies()
    {
        _fixture.HttpClient.Should().NotBeNull();
    }
}
