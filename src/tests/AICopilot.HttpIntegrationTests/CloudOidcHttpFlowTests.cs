using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.SharedKernel.Result;

namespace AICopilot.HttpIntegrationTests;

[Collection(CloudOidcHttpTestCollection.Name)]
public sealed class CloudOidcHttpFlowTests(CloudOidcHttpAppFixture fixture)
{
    private const string ExternalCookieName = "AICopilot-CloudOidc-External";
    private const string LocalPassword = "Password123!";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task CloudOidcHttpFlow_ShouldCloseLoginConflictRetryAndCookieScenarios()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var jitUserName = $"oidc-jit-{suffix}";
        var localUserName = $"oidc-local-{suffix}";
        var cancelUserName = $"oidc-cancel-{suffix}";
        var disabledUserName = $"oidc-disabled-{suffix}";

        await CreateLocalUserAsync(localUserName);
        await CreateLocalUserAsync(cancelUserName);
        await CreateDisabledLocalUserAsync(disabledUserName);

        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            CookieContainer = cookies
        };
        using var oidcClient = new HttpClient(handler)
        {
            BaseAddress = fixture.HttpClient.BaseAddress
        };

        fixture.Provider.SetIdentity(CreateIdentity("jit-subject", jitUserName));
        await CompleteCloudCallbackAsync(oidcClient, cookies);
        using var firstBinding = await oidcClient.PostAsync(
            "/api/identity/cloud-oidc/finalize",
            content: null);
        firstBinding.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstLogin = await ReadJsonAsync<LoginDto>(firstBinding);
        firstLogin.UserName.Should().Be(jitUserName);
        AssertExternalCookieCleared(firstBinding, cookies);

        await CompleteCloudCallbackAsync(oidcClient, cookies);
        using var repeatedLogin = await oidcClient.PostAsync(
            "/api/identity/cloud-oidc/finalize",
            content: null);
        repeatedLogin.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync<LoginDto>(repeatedLogin)).UserName.Should().Be(jitUserName);
        AssertExternalCookieCleared(repeatedLogin, cookies);

        fixture.Provider.SetIdentity(CreateIdentity("local-subject-a", localUserName));
        await CompleteCloudCallbackAsync(oidcClient, cookies);
        using var confirmationRequired = await oidcClient.PostAsync(
            "/api/identity/cloud-oidc/finalize",
            content: null);
        confirmationRequired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadJsonAsync<ProblemDto>(confirmationRequired)).Code.Should()
            .Be(AuthProblemCodes.ExternalIdentityConfirmationRequired);
        AssertExternalCookieRetained(confirmationRequired, cookies);

        using var wrongPassword = await oidcClient.PostAsJsonAsync(
            "/api/identity/cloud-oidc/confirm-existing",
            new { password = "WrongPassword123!" },
            JsonOptions);
        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadJsonAsync<ProblemDto>(wrongPassword)).Code.Should()
            .Be(AuthProblemCodes.InvalidCredentials);
        AssertExternalCookieRetained(wrongPassword, cookies);

        using var confirmed = await oidcClient.PostAsJsonAsync(
            "/api/identity/cloud-oidc/confirm-existing",
            new { password = LocalPassword },
            JsonOptions);
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmedLogin = await ReadJsonAsync<LoginDto>(confirmed);
        confirmedLogin.UserName.Should().Be(localUserName);
        AssertExternalCookieCleared(confirmed, cookies);

        fixture.Provider.SetIdentity(CreateIdentity("local-subject-b", localUserName));
        await CompleteCloudCallbackAsync(oidcClient, cookies);
        using var conflict = await oidcClient.PostAsync(
            "/api/identity/cloud-oidc/finalize",
            content: null);
        conflict.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadJsonAsync<ProblemDto>(conflict)).Code.Should()
            .Be(AuthProblemCodes.ExternalIdentityConflict);
        AssertExternalCookieCleared(conflict, cookies);

        fixture.Provider.SetIdentity(CreateIdentity("disabled-subject", disabledUserName));
        await CompleteCloudCallbackAsync(oidcClient, cookies);
        using var disabled = await oidcClient.PostAsync(
            "/api/identity/cloud-oidc/finalize",
            content: null);
        disabled.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadJsonAsync<ProblemDto>(disabled)).Code.Should()
            .Be(AuthProblemCodes.AccountDisabled);
        AssertExternalCookieCleared(disabled, cookies);

        using var expired = await oidcClient.PostAsync(
            "/api/identity/cloud-oidc/finalize",
            content: null);
        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadJsonAsync<ProblemDto>(expired)).Code.Should()
            .Be(AuthProblemCodes.CloudOidcInvalidPrincipal);
        AssertExternalCookieCleared(expired, cookies);

        fixture.Provider.SetIdentity(CreateIdentity("cancel-subject", cancelUserName));
        await CompleteCloudCallbackAsync(oidcClient, cookies);
        using var cancelConfirmationRequired = await oidcClient.PostAsync(
            "/api/identity/cloud-oidc/finalize",
            content: null);
        cancelConfirmationRequired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadJsonAsync<ProblemDto>(cancelConfirmationRequired)).Code.Should()
            .Be(AuthProblemCodes.ExternalIdentityConfirmationRequired);
        AssertExternalCookieRetained(cancelConfirmationRequired, cookies);

        using var canceled = await oidcClient.PostAsync(
            "/api/identity/cloud-oidc/cancel",
            content: null);
        canceled.StatusCode.Should().Be(HttpStatusCode.NoContent);
        AssertExternalCookieCleared(canceled, cookies);

        fixture.SetAuthToken(confirmedLogin.Token);
        using var profileResponse = await fixture.HttpClient.GetAsync("/api/identity/me");
        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await ReadJsonAsync<CurrentUserDto>(profileResponse);
        profile.UserName.Should().Be(localUserName);
        profile.RoleName.Should().Be("User");
        fixture.ClearAuthToken();
    }

    private async Task CreateLocalUserAsync(string userName)
    {
        using var loginResponse = await fixture.HttpClient.PostAsJsonAsync(
            "/api/identity/login",
            new
            {
                username = fixture.BootstrapAdminUserName,
                password = fixture.BootstrapAdminPassword
            },
            JsonOptions);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await ReadJsonAsync<LoginDto>(loginResponse);
        fixture.HttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.Token);

        using var createResponse = await fixture.HttpClient.PostAsJsonAsync(
            "/api/identity/user",
            new
            {
                userName,
                password = LocalPassword,
                roleName = "User"
            },
            JsonOptions);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.ClearAuthToken();
    }

    private async Task CreateDisabledLocalUserAsync(string userName)
    {
        using var loginResponse = await fixture.HttpClient.PostAsJsonAsync(
            "/api/identity/login",
            new
            {
                username = fixture.BootstrapAdminUserName,
                password = fixture.BootstrapAdminPassword
            },
            JsonOptions);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await ReadJsonAsync<LoginDto>(loginResponse);
        fixture.HttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.Token);

        using var createResponse = await fixture.HttpClient.PostAsJsonAsync(
            "/api/identity/user",
            new
            {
                userName,
                password = LocalPassword,
                roleName = "User"
            },
            JsonOptions);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await ReadJsonAsync<CreatedUserDto>(createResponse);

        using var disableResponse = await fixture.HttpClient.PutAsJsonAsync(
            "/api/identity/user/disable",
            new { userId = created.UserId },
            JsonOptions);
        disableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.ClearAuthToken();
    }

    private async Task CompleteCloudCallbackAsync(
        HttpClient client,
        CookieContainer cookies)
    {
        using var response = await client.GetAsync("/api/identity/cloud-oidc/challenge");

        response.RequestMessage!.RequestUri!.AbsolutePath.Should()
            .Be("/cloud-login/complete");
        HasExternalCookie(cookies).Should().BeTrue();
    }

    private void AssertExternalCookieRetained(
        HttpResponseMessage response,
        CookieContainer cookies)
    {
        HasExternalCookie(cookies).Should().BeTrue();
        HasExternalCookieDeletion(response).Should().BeFalse();
    }

    private void AssertExternalCookieCleared(
        HttpResponseMessage response,
        CookieContainer cookies)
    {
        HasExternalCookie(cookies).Should().BeFalse();
        HasExternalCookieDeletion(response).Should().BeTrue();
    }

    private bool HasExternalCookie(CookieContainer cookies)
    {
        return cookies.GetCookies(fixture.HttpClient.BaseAddress!)
            .Cast<Cookie>()
            .Any(cookie =>
                cookie.Name == ExternalCookieName &&
                !cookie.Expired &&
                !string.IsNullOrWhiteSpace(cookie.Value));
    }

    private static bool HasExternalCookieDeletion(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Set-Cookie", out var values) &&
               values.Any(value =>
                   value.StartsWith($"{ExternalCookieName}=", StringComparison.Ordinal) &&
                   (value.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
                    value.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)));
    }

    private static FakeCloudOidcIdentity CreateIdentity(
        string subjectPrefix,
        string userName)
    {
        return new FakeCloudOidcIdentity(
            $"{subjectPrefix}-{userName}",
            userName,
            userName,
            $"employee-{userName}",
            DisplayName: userName);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private sealed record LoginDto(string UserName, string Token);

    private sealed record CreatedUserDto(string UserId);

    private sealed record CurrentUserDto(string UserName, string? RoleName);

    private sealed record ProblemDto(string? Code);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CloudOidcHttpTestCollection
    : ICollectionFixture<CloudOidcHttpAppFixture>
{
    public const string Name = "AICopilotCloudOidcHttp";
}
