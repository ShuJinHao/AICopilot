using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AICopilot.ToolPlugin.ConformanceTests;

public sealed class McpV2HttpAutoDetectTests
{
    [Fact]
    public async Task AutoDetect_ShouldProbeDiscovery_ThenFallBackToLegacyInitialize()
    {
        var handler = new LegacyMcpHttpHandler(omitInputSchema: false);
        using var httpClient = new HttpClient(handler);
        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("https://legacy-mcp.example.test/mcp"),
                TransportMode = HttpTransportMode.AutoDetect,
                EnableStandaloneGetStream = false
            },
            httpClient,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);
        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: CancellationToken.None);

        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);
        var tool = tools.Should().ContainSingle().Which;
        tool.Name.Should().Be("queryLegacy");
        tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
        tool.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString().Should().Be("string");

        var result = await tool.CallAsync(cancellationToken: CancellationToken.None);
        result.StructuredContent!.Value.GetString().Should().Be("legacy-ok");
        handler.Methods.Should().ContainInOrder(
            "server/discover",
            "initialize",
            "notifications/initialized",
            "tools/list",
            "tools/call");
        handler.DiscoveryProtocolVersion.Should().Be("2026-07-28");
    }

    [Fact]
    public async Task LegacyListTools_ShouldRejectMissingInputSchema()
    {
        var handler = new LegacyMcpHttpHandler(omitInputSchema: true);
        using var httpClient = new HttpClient(handler);
        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("https://legacy-mcp.example.test/mcp"),
                TransportMode = HttpTransportMode.AutoDetect,
                EnableStandaloneGetStream = false
            },
            httpClient,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);
        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: CancellationToken.None);

        var action = () => client.ListToolsAsync(cancellationToken: CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<JsonException>();
    }

    private sealed class LegacyMcpHttpHandler(bool omitInputSchema) : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];

        public string? DiscoveryProtocolVersion { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post || request.Content is null)
            {
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }

            var payload = await request.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString()!;
            Methods.Add(method);
            request.Headers.TryGetValues("MCP-Protocol-Version", out var versions);

            return method switch
            {
                "server/discover" => RejectDiscovery(root, versions?.SingleOrDefault()),
                "initialize" => Initialize(root),
                "notifications/initialized" => new HttpResponseMessage(HttpStatusCode.Accepted),
                "tools/list" => ListTools(root),
                "tools/call" => CallTool(root),
                _ => RpcError(root, -32601, "Method not found")
            };
        }

        private HttpResponseMessage RejectDiscovery(JsonElement request, string? protocolVersion)
        {
            DiscoveryProtocolVersion = protocolVersion;
            return RpcError(request, -32601, "Method not found");
        }

        private static HttpResponseMessage Initialize(JsonElement request)
        {
            var response = RpcResult(
                request,
                new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "legacy-test-server", version = "1.0.0" }
                });
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", "legacy-test-session");
            return response;
        }

        private HttpResponseMessage ListTools(JsonElement request)
        {
            var tool = omitInputSchema
                ? new Dictionary<string, object?>
                {
                    ["name"] = "queryLegacy",
                    ["description"] = "Query legacy read-only data.",
                    ["outputSchema"] = new { type = "string" },
                    ["annotations"] = new
                    {
                        readOnlyHint = true,
                        destructiveHint = false,
                        idempotentHint = true
                    }
                }
                : new Dictionary<string, object?>
                {
                    ["name"] = "queryLegacy",
                    ["description"] = "Query legacy read-only data.",
                    ["inputSchema"] = new
                    {
                        type = "object",
                        properties = new { },
                        additionalProperties = false
                    },
                    ["outputSchema"] = new { type = "string" },
                    ["annotations"] = new
                    {
                        readOnlyHint = true,
                        destructiveHint = false,
                        idempotentHint = true
                    }
                };
            return RpcResult(request, new { tools = new[] { tool } });
        }

        private static HttpResponseMessage CallTool(JsonElement request)
        {
            return RpcResult(
                request,
                new
                {
                    content = Array.Empty<object>(),
                    structuredContent = "legacy-ok",
                    isError = false
                });
        }

        private static HttpResponseMessage RpcError(
            JsonElement request,
            int code,
            string message)
        {
            return JsonResponse(new
            {
                jsonrpc = "2.0",
                id = request.GetProperty("id").Clone(),
                error = new { code, message }
            });
        }

        private static HttpResponseMessage RpcResult(JsonElement request, object result)
        {
            return JsonResponse(new
            {
                jsonrpc = "2.0",
                id = request.GetProperty("id").Clone(),
                result
            });
        }

        private static HttpResponseMessage JsonResponse(object payload)
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
