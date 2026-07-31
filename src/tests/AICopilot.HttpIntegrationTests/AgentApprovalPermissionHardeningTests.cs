using System.Net;

namespace AICopilot.HttpIntegrationTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class AgentApprovalPermissionHardeningTests
{
    private readonly AICopilotAppFixture _fixture;

    public AgentApprovalPermissionHardeningTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<string, string> WithdrawnLegacyRoutes => new()
    {
        { "GET", "/api/aigateway/agent/task?id=00000000-0000-0000-0000-000000000001" },
        { "POST", "/api/aigateway/agent/task/run" },
        { "GET", "/api/aigateway/agent/approval/pending" },
        { "POST", "/api/aigateway/agent/approval/00000000-0000-0000-0000-000000000001/approve" },
        { "GET", "/api/aigateway/workspace/legacy" },
        { "POST", "/api/aigateway/workspace/legacy/finalize" },
        { "GET", "/api/aigateway/artifact/00000000-0000-0000-0000-000000000001/download" },
        { "GET", "/api/aigateway/approval-policy" },
        { "POST", "/api/aigateway/approval-policy" }
    };

    [Theory]
    [MemberData(nameof(WithdrawnLegacyRoutes))]
    public async Task LegacyApprovalAndWorkspaceRoutes_ShouldRemainUnreachable(
        string method,
        string route)
    {
        _fixture.HttpClient.DefaultRequestHeaders.Authorization = null;
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        using var response = await _fixture.HttpClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
