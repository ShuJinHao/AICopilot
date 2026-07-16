using AICopilot.HttpApi.Infrastructure;
using AICopilot.IdentityService.Authorization;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace AICopilot.InProcessTests;

public sealed class IdentityProblemContractTests
{
    [Fact]
    public void LastEnabledAdmin_ShouldExposeStableProblemDetailsContract()
    {
        var details = ApiProblemDetailsFactory.Create(
            StatusCodes.Status400BadRequest,
            IdentityProblemDescriptors.LastEnabledAdminRequired(),
            traceIdentifier: "trace-contract");

        details.Status.Should().Be(StatusCodes.Status400BadRequest);
        details.Detail.Should().Be(IdentityProblemDescriptors.LastEnabledAdminDetail);
        details.Extensions["code"].Should().Be(AuthProblemCodes.LastEnabledAdminRequired);
        details.Extensions[ApiProblemExtensionKeys.UserFacingMessage]
            .Should().Be(IdentityProblemDescriptors.LastEnabledAdminUserFacingMessage);
        details.Extensions[ApiProblemExtensionKeys.TraceId].Should().Be("trace-contract");
    }

    [Fact]
    public void MixedCaseReservedProblemExtensions_ShouldSerializeOnlyCanonicalCodeAndTrace()
    {
        var details = ApiProblemDetailsFactory.Create(
            StatusCodes.Status400BadRequest,
            new ApiProblemDescriptor(
                AuthProblemCodes.LastEnabledAdminRequired,
                IdentityProblemDescriptors.LastEnabledAdminDetail,
                new Dictionary<string, object?>
                {
                    [ApiProblemExtensionKeys.Code] = "forged_code",
                    ["Code"] = "mixed-case-forged-code",
                    [ApiProblemExtensionKeys.TraceId] = "forged-trace",
                    ["TRACEID"] = "mixed-case-forged-trace",
                    [ApiProblemExtensionKeys.UserFacingMessage] =
                        IdentityProblemDescriptors.LastEnabledAdminUserFacingMessage
                }),
            traceIdentifier: "canonical-http-trace");

        details.Extensions[ApiProblemExtensionKeys.Code]
            .Should().Be(AuthProblemCodes.LastEnabledAdminRequired);
        details.Extensions[ApiProblemExtensionKeys.TraceId]
            .Should().Be("canonical-http-trace");
        details.Extensions[ApiProblemExtensionKeys.UserFacingMessage]
            .Should().Be(IdentityProblemDescriptors.LastEnabledAdminUserFacingMessage);

        details.Extensions.Keys
            .Count(key => string.Equals(
                key,
                ApiProblemExtensionKeys.Code,
                StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        details.Extensions.Keys
            .Count(key => string.Equals(
                key,
                ApiProblemExtensionKeys.TraceId,
                StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        details.Extensions.Keys.Should().Contain(ApiProblemExtensionKeys.Code);
        details.Extensions.Keys.Should().Contain(ApiProblemExtensionKeys.TraceId);

        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(
            details,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var serializedKeys = serialized.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        serializedKeys.Count(key => string.Equals(
                key,
                ApiProblemExtensionKeys.Code,
                StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        serializedKeys.Count(key => string.Equals(
                key,
                ApiProblemExtensionKeys.TraceId,
                StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        serializedKeys.Should().Contain(ApiProblemExtensionKeys.Code);
        serializedKeys.Should().Contain(ApiProblemExtensionKeys.TraceId);
    }
}
