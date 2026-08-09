using AICopilot.Services.Contracts;

namespace AICopilot.UnitTests;

public sealed class CloudAiReadEndpointPolicyTests
{
    [Theory]
    [InlineData("/api/v1/ai/read/devices")]
    [InlineData("/api/v1/ai/read/processes")]
    [InlineData("/api/v1/ai/read/client-releases")]
    [InlineData("/api/v1/ai/read/device-client-states")]
    [InlineData("/api/v1/ai/read/capacity/summary")]
    [InlineData("/api/v1/ai/read/capacity/hourly")]
    [InlineData("/api/v1/ai/read/device-logs")]
    [InlineData("/api/v1/ai/read/production-records")]
    [InlineData("/api/v1/ai/read/device-plcs")]
    [InlineData("/api/v1/ai/read/data-schemas")]
    public void Evaluate_ShouldAllowWhitelistedGetPaths(string path)
    {
        var decision = CloudAiReadEndpointPolicy.Evaluate(HttpMethod.Get, path);

        decision.IsAllowed.Should().BeTrue(decision.Reason);
    }

    [Theory]
    [InlineData("PUT", "/api/v1/ai/read/devices")]
    [InlineData("PATCH", "/api/v1/ai/read/devices")]
    [InlineData("DELETE", "/api/v1/ai/read/device-logs")]
    [InlineData("GET", "/api/v1/devices")]
    [InlineData("GET", "/api/v1/ai/read/pass-stations/stacking")]
    [InlineData("GET", "/api/v1/ai/read/pass-stations/a/b")]
    [InlineData("POST", "/api/v1/ai/read/devices")]
    public void Evaluate_ShouldRejectWriteMethodsAndNonWhitelistedPaths(string method, string path)
    {
        CloudAiReadEndpointPolicy.Evaluate(new HttpMethod(method), path).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void SemanticSupport_ShouldNotSupportRecipeTarget()
    {
        CloudAiReadSemanticSupport.IsSupported(SemanticQueryTarget.Recipe).Should().BeFalse();
        CloudAiReadSemanticSupport.IsSupported(SemanticQueryTarget.Process).Should().BeTrue();
        CloudAiReadSemanticSupport.IsSupported(SemanticQueryTarget.ClientRelease).Should().BeTrue();
    }

    [Fact]
    public void AiReadPolicy_ShouldRejectIdentityStatusEndpoint()
    {
        CloudAiReadEndpointPolicy.Evaluate(
                HttpMethod.Get,
                "/api/v1/ai/identity/users/cloud-user-1/status")
            .IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void IdentityStatusPolicy_ShouldAllowOnlyItsFixedGetEndpoint()
    {
        CloudIdentityStatusEndpointPolicy.Evaluate(
                HttpMethod.Get,
                "/api/v1/ai/identity/users/cloud-user-1/status")
            .IsAllowed.Should().BeTrue();
        CloudIdentityStatusEndpointPolicy.Evaluate(
                HttpMethod.Get,
                "/api/v1/ai/read/devices")
            .IsAllowed.Should().BeFalse();
        CloudIdentityStatusEndpointPolicy.Evaluate(
                HttpMethod.Post,
                "/api/v1/ai/identity/users/cloud-user-1/status")
            .IsAllowed.Should().BeFalse();
        CloudIdentityStatusEndpointPolicy.Evaluate(
                HttpMethod.Get,
                "/api/v1/ai/identity/users/cloud-user-1/extra/status")
            .IsAllowed.Should().BeFalse();
    }

    [Theory]
    [InlineData("POST", "/api/v1/ai/read/devices/query")]
    [InlineData("POST", "/api/v1/ai/identity/users/query")]
    [InlineData("PUT", "/api/v1/ai/read/devices")]
    [InlineData("PATCH", "/api/v1/ai/read/devices")]
    [InlineData("DELETE", "/api/v1/ai/read/device-logs")]
    public void Evaluate_ShouldRejectEveryNonGetMethod(string method, string path)
    {
        var decision = CloudAiReadEndpointPolicy.Evaluate(new HttpMethod(method), path);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("fixed GET");
    }
}
