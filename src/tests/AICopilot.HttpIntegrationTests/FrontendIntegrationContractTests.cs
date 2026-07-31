using System.Net;
using System.Text.Json;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Http;

namespace AICopilot.HttpIntegrationTests;

public sealed class OpenApiContractTests(OpenApiContractFixture fixture)
    : IClassFixture<OpenApiContractFixture>
{
    [Fact]
    public async Task OpenApi_ShouldExposeStableAigatewayAndRagRoutes()
    {
        using var response = await fixture.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        AssertPath(document, "/api/aigateway/language-model/list", "get");
        AssertPath(document, "/api/aigateway/runtime-settings", "get");
        AssertPath(document, "/api/aigateway/session", "post");
        AssertPath(document, "/api/aigateway/session/list", "get");
        AssertPath(document, "/api/aigateway/session/{sessionId}/agent-mode", "put");
        AssertPath(document, "/api/aigateway/chat-message/list", "get");
        AssertPath(document, "/api/aigateway/chat", "post");
        AssertPath(document, "/api/aigateway/approval/pending", "get");
        AssertPath(document, "/api/aigateway/approval/decision", "post");
        AssertPath(document, "/api/aigateway/tools", "get");
        AssertPath(document, "/api/aigateway/tools/{toolCode}", "get");
        AssertPath(document, "/api/aigateway/tools/{toolCode}", "patch");
        AssertPath(document, "/api/aigateway/cloud-readonly/status", "get");
        AssertPath(document, "/api/identity/login", "post");
        AssertPath(document, "/api/identity/cloud-oidc/status", "get");
        AssertPath(document, "/api/identity/cloud-oidc/finalize", "post");
        AssertPath(document, "/api/identity/cloud-oidc/confirm-existing", "post");
        AssertPath(document, "/api/identity/cloud-oidc/cancel", "post");
        AssertPath(document, "/api/identity/me", "get");
        AssertPath(document, "/api/identity/role/list", "get");
        AssertPath(document, "/api/identity/user/list", "get");
        AssertPath(document, "/api/system/build-identity", "get");
        AssertPath(document, "/api/data-analysis/business-database/list", "get");
        AssertPath(document, "/api/data-analysis/business-database/authorized", "get");
        AssertPath(document, "/api/data-analysis/business-database/query-readonly", "post");
        AssertPath(document, "/api/data-analysis/semantic-source/status", "get");
        AssertPath(document, "/api/mcp/server", "post");
        AssertPath(document, "/api/mcp/server", "put");
        AssertPath(document, "/api/rag/embedding-model/list", "get");
        AssertPath(document, "/api/rag/knowledge-base/list", "get");
        AssertPath(document, "/api/rag/document", "post");
        AssertPath(document, "/api/rag/document/list", "get");
        AssertPath(document, "/api/rag/document/governance", "put");
        AssertPath(document, "/api/rag/search", "post");

        AssertMissingPath(document, "/api/aigateway/agent/trial-scenarios");
        AssertMissingPath(document, "/api/aigateway/agent/trial-scenarios/create-task");
        AssertMissingPath(document, "/api/aigateway/upload");
        AssertMissingPath(document, "/api/aigateway/upload/list");
        AssertMissingPath(document, "/api/aigateway/agent/task/plan-stream");
        AssertMissingPath(document, "/api/aigateway/agent/task/run");
        AssertMissingPath(document, "/api/aigateway/agent/task/retry");
        AssertMissingPath(document, "/api/aigateway/agent/task/cancel");
        AssertMissingPath(document, "/api/aigateway/agent/approval/pending");
        AssertMissingPath(document, "/api/aigateway/workspace/{code}");
        AssertMissingPath(document, "/api/aigateway/workspace-settings");
        AssertMissingPath(document, "/api/aigateway/artifact/{id}/download");
        AssertMissingPath(document, "/api/aigateway/artifact/{id}/preview");
        AssertMissingPath(document, "/api/aigateway/approval-policy");
        AssertMissingPath(document, "/api/aigateway/approval-policy/list");
        AssertMissingPath(document, "/api/aigateway/session/timeline");
        AssertMissingPath(document, "/api/aigateway/session/safety-attestation");
        AssertMissingPath(document, "/api/aigateway/agent/task/plan");
        AssertMissingPath(document, "/api/aigateway/agent/cloud-sandbox-controlled-trial/plan");
        AssertMissingPath(document, "/api/aigateway/agent/cloud-production-controlled-pilot/plan");
        AssertMissingPath(document, "/api/aigateway/agent/task/{id}/run-queue");
        AssertMissingPath(document, "/api/aigateway/agent/run-queue");
        AssertMissingPath(document, "/api/aigateway/agent/run-queue/summary");
        AssertMissingPath(document, "/api/aigateway/agent/worker/status");
        AssertMissingPath(document, "/api/aigateway/cloud-readonly/readiness");
        AssertMissingPath(document, "/api/aigateway/trial-operations/campaigns");
        AssertMissingPath(document, "/api/aigateway/pilot-authorization/submissions");
        AssertMissingPath(document, "/api/aigateway/agent/task/execute");
        AssertMissingPath(document, "/api/aigateway/agent/task/plan-draft");
        AssertMissingPath(document, "/api/data-analysis/business-database/query");
        AssertMissingPath(document, "/api/rag/knowledge-base/search");
    }

    [Fact]
    public async Task BuildIdentity_ShouldExposeTheInjectedSourceWithoutAuthentication()
    {
        fixture.HttpClient.DefaultRequestHeaders.Authorization = null;

        using var response = await fixture.HttpClient.GetAsync("/api/system/build-identity");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("schemaVersion").GetString()
            .Should().Be("aicopilot-build-identity-v1");
        root.GetProperty("serviceName").GetString()
            .Should().Be("AICopilot.HttpApi");
        root.GetProperty("releaseTag").GetString()
            .Should().Be($"sha-{OpenApiContractFixture.SourceCommit}");
        root.GetProperty("sourceCommit").GetString()
            .Should().Be(OpenApiContractFixture.SourceCommit);
        root.GetProperty("available").GetBoolean().Should().BeTrue();
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "schemaVersion",
            "serviceName",
            "releaseTag",
            "sourceCommit",
            "available");
    }

    [Fact]
    public async Task OpenApi_ShouldLockCriticalRequestSchemasAndProblemDetailsResponses()
    {
        using var response = await fixture.HttpClient.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        AssertRequestSchemaProperties(
            document,
            "/api/identity/login",
            "post",
            "username",
            "password");
        AssertRequestSchemaProperties(
            document,
            "/api/identity/cloud-oidc/confirm-existing",
            "post",
            "password");
        AssertRequestSchemaProperties(
            document,
            "/api/aigateway/chat",
            "post",
            "sessionId",
            "message");
        AssertRequestSchemaProperties(
            document,
            "/api/aigateway/session/{sessionId}/agent-mode",
            "put",
            "mode",
            "expectedVersion");
        AssertRequestSchemaProperties(
            document,
            "/api/aigateway/approval/decision",
            "post",
            "sessionId",
            "callId",
            "decision");
        AssertRequestSchemaProperties(
            document,
            "/api/aigateway/tools/{toolCode}",
            "patch",
            "displayName",
            "description",
            "riskLevel",
            "requiresApproval",
            "isEnabled",
            "timeoutSeconds");
        foreach (var method in new[] { "post", "put" })
        {
            AssertRequestSchemaProperties(
                document,
                "/api/mcp/server",
                method,
                "externalSystemType",
                "capabilityKind");
            AssertRequestSchemaRequiredProperties(
                document,
                "/api/mcp/server",
                method,
                "externalSystemType",
                "capabilityKind");
        }
        AssertRequestSchemaProperties(
            document,
            "/api/rag/search",
            "post",
            "knowledgeBaseId",
            "queryText",
            "topK",
            "minScore");

        foreach (var (path, method) in new[]
                 {
                     ("/api/identity/login", "post"),
                     ("/api/data-analysis/business-database/query-readonly", "post"),
                     ("/api/mcp/server", "post"),
                     ("/api/mcp/server", "put"),
                     ("/api/rag/search", "post")
                 })
        {
            AssertProblemDetailsResponses(document, path, method);
        }
    }

    [Theory]
    [InlineData("/api/aigateway/agent/task/plan-stream")]
    [InlineData("/api/aigateway/agent/approval/pending")]
    [InlineData("/api/aigateway/upload")]
    [InlineData("/api/aigateway/workspace/legacy")]
    [InlineData("/api/aigateway/artifact/00000000-0000-0000-0000-000000000001/preview")]
    [InlineData("/api/aigateway/approval-policy")]
    [InlineData("/api/aigateway/session/timeline")]
    [InlineData("/api/aigateway/session/safety-attestation")]
    public async Task LegacyAgentRuntimeRoutes_ShouldReturnNotFound(string route)
    {
        fixture.HttpClient.DefaultRequestHeaders.Authorization = null;

        using var response = await fixture.HttpClient.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void ProblemDetailsFactory_ShouldKeepStableFrontendErrorShape()
    {
        var details = ApiProblemDetailsFactory.Create(
            StatusCodes.Status403Forbidden,
            new ApiProblemDescriptor(
                AuthProblemCodes.MissingPermission,
                "当前账号缺少所需权限。",
                new Dictionary<string, object?>
                {
                    ["correlationId"] = "corr-contract"
                }));

        details.Status.Should().Be(StatusCodes.Status403Forbidden);
        details.Title.Should().Be("Forbidden");
        details.Type.Should().EndWith("/403");
        details.Detail.Should().Be("当前账号缺少所需权限。");
        details.Extensions["code"].Should().Be(AuthProblemCodes.MissingPermission);
        details.Extensions["correlationId"].Should().Be("corr-contract");
    }

    private static void AssertPath(JsonDocument document, string path, string method)
    {
        var paths = document.RootElement.GetProperty("paths");
        var availablePaths = paths
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        paths.TryGetProperty(path, out var pathElement)
            .Should()
            .BeTrue($"OpenAPI should expose {path}; available paths: {string.Join(", ", availablePaths)}");
        pathElement.TryGetProperty(method, out _)
            .Should()
            .BeTrue($"OpenAPI should expose {method.ToUpperInvariant()} {path}");
    }

    private static void AssertMissingPath(JsonDocument document, string path)
    {
        var paths = document.RootElement.GetProperty("paths");
        paths.TryGetProperty(path, out _)
            .Should()
            .BeFalse($"OpenAPI should not expose legacy product route {path}");
    }

    private static void AssertRequestSchemaProperties(
        JsonDocument document,
        string path,
        string method,
        params string[] expectedProperties)
    {
        var operation = GetOperation(document, path, method);
        var requestBody = operation.GetProperty("requestBody");
        var schema = ResolveSchema(
            document,
            requestBody.GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        var properties = schema.GetProperty("properties");
        foreach (var expectedProperty in expectedProperties)
        {
            properties.TryGetProperty(expectedProperty, out _)
                .Should()
                .BeTrue($"OpenAPI request schema for {method.ToUpperInvariant()} {path} should expose {expectedProperty}");
        }
    }

    private static void AssertProblemDetailsResponses(JsonDocument document, string path, string method)
    {
        var responses = GetOperation(document, path, method).GetProperty("responses");
        foreach (var statusCode in new[] { "400", "401", "403", "404", "429", "500" })
        {
            responses.TryGetProperty(statusCode, out var response)
                .Should()
                .BeTrue($"OpenAPI should document {statusCode} ProblemDetails for {method.ToUpperInvariant()} {path}");
            var content = response.GetProperty("content");
            var hasProblemJson = content.TryGetProperty("application/problem+json", out var mediaType);
            if (!hasProblemJson)
            {
                content.TryGetProperty("application/json", out mediaType)
                    .Should()
                    .BeTrue($"OpenAPI {statusCode} response should document a JSON ProblemDetails body");
            }
            var schema = ResolveSchema(document, mediaType.GetProperty("schema"));
            var properties = schema.GetProperty("properties");
            foreach (var property in new[] { "type", "title", "status", "detail", "instance" })
            {
                properties.TryGetProperty(property, out _)
                    .Should()
                    .BeTrue($"ProblemDetails schema should expose {property}");
            }
        }
    }

    private static void AssertRequestSchemaRequiredProperties(
        JsonDocument document,
        string path,
        string method,
        params string[] expectedRequiredProperties)
    {
        var operation = GetOperation(document, path, method);
        var requestBody = operation.GetProperty("requestBody");
        var schema = requestBody.GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var requiredProperties = EnumerateRequiredProperties(document, schema)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var expectedProperty in expectedRequiredProperties)
        {
            requiredProperties.Should().Contain(
                expectedProperty,
                $"OpenAPI request schema for {method.ToUpperInvariant()} {path} should require {expectedProperty}");
        }
    }

    private static IEnumerable<string> EnumerateRequiredProperties(
        JsonDocument document,
        JsonElement schema)
    {
        var resolvedSchema = ResolveSchema(document, schema);
        if (resolvedSchema.TryGetProperty("required", out var required))
        {
            foreach (var property in required.EnumerateArray())
            {
                if (property.GetString() is { } propertyName)
                {
                    yield return propertyName;
                }
            }
        }

        if (!resolvedSchema.TryGetProperty("allOf", out var allOf))
        {
            yield break;
        }

        foreach (var component in allOf.EnumerateArray())
        {
            foreach (var propertyName in EnumerateRequiredProperties(document, component))
            {
                yield return propertyName;
            }
        }
    }

    private static JsonElement GetOperation(JsonDocument document, string path, string method)
    {
        return document.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method);
    }

    private static JsonElement ResolveSchema(JsonDocument document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        const string prefix = "#/components/schemas/";
        var referenceValue = reference.GetString();
        referenceValue.Should().StartWith(prefix);
        return document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(referenceValue![prefix.Length..]);
    }

}

public sealed class OpenApiContractFixture : AICopilotAppFixture
{
    public const string SourceCommit = "0123456789abcdef0123456789abcdef01234567";

    protected override bool EnableRagWorker => false;

    protected override bool EnableDataWorker => false;

    protected override void ConfigureAdditionalEnvironment()
    {
        SetEnvironmentVariable("AICOPILOT_SOURCE_SHA", SourceCommit);
        SetEnvironmentVariable("AICOPILOT_RELEASE_TAG", $"sha-{SourceCommit}");
    }
}
