using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.RagService.EmbeddingModels;
using AICopilot.Services.Contracts.AiGateway.Dtos;
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
        AssertPath(document, "/api/aigateway/upload", "post");
        AssertPath(document, "/api/aigateway/agent/task/plan-stream", "post");
        AssertPath(document, "/api/aigateway/agent/task/run", "post");
        AssertPath(document, "/api/aigateway/agent/task/retry", "post");
        AssertPath(document, "/api/aigateway/agent/task/cancel", "post");
        AssertPath(document, "/api/aigateway/agent/task/{id}/approvals", "get");
        AssertPath(document, "/api/aigateway/agent/task/{id}/audit-summary", "get");
        AssertPath(document, "/api/aigateway/agent/task/{id}/tool-executions", "get");
        AssertPath(document, "/api/aigateway/agent/task/{id}/run-attempts", "get");
        AssertPath(document, "/api/aigateway/tools", "get");
        AssertPath(document, "/api/aigateway/tools/{toolCode}", "get");
        AssertPath(document, "/api/aigateway/tools/{toolCode}", "patch");
        AssertPath(document, "/api/aigateway/workspace/{code}", "get");
        AssertPath(document, "/api/aigateway/artifact/{id}/download", "get");
        AssertPath(document, "/api/aigateway/artifact/{id}/preview", "get");
        AssertPath(document, "/api/aigateway/artifact/{id}/revision-comment", "post");
        AssertPath(document, "/api/aigateway/artifact/{id}/regenerate-draft", "post");
        AssertPath(document, "/api/aigateway/artifact/{id}/submit-final-approval", "post");
        AssertPath(document, "/api/aigateway/cloud-readonly/status", "get");
        AssertPath(document, "/api/identity/login", "post");
        AssertPath(document, "/api/identity/cloud-oidc/status", "get");
        AssertPath(document, "/api/identity/cloud-oidc/finalize", "post");
        AssertPath(document, "/api/identity/me", "get");
        AssertPath(document, "/api/identity/role/list", "get");
        AssertPath(document, "/api/identity/user/list", "get");
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
            "/api/aigateway/agent/task/plan-stream",
            "post",
            "sessionId",
            "goal",
            "taskType",
            "modelId",
            "uploadIds",
            "knowledgeBaseIds",
            "dataSourceIds",
            "requiresDataApproval",
            "artifactTypes");
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
                     ("/api/aigateway/agent/task/run", "post"),
                     ("/api/data-analysis/business-database/query-readonly", "post"),
                     ("/api/mcp/server", "post"),
                     ("/api/mcp/server", "put"),
                     ("/api/rag/search", "post")
                 })
        {
            AssertProblemDetailsResponses(document, path, method);
        }

        fixture.HttpClient.DefaultRequestHeaders.Authorization = null;
        using var loginResponse = await fixture.HttpClient.PostAsJsonAsync(
            "/api/identity/login",
            new
            {
                username = fixture.BootstrapAdminUserName,
                password = fixture.BootstrapAdminPassword
            });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var loginDocument = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        fixture.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            loginDocument.RootElement.GetProperty("token").GetString());

        try
        {
            var sessionId = Guid.NewGuid();
            var validPayload = $$"""
            {
              "sessionId": "{{sessionId:D}}",
              "goal": "生成只读计划草案",
              "taskType": "ReportGeneration",
              "pluginSelectionMode": "BuiltInOnly",
              "selectedPluginIds": [],
              "capabilitySelectionMode": "InferredFromGoal",
              "requestedCapabilityCodes": []
            }
            """;
            var invalidSelectionPayloads = new[]
            {
                validPayload.Replace(
                    "\"pluginSelectionMode\": \"BuiltInOnly\"",
                    "\"pluginSelectionMode\": 1",
                    StringComparison.Ordinal),
                validPayload.Replace(
                    "\"pluginSelectionMode\": \"BuiltInOnly\"",
                    "\"pluginSelectionMode\": \"Unknown\"",
                    StringComparison.Ordinal),
                validPayload.Replace(
                    "\"capabilitySelectionMode\": \"InferredFromGoal\"",
                    "\"capabilitySelectionMode\": 1",
                    StringComparison.Ordinal),
                validPayload.Replace(
                    "\"capabilitySelectionMode\": \"InferredFromGoal\"",
                    "\"capabilitySelectionMode\": \"Unknown\"",
                    StringComparison.Ordinal)
            };

            foreach (var invalidPayload in invalidSelectionPayloads)
            {
                using var invalidResponse = await SendPlanStreamAsync(invalidPayload);
                var invalidBody = await invalidResponse.Content.ReadAsStringAsync();
                invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                invalidResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
                invalidBody.Should().NotContain("session_not_found");
                invalidBody.Should().NotContain("plan_draft_started");
            }

            foreach (var (legacyProperty, legacyValue) in new[]
                     {
                         (
                             "skillCode",
                             "\"knowledge_research\""),
                         (
                             "preferredToolCodes",
                             "[\"rag_search\"]")
                     })
            {
                var legacyPayload = validPayload.Replace(
                    "\"requestedCapabilityCodes\": []",
                    $"\"requestedCapabilityCodes\": [], \"{legacyProperty}\": {legacyValue}",
                    StringComparison.Ordinal);
                using var legacyResponse = await SendPlanStreamAsync(legacyPayload);
                var legacyBody = await legacyResponse.Content.ReadAsStringAsync();

                legacyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                legacyResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
                legacyBody.Should().Contain(AppProblemCodes.AgentPlanSchemaInvalid);
                legacyBody.Should().Contain("Agent task plan does not match the required schema.");
                legacyBody.Should().NotContain("retired for Plan v2");
                legacyBody.Should().NotContain("session_not_found");
                legacyBody.Should().NotContain("plan_draft_started");
            }
        }
        finally
        {
            fixture.HttpClient.DefaultRequestHeaders.Authorization = null;
        }
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

    private async Task<HttpResponseMessage> SendPlanStreamAsync(string payload)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/aigateway/agent/task/plan-stream")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        return await fixture.HttpClient.SendAsync(request);
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
    protected override bool EnableRagWorker => false;

    protected override bool EnableDataWorker => false;
}
