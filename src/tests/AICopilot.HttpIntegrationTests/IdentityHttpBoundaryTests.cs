using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using AICopilot.AspireIntegrationTestKit;
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
        var firstTraceId = ActivityTraceId.CreateRandom().ToString();
        var secondTraceId = ActivityTraceId.CreateRandom().ToString();
        firstTraceId.Should().NotBe(secondTraceId);
        var successTraceId = ActivityTraceId.CreateRandom().ToString();

        var logs = await fixture.ExecuteWithHttpTraceLogEvidenceAsync(
            "aicopilot-httpapi",
            [firstTraceId, secondTraceId, successTraceId],
            async () =>
            {
                using var firstResponse = await SendWithTraceAsync(HttpMethod.Get, "/api/identity/me", firstTraceId);
                using var secondResponse = await SendWithTraceAsync(HttpMethod.Get, "/api/identity/me", secondTraceId);
                var firstProblem = await firstResponse.Content.ReadFromJsonAsync<ProblemDetailsDto>(JsonOptions);
                var secondProblem = await secondResponse.Content.ReadFromJsonAsync<ProblemDetailsDto>(JsonOptions);

                foreach (var (response, problem, traceId) in new[]
                         {
                             (firstResponse, firstProblem, firstTraceId),
                             (secondResponse, secondProblem, secondTraceId)
                         })
                {
                    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                    response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
                    response.Headers.GetValues("X-AICopilot-Trace-Id").Should().ContainSingle().Which.Should().Be(traceId);
                    problem!.Code.Should().Be(AuthProblemCodes.Unauthorized);
                    problem.TraceId.Should().Be(traceId);
                }

                var login = await LoginAsync();
                fixture.HttpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", login.Token);
                using var successResponse = await SendWithTraceAsync(HttpMethod.Get, "/api/identity/me", successTraceId);
                successResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                successResponse.Headers.GetValues("X-AICopilot-Trace-Id")
                    .Should().ContainSingle().Which.Should().Be(successTraceId);
            });

        foreach (var traceId in new[] { firstTraceId, secondTraceId, successTraceId })
        {
            logs.Count(line => line.Contains("HTTP request started", StringComparison.Ordinal) &&
                               line.Contains(traceId, StringComparison.Ordinal)).Should().Be(1);
            logs.Count(line => line.Contains("HTTP request completed", StringComparison.Ordinal) &&
                               line.Contains(traceId, StringComparison.Ordinal)).Should().Be(1);
        }
    }

    [Fact]
    public async Task CloudOidcFinalize_WithoutExternalCookie_ShouldAuthenticateTrackAndSignOutExactlyOnce()
    {
        fixture.ClearAuthToken();

        using var response = await fixture.HttpClient.PostAsync(
            "/api/identity/cloud-oidc/finalize",
            content: null);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>(JsonOptions);
        var externalCookieDeletes = response.Headers.TryGetValues("Set-Cookie", out var setCookies)
            ? setCookies.Where(cookie => cookie.Contains(
                    "__Host-AICopilot-CloudOidc-External=",
                    StringComparison.Ordinal))
                .ToArray()
            : [];

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        problem!.Code.Should().Be(AuthProblemCodes.CloudOidcInvalidPrincipal);
        problem.TraceId.Should().NotBeNullOrWhiteSpace();
        externalCookieDeletes.Should().ContainSingle();
        externalCookieDeletes[0].Should().Contain("expires=", Exactly.Once());
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

    private async Task<HttpResponseMessage> SendWithTraceAsync(
        HttpMethod method,
        string path,
        string traceId)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(
            "traceparent",
            $"00-{traceId}-0123456789abcdef-01").Should().BeTrue();
        return await fixture.HttpClient.SendAsync(request);
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
