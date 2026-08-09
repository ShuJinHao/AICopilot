using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.Services.Contracts;
using Microsoft.AspNetCore.Authentication;

namespace AICopilot.InProcessTests;

public sealed class CloudOidcDelegationProofTests
{
    [Fact]
    public void Proof_ShouldBindTrustedIssuerUserInfoSubjectTenantAndGrantedScope()
    {
        var properties = new AuthenticationProperties();
        CloudOidcDelegationProof.CaptureGrantedScopes(
            properties,
            grantedScope: null,
            ["openid", "profile", CloudDelegationDefaults.Scope]);
        using var userInfo = JsonDocument.Parse(
            """{"sub":"10000000-0000-0000-0000-000000000001","tenant_id":"tenant-a"}""");

        CloudOidcDelegationProof.TryCaptureUserInfo(
                userInfo,
                properties,
                out var captureError)
            .Should().BeTrue(captureError);
        var created = CloudOidcDelegationProof.TryCreateEvidence(
            properties,
            new CloudOidcOptions { Issuer = "https://cloud.example.com/" },
            ValidContractProof(),
            out var evidence);

        created.Should().BeTrue();
        evidence.Should().BeEquivalentTo(new CloudDelegationTokenEvidence(
            "https://cloud.example.com",
            "10000000-0000-0000-0000-000000000001",
            "10000000-0000-0000-0000-000000000001",
            "tenant-a",
            CloudDelegationDefaults.Audience,
            CloudDelegationDefaults.Actor,
            ["openid", "profile", CloudDelegationDefaults.Scope]));
    }

    [Fact]
    public void Proof_ShouldRejectDuplicateUserInfoSubject()
    {
        var properties = new AuthenticationProperties();
        using var userInfo = JsonDocument.Parse(
            """{"sub":"10000000-0000-0000-0000-000000000001","sub":"20000000-0000-0000-0000-000000000002","tenant_id":"tenant-a"}""");

        var captured = CloudOidcDelegationProof.TryCaptureUserInfo(
            userInfo,
            properties,
            out var error);

        captured.Should().BeFalse();
        error.Should().Contain("one valid subject");
    }

    [Fact]
    public void Proof_ShouldNotCreateEvidenceWithoutTheTokenBoundUserInfoOrScope()
    {
        var created = CloudOidcDelegationProof.TryCreateEvidence(
            new AuthenticationProperties(),
            new CloudOidcOptions { Issuer = "https://cloud.example.com" },
            ValidContractProof(),
            out _);

        created.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, null, true)]
    [InlineData(
        HttpStatusCode.Forbidden,
        "拒绝访问：Cloud 用户状态、权限或设备范围不再有效",
        true)]
    [InlineData(
        HttpStatusCode.Forbidden,
        "当前令牌不具备访问该资源的授权范围。",
        false)]
    [InlineData(HttpStatusCode.Unauthorized, null, false)]
    [InlineData(HttpStatusCode.Redirect, null, false)]
    public async Task ContractValidator_ShouldRequireCloudAiReadPolicyProof(
        HttpStatusCode statusCode,
        string? detail,
        bool expectedValid)
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(statusCode)
            {
                Content = detail is null
                    ? JsonContent.Create(new { })
                    : JsonContent.Create(new { detail })
            };
        }));
        var validator = new CloudDelegationTokenContractValidator(httpClient);

        var proof = await validator.ValidateAsync(
            "https://cloud.example.com",
            "opaque-delegated-token",
            CancellationToken.None);

        (proof is not null).Should().Be(expectedValid);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri.Should().Be(
            "https://cloud.example.com/api/v1/ai/read/processes?maxRows=1");
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("opaque-delegated-token");
        if (proof is not null)
        {
            proof.Should().Be(ValidContractProof());
        }
    }

    [Fact]
    public void Proof_ShouldRejectUnvalidatedAudienceOrActor()
    {
        var properties = new AuthenticationProperties();
        CloudOidcDelegationProof.CaptureGrantedScopes(
            properties,
            CloudDelegationDefaults.Scope,
            [CloudDelegationDefaults.Scope]);
        using var userInfo = JsonDocument.Parse(
            """{"sub":"10000000-0000-0000-0000-000000000001","tenant_id":"tenant-a"}""");
        CloudOidcDelegationProof.TryCaptureUserInfo(userInfo, properties, out _).Should().BeTrue();

        var created = CloudOidcDelegationProof.TryCreateEvidence(
            properties,
            new CloudOidcOptions { Issuer = "https://cloud.example.com" },
            new CloudDelegationTokenContractProof("wrong-audience", "wrong-actor"),
            out _);

        created.Should().BeFalse();
    }

    private static CloudDelegationTokenContractProof ValidContractProof() => new(
        CloudDelegationDefaults.Audience,
        CloudDelegationDefaults.Actor);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }
}
