using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.BackendTests;
using AICopilot.IdentityService.Authorization;
using AICopilot.SharedKernel.Result;

namespace AICopilot.HttpIntegrationTests;

[Collection(IdentityHttpTestCollection.Name)]
public sealed class IdentityHttpBoundaryTests(CoreAICopilotAppFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task ProtectedEndpoint_ShouldUseRealAuthenticationMiddlewareAndTraceProblem()
    {
        fixture.ClearAuthToken();

        using var response = await fixture.HttpClient.GetAsync("/api/identity/me");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        problem!.Code.Should().Be(AuthProblemCodes.Unauthorized);
        problem.TraceId.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("/api/identity/user/disable", null)]
    [InlineData("/api/identity/user/role", "User")]
    public async Task LastEnabledAdminMutation_ShouldCrossAuthMiddlewareAndReturnStableTrackedContract(
        string path,
        string? roleName)
    {
        var login = await LoginAsync();
        fixture.HttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.Token);

        var profile = await fixture.HttpClient.GetFromJsonAsync<CurrentUserDto>(
            "/api/identity/me",
            JsonOptions);
        profile.Should().NotBeNull();

        object payload = roleName is null
            ? new { userId = profile!.UserId }
            : new { userId = profile!.UserId, roleName };
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        using var response = await fixture.HttpClient.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem!.Code.Should().Be(AuthProblemCodes.LastEnabledAdminRequired);
        problem.UserFacingMessage.Should()
            .Be(IdentityProblemDescriptors.LastEnabledAdminUserFacingMessage);
        problem.TraceId.Should().NotBeNullOrWhiteSpace();

        using var stillAuthenticated = await fixture.HttpClient.GetAsync("/api/identity/me");
        stillAuthenticated.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<LoginDto> LoginAsync()
    {
        using var response = await fixture.HttpClient.PostAsJsonAsync(
            "/api/identity/login",
            new
            {
                username = fixture.BootstrapAdminUserName,
                password = fixture.BootstrapAdminPassword
            },
            JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LoginDto>(JsonOptions))!;
    }

    private sealed record LoginDto(string Token);
    private sealed record CurrentUserDto(string UserId);
    private sealed record ProblemDetailsDto(
        string? Code,
        string? UserFacingMessage,
        string? TraceId);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IdentityHttpTestCollection : ICollectionFixture<CoreAICopilotAppFixture>
{
    public const string Name = "AICopilotIdentityHttp";
}
