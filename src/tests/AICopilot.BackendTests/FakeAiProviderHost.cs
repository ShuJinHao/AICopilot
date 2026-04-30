using System.Net;
using System.Net.Mime;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AICopilot.BackendTests;

public sealed class FakeAiProviderHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private WebApplication? _app;

    public Uri BaseUri { get; private set; } = null!;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app != null)
        {
            return;
        }

        var port = GetRandomUnusedPort();

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

        var app = builder.Build();

        app.MapPost("/v1/chat/completions", HandleChatCompletionsAsync);
        app.MapPost("/chat/completions", HandleChatCompletionsAsync);
        app.MapPost("/v1/embeddings", HandleEmbeddingsAsync);
        app.MapPost("/embeddings", HandleEmbeddingsAsync);

        await app.StartAsync(cancellationToken);

        _app = app;
        BaseUri = new Uri($"http://127.0.0.1:{port}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static async Task HandleChatCompletionsAsync(HttpContext context)
    {
        using var document = await JsonDocument.ParseAsync(context.Request.Body);
        var root = document.RootElement;
        var stream = root.TryGetProperty("stream", out var streamElement) && streamElement.GetBoolean();

        var messageTexts = ExtractMessageTexts(root);
        var latestUserText = ExtractLatestUserText(root);
        var hasToolResult = HasToolResultMessage(root);
        var isIntentRouting = messageTexts.Any(text => text.Contains("General.Chat", StringComparison.OrdinalIgnoreCase));

        if (stream)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";

            if (hasToolResult)
            {
                await WriteTextStreamAsync(context, "已批准并执行工具。");
                return;
            }

            if (ShouldBlockControlRequest(latestUserText))
            {
                await WriteTextStreamAsync(context, BuildControlBoundaryResponse());
                return;
            }

            if (ShouldTriggerDiagnosticApproval(latestUserText))
            {
                var toolName = ExtractDiagnosticToolName(root) ?? "GenerateDiagnosticChecklist";
                await WriteToolCallStreamAsync(context, toolName);
                return;
            }

            await WriteTextStreamAsync(context, ResolvePlainTextResponse(messageTexts, latestUserText));
            return;
        }

        var content = isIntentRouting
            ? ResolveIntentResponse(latestUserText)
            : ResolvePlainTextResponse(messageTexts, latestUserText);

        var payload = new
        {
            id = $"chatcmpl-{Guid.NewGuid():N}",
            @object = "chat.completion",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() ?? "fake-chat-model" : "fake-chat-model",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new
                    {
                        role = "assistant",
                        content
                    },
                    finish_reason = "stop"
                }
            }
        };

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static async Task HandleEmbeddingsAsync(HttpContext context)
    {
        using var document = await JsonDocument.ParseAsync(context.Request.Body);
        var root = document.RootElement;

        var inputs = root.TryGetProperty("input", out var inputElement)
            ? ExtractEmbeddingInputs(inputElement)
            : [];

        var data = inputs
            .Select((value, index) => new
            {
                @object = "embedding",
                index,
                embedding = CreateEmbedding(value)
            })
            .ToArray();

        var payload = new
        {
            @object = "list",
            data,
            model = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() ?? "fake-embedding-model" : "fake-embedding-model",
            usage = new
            {
                prompt_tokens = Math.Max(inputs.Count, 1),
                total_tokens = Math.Max(inputs.Count, 1)
            }
        };

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static async Task WriteTextStreamAsync(HttpContext context, string text)
    {
        var chunks = text.Chunk(Math.Max(1, Math.Min(12, text.Length))).Select(chars => new string(chars)).ToArray();

        foreach (var chunk in chunks)
        {
            var payload = new
            {
                id = $"chatcmpl-{Guid.NewGuid():N}",
                @object = "chat.completion.chunk",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                model = "fake-chat-model",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new
                        {
                            content = chunk
                        },
                        finish_reason = (string?)null
                    }
                }
            };

            await WriteSseAsync(context, payload);
        }

        await WriteSseAsync(context, new
        {
            id = $"chatcmpl-{Guid.NewGuid():N}",
            @object = "chat.completion.chunk",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = "fake-chat-model",
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new { },
                    finish_reason = "stop"
                }
            }
        });

        await WriteDoneAsync(context);
    }

    private static async Task WriteToolCallStreamAsync(HttpContext context, string toolName)
    {
        var callId = $"call_{Guid.NewGuid():N}";
        var argumentsJson = JsonSerializer.Serialize(new { });

        await WriteSseAsync(context, new
        {
            id = $"chatcmpl-{Guid.NewGuid():N}",
            @object = "chat.completion.chunk",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = "fake-chat-model",
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new
                    {
                        tool_calls = new[]
                        {
                            new
                            {
                                index = 0,
                                id = callId,
                                type = "function",
                                function = new
                                {
                                    name = toolName,
                                    arguments = argumentsJson
                                }
                            }
                        }
                    },
                    finish_reason = (string?)null
                }
            }
        });

        await WriteSseAsync(context, new
        {
            id = $"chatcmpl-{Guid.NewGuid():N}",
            @object = "chat.completion.chunk",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = "fake-chat-model",
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new { },
                    finish_reason = "tool_calls"
                }
            }
        });

        await WriteDoneAsync(context);
    }

    private static Task WriteSseAsync(HttpContext context, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return WriteRawSseAsync(context, $"data: {json}\n\n");
    }

    private static Task WriteDoneAsync(HttpContext context)
    {
        return WriteRawSseAsync(context, "data: [DONE]\n\n");
    }

    private static async Task WriteRawSseAsync(HttpContext context, string line)
    {
        await context.Response.WriteAsync(line);
        await context.Response.Body.FlushAsync();
    }

    private static bool ShouldTriggerDiagnosticApproval(string latestUserText)
    {
        return latestUserText.Contains("diagnostic checklist", StringComparison.OrdinalIgnoreCase)
               || latestUserText.Contains("诊断清单", StringComparison.OrdinalIgnoreCase)
               || latestUserText.Contains("排查清单", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldBlockControlRequest(string latestUserText)
    {
        return latestUserText.Contains("restart the server", StringComparison.OrdinalIgnoreCase)
               || latestUserText.Contains("restart server", StringComparison.OrdinalIgnoreCase)
               || latestUserText.Contains("重启服务器", StringComparison.OrdinalIgnoreCase)
               || latestUserText.Contains("重启服务", StringComparison.OrdinalIgnoreCase)
               || latestUserText.Contains("下发参数", StringComparison.OrdinalIgnoreCase)
               || latestUserText.Contains("写参数", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildControlBoundaryResponse()
    {
        return "我不能直接执行重启、写参数、下发配方或其他控制动作，但可以继续提供诊断分析、根因排查和人工执行前的检查清单。";
    }

    private static string ResolveIntentResponse(string latestUserText)
    {
        if (TryResolveSemanticIntent(latestUserText, out var semanticIntent))
        {
            return JsonSerializer.Serialize(new[] { semanticIntent }, JsonOptions);
        }

        if (TryResolvePolicyIntent(latestUserText, out var policyIntent))
        {
            return JsonSerializer.Serialize(new[] { policyIntent }, JsonOptions);
        }

        if (ShouldBlockControlRequest(latestUserText))
        {
            return JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        intent = "General.Chat",
                        confidence = 0.99,
                        reasoning = "The request asks for a control action, which must be refused and redirected to diagnostics-only guidance."
                    }
                },
                JsonOptions);
        }

        var result = ShouldTriggerDiagnosticApproval(latestUserText)
            ? new[]
            {
                new
                {
                    intent = "Action.DiagnosticAdvisorPlugin",
                    confidence = 0.99,
                    reasoning = "The user explicitly asked for a high-risk diagnostic checklist that still requires human review."
                }
            }
            : new[]
            {
                new
                {
                    intent = "General.Chat",
                    confidence = 0.99,
                    reasoning = "The user is asking for a normal conversational response."
                }
            };

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static string ResolvePlainTextResponse(IReadOnlyCollection<string> messageTexts, string latestUserText)
    {
        if (TryResolveSemanticAnswer(latestUserText, out var semanticAnswer))
        {
            return semanticAnswer;
        }

        if (TryResolveBusinessPolicyAnswer(latestUserText, out var businessPolicyAnswer))
        {
            return businessPolicyAnswer;
        }

        return latestUserText switch
        {
            _ when ShouldBlockControlRequest(latestUserText)
                => BuildControlBoundaryResponse(),
            _ when messageTexts.Any(text => text.Contains("未处于只读模式", StringComparison.OrdinalIgnoreCase))
                => "结论：当前查询已被拒绝，因为目标数据源未处于只读模式。",
            _ when messageTexts.Any(text => text.Contains("只读数据源", StringComparison.OrdinalIgnoreCase))
                => "结论：当前查询已被拒绝，因为只读数据源不可用或配置错误。",
            _ when messageTexts.Any(text => text.Contains("安全警告", StringComparison.OrdinalIgnoreCase))
                => "结论：当前查询已被系统安全策略拒绝。",
            _ when latestUserText.Contains("你好", StringComparison.OrdinalIgnoreCase) => "你好，我在。",
            _ when latestUserText.Contains("hello", StringComparison.OrdinalIgnoreCase) => "Hello, I am ready.",
            _ when messageTexts.Any(text => text.Contains("database_name", StringComparison.OrdinalIgnoreCase)) => "结论：未查询到匹配的数据。",
            _ => $"Received: {latestUserText}"
        };
    }

    private static bool TryResolveSemanticIntent(string latestUserText, out object semanticIntent)
    {
        semanticIntent = null!;

        if (TryMatchDeviceList(latestUserText, out var listLine))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.Device.List",
                "The user requested a device list.",
                filters: string.IsNullOrWhiteSpace(listLine)
                    ? null
                    : new[]
                    {
                        new { field = "lineName", @operator = "eq", value = listLine }
                    },
                sort: new { field = "deviceCode", direction = "asc" },
                limit: 20,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchDeviceDetail(latestUserText, out var detailCode))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.Device.Detail",
                "The user requested the detail of one device.",
                filters: new[]
                {
                    new { field = "deviceCode", @operator = "eq", value = detailCode }
                },
                limit: 1,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchDeviceStatus(latestUserText, out var statusCode))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.Device.Status",
                "The user requested device status.",
                filters: new[]
                {
                    new { field = "deviceCode", @operator = "eq", value = statusCode }
                },
                sort: new { field = "updatedAt", direction = "desc" },
                limit: 10,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchDeviceLogRange(latestUserText, out var rangeCode, out var rangeStart, out var rangeEnd))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.DeviceLog.Range",
                "The user requested logs within a time range.",
                filters: new[]
                {
                    new { field = "deviceCode", @operator = "eq", value = rangeCode }
                },
                timeRange: new
                {
                    field = "occurredAt",
                    start = rangeStart,
                    end = rangeEnd
                },
                sort: new { field = "occurredAt", direction = "desc" },
                limit: 50,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchDeviceLogByLevel(latestUserText, out var levelCode, out var level))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.DeviceLog.ByLevel",
                "The user requested logs filtered by severity level.",
                filters: new[]
                {
                    new { field = "deviceCode", @operator = "eq", value = levelCode },
                    new { field = "level", @operator = "eq", value = level }
                },
                sort: new { field = "occurredAt", direction = "desc" },
                limit: 20,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchLatestDeviceLog(latestUserText, out var latestCode))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.DeviceLog.Latest",
                "The user requested the latest logs for one device.",
                filters: new[]
                {
                    new { field = "deviceCode", @operator = "eq", value = latestCode }
                },
                sort: new { field = "occurredAt", direction = "desc" },
                limit: 10,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchRecipeVersionHistory(latestUserText, out var recipeNameForHistory))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.Recipe.VersionHistory",
                "The user requested recipe version history.",
                filters: new[]
                {
                    new { field = "recipeName", @operator = "eq", value = recipeNameForHistory }
                },
                sort: new { field = "version", direction = "desc" },
                limit: 20,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchRecipeDetail(latestUserText, out var recipeNameForDetail))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.Recipe.Detail",
                "The user requested one recipe detail.",
                filters: new[]
                {
                    new { field = "recipeName", @operator = "eq", value = recipeNameForDetail }
                },
                sort: new { field = "updatedAt", direction = "desc" },
                limit: 1,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchRecipeList(latestUserText, out var recipeDeviceCode))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.Recipe.List",
                "The user requested recipe list.",
                filters: string.IsNullOrWhiteSpace(recipeDeviceCode)
                    ? null
                    : new[]
                    {
                        new { field = "deviceCode", @operator = "eq", value = recipeDeviceCode }
                    },
                sort: new { field = "updatedAt", direction = "desc" },
                limit: 20,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchCapacityRange(latestUserText, out var capacityRangeDeviceCode, out var capacityRangeStart, out var capacityRangeEnd))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.Capacity.Range",
                "The user requested capacity within a time range.",
                filters: new[]
                {
                    new { field = "deviceCode", @operator = "eq", value = capacityRangeDeviceCode }
                },
                timeRange: new
                {
                    field = "occurredAt",
                    start = capacityRangeStart,
                    end = capacityRangeEnd
                },
                sort: new { field = "occurredAt", direction = "desc" },
                limit: 50,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchCapacityByProcess(latestUserText, out var processName))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.Capacity.ByProcess",
                "The user requested capacity by process.",
                filters: new[]
                {
                    new { field = "processName", @operator = "eq", value = processName }
                },
                sort: new { field = "occurredAt", direction = "desc" },
                limit: 50,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchCapacityByDevice(latestUserText, out var capacityDeviceCode))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.Capacity.ByDevice",
                "The user requested capacity by device.",
                filters: new[]
                {
                    new { field = "deviceCode", @operator = "eq", value = capacityDeviceCode }
                },
                sort: new { field = "occurredAt", direction = "desc" },
                limit: 50,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchProductionRange(latestUserText, out var productionRangeDeviceCode, out var productionRangeStart, out var productionRangeEnd))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.ProductionData.Range",
                "The user requested production data within a time range.",
                filters: new[]
                {
                    new { field = "deviceCode", @operator = "eq", value = productionRangeDeviceCode }
                },
                timeRange: new
                {
                    field = "occurredAt",
                    start = productionRangeStart,
                    end = productionRangeEnd
                },
                sort: new { field = "occurredAt", direction = "desc" },
                limit: 50,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchProductionLatest(latestUserText, out var productionLatestDeviceCode))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.ProductionData.Latest",
                "The user requested latest production data.",
                filters: new[]
                {
                    new { field = "deviceCode", @operator = "eq", value = productionLatestDeviceCode }
                },
                sort: new { field = "occurredAt", direction = "desc" },
                limit: 20,
                queryText: latestUserText);
            return true;
        }

        if (TryMatchProductionByDevice(latestUserText, out var productionDeviceCode))
        {
            semanticIntent = CreateSemanticIntent(
                "Analysis.ProductionData.ByDevice",
                "The user requested production data by device.",
                filters: new[]
                {
                    new { field = "deviceCode", @operator = "eq", value = productionDeviceCode }
                },
                sort: new { field = "occurredAt", direction = "desc" },
                limit: 50,
                queryText: latestUserText);
            return true;
        }

        return false;
    }

    private static bool TryMatchDeviceList(string text, out string lineName)
    {
        lineName = string.Empty;

        var englishMatch = Regex.Match(
            text,
            @"list\s+devices(?:\s+on\s+line\s+(?<line>[A-Za-z0-9\-_]+))?",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            lineName = englishMatch.Groups["line"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"(?:列出|查看|看看|查询)\s*(?<line>[A-Za-z0-9\-_]+)\s*(?:产线|线体).*(?:设备|机台)",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            lineName = chineseMatch.Groups["line"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchDeviceDetail(string text, out string deviceCode)
    {
        deviceCode = string.Empty;

        var englishMatch = Regex.Match(
            text,
            @"detail\s+for\s+device\s+(?<code>[A-Za-z0-9\-_]+)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            deviceCode = englishMatch.Groups["code"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"(?:查看|查询|显示).*(?:设备)\s*(?<code>[A-Za-z0-9\-_]+).*详情",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            deviceCode = chineseMatch.Groups["code"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchDeviceStatus(string text, out string deviceCode)
    {
        deviceCode = string.Empty;

        var englishMatch = Regex.Match(
            text,
            @"status\s+of\s+device\s+(?<code>[A-Za-z0-9\-_]+)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            deviceCode = englishMatch.Groups["code"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"设备\s*(?<code>[A-Za-z0-9\-_]+).*(?:状态|现在.*状态)",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            deviceCode = chineseMatch.Groups["code"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchDeviceLogRange(string text, out string deviceCode, out string start, out string end)
    {
        deviceCode = string.Empty;
        start = string.Empty;
        end = string.Empty;

        var englishMatch = Regex.Match(
            text,
            @"logs?\s+for\s+device\s+(?<code>[A-Za-z0-9\-_]+)\s+from\s+(?<start>\S+)\s+to\s+(?<end>\S+)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            deviceCode = englishMatch.Groups["code"].Value;
            start = englishMatch.Groups["start"].Value;
            end = englishMatch.Groups["end"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"设备\s*(?<code>[A-Za-z0-9\-_]+).*(?<start>\d{4}-\d{2}-\d{2}T[^\s]+)\s*(?:到|至)\s*(?<end>\d{4}-\d{2}-\d{2}T[^\s]+).*日志",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            deviceCode = chineseMatch.Groups["code"].Value;
            start = chineseMatch.Groups["start"].Value;
            end = chineseMatch.Groups["end"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchDeviceLogByLevel(string text, out string deviceCode, out string level)
    {
        deviceCode = string.Empty;
        level = string.Empty;

        var englishMatch = Regex.Match(
            text,
            @"(?<level>error|warn|warning|info)\s+logs?\s+for\s+device\s+(?<code>[A-Za-z0-9\-_]+)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            deviceCode = englishMatch.Groups["code"].Value;
            level = NormalizeLevel(englishMatch.Groups["level"].Value);
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"设备\s*(?<code>[A-Za-z0-9\-_]+).*(?<level>错误|告警|警告|信息|error|warn|warning|info).*日志",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            deviceCode = chineseMatch.Groups["code"].Value;
            level = NormalizeLevel(chineseMatch.Groups["level"].Value);
            return true;
        }

        return false;
    }

    private static bool TryMatchLatestDeviceLog(string text, out string deviceCode)
    {
        deviceCode = string.Empty;

        var englishMatch = Regex.Match(
            text,
            @"latest\s+logs?\s+for\s+device\s+(?<code>[A-Za-z0-9\-_]+)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            deviceCode = englishMatch.Groups["code"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"设备\s*(?<code>[A-Za-z0-9\-_]+).*(最新日志|最近日志)",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            deviceCode = chineseMatch.Groups["code"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchRecipeList(string text, out string deviceCode)
    {
        deviceCode = string.Empty;

        if ((text.Contains("list recipes", StringComparison.OrdinalIgnoreCase)
             || (text.Contains("列出", StringComparison.OrdinalIgnoreCase) && text.Contains("配方", StringComparison.OrdinalIgnoreCase)))
            && TryExtractDeviceCode(text, out deviceCode))
        {
            return true;
        }

        if (text.Contains("list recipes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var englishMatch = Regex.Match(
            text,
            @"list\s+recipes(?:\s+for\s+device\s+(?<code>[A-Za-z0-9\-_]+))?",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            deviceCode = englishMatch.Groups["code"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"(?:列出|查看|查询).*(?:设备)\s*(?<code>[A-Za-z0-9\-_]+)?.*(?:配方)",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            deviceCode = chineseMatch.Groups["code"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchRecipeDetail(string text, out string recipeName)
    {
        recipeName = string.Empty;

        if ((text.Contains("recipe", StringComparison.OrdinalIgnoreCase)
             || text.Contains("配方", StringComparison.OrdinalIgnoreCase))
            && (text.Contains("detail", StringComparison.OrdinalIgnoreCase)
                || text.Contains("详情", StringComparison.OrdinalIgnoreCase))
            && TryExtractRecipeName(text, out recipeName))
        {
            return true;
        }

        var englishMatch = Regex.Match(
            text,
            @"(?:show|view)\s+recipe\s+(?<name>[A-Za-z0-9\-_]+)(?:\s+detail)?$",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success && !text.Contains("history", StringComparison.OrdinalIgnoreCase))
        {
            recipeName = englishMatch.Groups["name"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"(?:查看|查询).*(?:配方)\s*(?<name>[A-Za-z0-9\-_]+).*详情",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            recipeName = chineseMatch.Groups["name"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchRecipeVersionHistory(string text, out string recipeName)
    {
        recipeName = string.Empty;

        if ((text.Contains("recipe", StringComparison.OrdinalIgnoreCase)
             || text.Contains("配方", StringComparison.OrdinalIgnoreCase))
            && (text.Contains("version history", StringComparison.OrdinalIgnoreCase)
                || text.Contains("版本历史", StringComparison.OrdinalIgnoreCase)
                || text.Contains("历史", StringComparison.OrdinalIgnoreCase))
            && TryExtractRecipeName(text, out recipeName))
        {
            return true;
        }

        var englishMatch = Regex.Match(
            text,
            @"(?:show|view)\s+recipe\s+(?<name>[A-Za-z0-9\-_]+).*(?:version\s+history|history)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            recipeName = englishMatch.Groups["name"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"(?:查看|查询).*(?:配方)\s*(?<name>[A-Za-z0-9\-_]+).*(?:版本历史|历史版本)",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            recipeName = chineseMatch.Groups["name"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchCapacityRange(string text, out string deviceCode, out string start, out string end)
    {
        deviceCode = string.Empty;
        start = string.Empty;
        end = string.Empty;

        if ((text.Contains("capacity", StringComparison.OrdinalIgnoreCase)
             || text.Contains("产能", StringComparison.OrdinalIgnoreCase))
            && TryExtractDeviceCode(text, out deviceCode)
            && TryExtractIsoRange(text, out start, out end))
        {
            return true;
        }

        var englishMatch = Regex.Match(
            text,
            @"capacity\s+for\s+(?:device\s+)?(?<code>[A-Za-z0-9\-_]+)\s+from\s+(?<start>\S+)\s+to\s+(?<end>\S+)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            deviceCode = englishMatch.Groups["code"].Value;
            start = englishMatch.Groups["start"].Value;
            end = englishMatch.Groups["end"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"(?:查看|查询).*(?<code>[A-Za-z0-9\-_]+).*(?:产能).*(?<start>\d{4}-\d{2}-\d{2}T[^\s]+)\s*(?:到|至)\s*(?<end>\d{4}-\d{2}-\d{2}T[^\s]+)",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            deviceCode = chineseMatch.Groups["code"].Value;
            start = chineseMatch.Groups["start"].Value;
            end = chineseMatch.Groups["end"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchCapacityByDevice(string text, out string deviceCode)
    {
        deviceCode = string.Empty;

        if ((text.Contains("capacity", StringComparison.OrdinalIgnoreCase)
             || text.Contains("产能", StringComparison.OrdinalIgnoreCase))
            && !Regex.IsMatch(text, @"\d{4}-\d{2}-\d{2}T", RegexOptions.IgnoreCase)
            && TryExtractDeviceCode(text, out deviceCode))
        {
            return true;
        }

        var englishMatch = Regex.Match(
            text,
            @"capacity\s+for\s+(?:device\s+)?(?<code>[A-Za-z0-9\-_]+)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success && !text.Contains("from", StringComparison.OrdinalIgnoreCase))
        {
            deviceCode = englishMatch.Groups["code"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"(?:查看|查询).*(?:设备)\s*(?<code>[A-Za-z0-9\-_]+).*(?:产能)",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success && !Regex.IsMatch(text, @"\d{4}-\d{2}-\d{2}T", RegexOptions.IgnoreCase))
        {
            deviceCode = chineseMatch.Groups["code"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchCapacityByProcess(string text, out string processName)
    {
        processName = string.Empty;

        if ((text.Contains("capacity", StringComparison.OrdinalIgnoreCase)
             || text.Contains("产能", StringComparison.OrdinalIgnoreCase))
            && TryExtractProcessName(text, out processName))
        {
            return true;
        }

        var englishMatch = Regex.Match(
            text,
            @"capacity\s+(?:for|of)\s+process\s+(?<process>[A-Za-z0-9\-_]+)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            processName = englishMatch.Groups["process"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"(?:查看|查询).*(?<process>Cutting|Welding|Assembly|切割|焊接|装配).*(?:工序).*(?:产能)",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            processName = NormalizeProcessName(chineseMatch.Groups["process"].Value);
            return true;
        }

        return false;
    }

    private static bool TryMatchProductionLatest(string text, out string deviceCode)
    {
        deviceCode = string.Empty;

        if ((text.Contains("latest", StringComparison.OrdinalIgnoreCase)
             || text.Contains("最新", StringComparison.OrdinalIgnoreCase))
            && ContainsAny(text, "production", "record", "data", "生产记录", "生产数据", "过站数据")
            && TryExtractDeviceCode(text, out deviceCode))
        {
            return true;
        }

        var englishMatch = Regex.Match(
            text,
            @"latest\s+production\s+(?:records?|data)\s+for\s+(?:device\s+)?(?<code>[A-Za-z0-9\-_]+)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            deviceCode = englishMatch.Groups["code"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"(?:查看|查询).*(?:设备)\s*(?<code>[A-Za-z0-9\-_]+).*(?:最新).*(?:生产记录|生产数据|过站数据)",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            deviceCode = chineseMatch.Groups["code"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchProductionRange(string text, out string deviceCode, out string start, out string end)
    {
        deviceCode = string.Empty;
        start = string.Empty;
        end = string.Empty;

        if (ContainsAny(text, "production", "record", "data", "生产记录", "生产数据", "过站数据")
            && TryExtractDeviceCode(text, out deviceCode)
            && TryExtractIsoRange(text, out start, out end))
        {
            return true;
        }

        var englishMatch = Regex.Match(
            text,
            @"production\s+(?:records?|data)\s+for\s+(?:device\s+)?(?<code>[A-Za-z0-9\-_]+)\s+from\s+(?<start>\S+)\s+to\s+(?<end>\S+)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            deviceCode = englishMatch.Groups["code"].Value;
            start = englishMatch.Groups["start"].Value;
            end = englishMatch.Groups["end"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"(?:查看|查询).*(?<code>[A-Za-z0-9\-_]+).*(?:生产记录|生产数据|过站数据).*(?<start>\d{4}-\d{2}-\d{2}T[^\s]+)\s*(?:到|至)\s*(?<end>\d{4}-\d{2}-\d{2}T[^\s]+)",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success)
        {
            deviceCode = chineseMatch.Groups["code"].Value;
            start = chineseMatch.Groups["start"].Value;
            end = chineseMatch.Groups["end"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchProductionByDevice(string text, out string deviceCode)
    {
        deviceCode = string.Empty;

        if (ContainsAny(text, "production", "record", "data", "生产记录", "生产数据", "过站数据")
            && !Regex.IsMatch(text, @"\d{4}-\d{2}-\d{2}T", RegexOptions.IgnoreCase)
            && TryExtractDeviceCode(text, out deviceCode))
        {
            return true;
        }

        var englishMatch = Regex.Match(
            text,
            @"production\s+(?:records?|data)\s+for\s+(?:device\s+)?(?<code>[A-Za-z0-9\-_]+)",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success && !text.Contains("from", StringComparison.OrdinalIgnoreCase))
        {
            deviceCode = englishMatch.Groups["code"].Value;
            return true;
        }

        var chineseMatch = Regex.Match(
            text,
            @"(?:查看|查询).*(?:设备)\s*(?<code>[A-Za-z0-9\-_]+).*(?:生产记录|生产数据|过站数据)",
            RegexOptions.IgnoreCase);
        if (chineseMatch.Success && !Regex.IsMatch(text, @"\d{4}-\d{2}-\d{2}T", RegexOptions.IgnoreCase))
        {
            deviceCode = chineseMatch.Groups["code"].Value;
            return true;
        }

        return false;
    }

    private static string NormalizeProcessName(string rawProcessName)
    {
        return rawProcessName.Trim().ToLowerInvariant() switch
        {
            "切割" => "Cutting",
            "焊接" => "Welding",
            "装配" => "Assembly",
            _ => rawProcessName
        };
    }

    private static bool TryExtractDeviceCode(string text, out string deviceCode)
    {
        var match = Regex.Match(text, @"\bDEV-[A-Za-z0-9\-_]+\b", RegexOptions.IgnoreCase);
        deviceCode = match.Success ? match.Value : string.Empty;
        return match.Success;
    }

    private static bool TryExtractRecipeName(string text, out string recipeName)
    {
        var match = Regex.Match(text, @"\bRecipe-[A-Za-z0-9\-_]+\b", RegexOptions.IgnoreCase);
        recipeName = match.Success ? match.Value : string.Empty;
        return match.Success;
    }

    private static bool TryExtractIsoRange(string text, out string start, out string end)
    {
        var matches = Regex.Matches(text, @"\d{4}-\d{2}-\d{2}T[0-9:\.\+\-Z]+", RegexOptions.IgnoreCase);
        if (matches.Count >= 2)
        {
            start = matches[0].Value;
            end = matches[1].Value;
            return true;
        }

        start = string.Empty;
        end = string.Empty;
        return false;
    }

    private static bool TryExtractProcessName(string text, out string processName)
    {
        var englishMatch = Regex.Match(text, @"\b(Cutting|Welding|Assembly)\b", RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            processName = NormalizeProcessName(englishMatch.Value);
            return true;
        }

        if (text.Contains("切割", StringComparison.OrdinalIgnoreCase))
        {
            processName = "Cutting";
            return true;
        }

        if (text.Contains("焊接", StringComparison.OrdinalIgnoreCase))
        {
            processName = "Welding";
            return true;
        }

        if (text.Contains("装配", StringComparison.OrdinalIgnoreCase))
        {
            processName = "Assembly";
            return true;
        }

        processName = string.Empty;
        return false;
    }

    private static bool IsEmployeeAuthorizationQuestion(string text)
    {
        if (ContainsAny(text, "权限", "授权", "permission", "authorization")
            && ContainsAny(text, "设备", "机台", "参数", "配方", "device", "machine", "recipe", "parameter"))
        {
            return true;
        }

        return (text.Contains("device assignment", StringComparison.OrdinalIgnoreCase)
                || text.Contains("设备分配", StringComparison.OrdinalIgnoreCase))
               && ContainsAny(text, "operator", "change", "modify", "recipe", "settings", "参数", "配方", "修改");
    }

    private static bool IsDeviceRegistrationQuestion(string text)
    {
        return ContainsAny(text, "注册设备", "新设备", "device registration", "管理员")
               || (text.Contains("register", StringComparison.OrdinalIgnoreCase)
                   && text.Contains("device", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDeviceLifecycleQuestion(string text)
    {
        return ContainsAny(text, "删除设备", "设备删除", "改设备名", "寻址码", "hard delete", "rename device", "delete device", "device code")
               || (text.Contains("delete", StringComparison.OrdinalIgnoreCase)
                   && text.Contains("device", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBootstrapIdentityQuestion(string text)
    {
        return ContainsAny(text, "bootstrap", "clientcode", "deviceid", "寻址", "上传身份", "正式设备标识")
               || (text.Contains("upload", StringComparison.OrdinalIgnoreCase)
                   && text.Contains("device name", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRecipeVersioningQuestion(string text)
    {
        return ContainsAny(text, "配方版本", "版本历史", "v1.0", "recipe version", "version history", "覆盖旧配方")
               || (text.Contains("recipe", StringComparison.OrdinalIgnoreCase)
                   && text.Contains("overwrite", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string text, params string[] parts)
    {
        return parts.Any(part => text.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeLevel(string rawLevel)
    {
        return rawLevel.Trim().ToLowerInvariant() switch
        {
            "warning" => "Warn",
            "warn" => "Warn",
            "警告" => "Warn",
            "告警" => "Warn",
            "info" => "Info",
            "信息" => "Info",
            _ => "Error"
        };
    }

    private static object CreateSemanticIntent(
        string intent,
        string reasoning,
        object? filters = null,
        object? sort = null,
        object? timeRange = null,
        int? limit = null,
        string? queryText = null)
    {
        var queryPayload = new Dictionary<string, object?>();
        if (filters != null)
        {
            queryPayload["filters"] = filters;
        }

        if (sort != null)
        {
            queryPayload["sort"] = sort;
        }

        if (timeRange != null)
        {
            queryPayload["timeRange"] = timeRange;
        }

        if (limit.HasValue)
        {
            queryPayload["limit"] = limit.Value;
        }

        if (!string.IsNullOrWhiteSpace(queryText))
        {
            queryPayload["queryText"] = queryText;
        }

        return new
        {
            intent,
            confidence = 0.99,
            reasoning,
            query = JsonSerializer.Serialize(queryPayload, JsonOptions)
        };
    }

    private static bool TryResolvePolicyIntent(string latestUserText, out object policyIntent)
    {
        policyIntent = null!;

        if (IsEmployeeAuthorizationQuestion(latestUserText))
        {
            policyIntent = CreatePolicyIntent(
                "Policy.EmployeeAuthorization",
                "The user is asking about employee authorization and the double-check rule.",
                latestUserText);
            return true;
        }

        if (IsDeviceRegistrationQuestion(latestUserText))
        {
            policyIntent = CreatePolicyIntent(
                "Policy.DeviceRegistration",
                "The user is asking about admin-only device registration.",
                latestUserText);
            return true;
        }

        if (IsBootstrapIdentityQuestion(latestUserText))
        {
            policyIntent = CreatePolicyIntent(
                "Policy.BootstrapIdentity",
                "The user is asking about ClientCode, bootstrap, DeviceId, or upload identity.",
                latestUserText);
            return true;
        }

        if (IsDeviceLifecycleQuestion(latestUserText))
        {
            policyIntent = CreatePolicyIntent(
                "Policy.DeviceLifecycle",
                "The user is asking about device rename and deletion constraints.",
                latestUserText);
            return true;
        }

        if (latestUserText.Contains("配方修改", StringComparison.OrdinalIgnoreCase)
            || latestUserText.Contains("新建版本", StringComparison.OrdinalIgnoreCase)
            || latestUserText.Contains("覆盖", StringComparison.OrdinalIgnoreCase))
        {
            policyIntent = CreatePolicyIntent(
                "Policy.RecipeVersioning",
                "The user is asking about recipe version lifecycle.",
                latestUserText);
            return true;
        }

        if (IsRecipeVersioningQuestion(latestUserText))
        {
            policyIntent = CreatePolicyIntent(
                "Policy.RecipeVersioning",
                "The user is asking about recipe version lifecycle.",
                latestUserText);
            return true;
        }

        return false;
    }

    private static object CreatePolicyIntent(string intent, string reasoning, string latestUserText)
    {
        return new
        {
            intent,
            confidence = 0.99,
            reasoning,
            query = latestUserText
        };
    }

    private static bool TryResolveBusinessPolicyAnswer(string latestUserText, out string policyAnswer)
    {
        policyAnswer = string.Empty;
        const string startMarker = "<business_policy_context>";
        const string endMarker = "</business_policy_context>";

        var startIndex = latestUserText.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return false;
        }

        startIndex += startMarker.Length;
        var endIndex = latestUserText.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return false;
        }

        var rawContext = latestUserText[startIndex..endIndex];
        var sanitized = Regex.Replace(rawContext, @"<policy[^>]*>", string.Empty, RegexOptions.IgnoreCase)
            .Replace("</policy>", string.Empty, StringComparison.OrdinalIgnoreCase);
        sanitized = Regex.Replace(sanitized, @"[ \t]+\r?\n", Environment.NewLine);

        var lines = sanitized
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length == 0)
        {
            return false;
        }

        policyAnswer = string.Join(Environment.NewLine, lines);
        return true;
    }

    private static bool TryResolveSemanticAnswer(string latestUserText, out string semanticAnswer)
    {
        semanticAnswer = string.Empty;
        const string marker = "<data_analysis_context>";
        var markerIndex = latestUserText.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var jsonStart = latestUserText.IndexOf('{', markerIndex);
        if (jsonStart < 0 || !TryExtractJsonObject(latestUserText[jsonStart..], out var jsonText))
        {
            return false;
        }

        using var document = JsonDocument.Parse(jsonText);
        if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var queryScope = GetSemanticScope(document.RootElement);
        if (document.RootElement.TryGetProperty("semantic_summary", out var summaryElement)
            && summaryElement.ValueKind == JsonValueKind.Object)
        {
            semanticAnswer = BuildSemanticAnswerFromSummary(summaryElement, queryScope);
            if (!string.IsNullOrWhiteSpace(semanticAnswer))
            {
                return true;
            }
        }

        var rowCount = dataElement.GetArrayLength();
        if (rowCount == 0)
        {
            var emptyLines = new List<string>
            {
                "结论：未查询到符合条件的设备或日志记录。"
            };

            if (!string.IsNullOrWhiteSpace(queryScope))
            {
                emptyLines.Add($"查询条件：{queryScope}");
            }

            semanticAnswer = string.Join(Environment.NewLine, emptyLines);
            return true;
        }

        var firstRow = dataElement[0];
        if (firstRow.ValueKind != JsonValueKind.Object)
        {
            semanticAnswer = $"结论：共返回 {rowCount} 条记录。";
            return true;
        }

        var previewRows = dataElement.EnumerateArray()
            .Take(3)
            .Select(DescribeSemanticRowForPhase3)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var lines = new List<string>
        {
            $"结论：{BuildSemanticConclusion(firstRow, rowCount)}",
            "关键记录："
        };

        for (var index = 0; index < previewRows.Length; index++)
        {
            lines.Add($"{index + 1}. {previewRows[index]}");
        }

        if (!string.IsNullOrWhiteSpace(queryScope))
        {
            lines.Add($"查询条件：{queryScope}");
        }

        if (rowCount > previewRows.Length)
        {
            lines.Add($"其余记录：还有 {rowCount - previewRows.Length} 条未展开。");
        }

        semanticAnswer = string.Join(Environment.NewLine, lines);
        return true;
    }

    private static string GetSemanticScope(JsonElement root)
    {
        if (!root.TryGetProperty("analysis", out var analysisElement)
            || !analysisElement.TryGetProperty("description", out var descriptionElement))
        {
            return string.Empty;
        }

        var description = descriptionElement.GetString() ?? string.Empty;
        const string marker = "查询范围：";
        var markerIndex = description.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return description.Trim().TrimEnd('。');
        }

        return description[(markerIndex + marker.Length)..].Trim().TrimEnd('。');
    }

    private static string BuildSemanticAnswerFromSummary(JsonElement summaryElement, string fallbackScope)
    {
        var conclusion = GetSummaryString(summaryElement, "conclusion");
        if (string.IsNullOrWhiteSpace(conclusion))
        {
            return string.Empty;
        }

        var lines = new List<string>
        {
            $"结论：{conclusion}"
        };

        var metrics = GetSummaryMetrics(summaryElement);
        if (metrics.Count > 0)
        {
            lines.Add("关键指标：");
            for (var index = 0; index < metrics.Count; index++)
            {
                lines.Add($"{index + 1}. {metrics[index]}");
            }
        }

        var highlights = GetSummaryHighlights(summaryElement);
        if (highlights.Count > 0)
        {
            lines.Add("关键记录：");
            for (var index = 0; index < highlights.Count; index++)
            {
                lines.Add($"{index + 1}. {highlights[index]}");
            }
        }

        var scope = GetSummaryString(summaryElement, "scope");
        var effectiveScope = string.IsNullOrWhiteSpace(scope) ? fallbackScope : scope;
        if (!string.IsNullOrWhiteSpace(effectiveScope))
        {
            lines.Add($"查询范围：{effectiveScope}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string GetSummaryString(JsonElement summaryElement, string propertyName)
    {
        return TryGetPropertyCaseInsensitive(summaryElement, propertyName, out var propertyElement)
            ? propertyElement.ToString()
            : string.Empty;
    }

    private static List<string> GetSummaryMetrics(JsonElement summaryElement)
    {
        var metrics = new List<string>();
        if (!TryGetPropertyCaseInsensitive(summaryElement, "metrics", out var metricsElement)
            || metricsElement.ValueKind != JsonValueKind.Array)
        {
            return metrics;
        }

        foreach (var metric in metricsElement.EnumerateArray())
        {
            var label = GetSummaryString(metric, "label");
            var value = GetSummaryString(metric, "value");
            if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value))
            {
                metrics.Add($"{label}：{value}");
            }
        }

        return metrics;
    }

    private static List<string> GetSummaryHighlights(JsonElement summaryElement)
    {
        var highlights = new List<string>();
        if (!TryGetPropertyCaseInsensitive(summaryElement, "highlights", out var highlightsElement)
            || highlightsElement.ValueKind != JsonValueKind.Array)
        {
            return highlights;
        }

        foreach (var highlight in highlightsElement.EnumerateArray())
        {
            var text = highlight.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                highlights.Add(text);
            }
        }

        return highlights;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement valueElement)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out valueElement))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    valueElement = property.Value;
                    return true;
                }
            }
        }

        valueElement = default;
        return false;
    }

    private static string BuildSemanticConclusion(JsonElement firstRow, int rowCount)
    {
        if (HasProperty(firstRow, "message"))
        {
            return $"共找到 {rowCount} 条设备日志记录。";
        }

        if (HasProperty(firstRow, "recipeName"))
        {
            var recipeName = GetStringValue(firstRow, "recipeName");
            return rowCount == 1
                ? $"已找到配方 {recipeName} 的关键信息。"
                : $"共找到 {rowCount} 条配方记录。";
        }

        if (HasProperty(firstRow, "outputQty"))
        {
            return $"共找到 {rowCount} 条产能记录。";
        }

        if (HasProperty(firstRow, "barcode"))
        {
            return $"共找到 {rowCount} 条生产记录。";
        }

        var deviceCode = GetStringValue(firstRow, "deviceCode");
        return rowCount == 1 && !string.IsNullOrWhiteSpace(deviceCode)
            ? $"已找到设备 {deviceCode} 的关键信息。"
            : $"共找到 {rowCount} 台设备。";
    }

    private static string DescribeSemanticRow(JsonElement row)
    {
        if (HasProperty(row, "message"))
        {
            return $"设备 {GetStringValue(row, "deviceCode")}，级别 {GetStringValue(row, "level")}，内容 {GetStringValue(row, "message")}，时间 {GetStringValue(row, "occurredAt")}";
        }

        return $"设备 {GetStringValue(row, "deviceCode")} / {GetStringValue(row, "deviceName")}，状态 {GetStringValue(row, "status")}，产线 {GetStringValue(row, "lineName")}，更新时间 {GetStringValue(row, "updatedAt")}";
    }

    private static string DescribeSemanticRowForPhase3(JsonElement row)
    {
        if (row.TryGetProperty("message", out _))
        {
            return $"设备 {GetStringValue(row, "deviceCode")}，级别 {GetStringValue(row, "level")}，内容 {GetStringValue(row, "message")}，时间 {GetStringValue(row, "occurredAt")}";
        }

        if (HasProperty(row, "recipeName"))
        {
            return $"配方 {GetStringValue(row, "recipeName")}，版本 {GetStringValue(row, "version")}，设备 {GetStringValue(row, "deviceCode")}，工序 {GetStringValue(row, "processName")}，生效 {GetStringValue(row, "isActive")}，更新时间 {GetStringValue(row, "updatedAt")}";
        }

        if (HasProperty(row, "outputQty"))
        {
            return $"设备 {GetStringValue(row, "deviceCode")}，工序 {GetStringValue(row, "processName")}，班次日期 {GetStringValue(row, "shiftDate")}，产出 {GetStringValue(row, "outputQty")}，合格 {GetStringValue(row, "qualifiedQty")}，时间 {GetStringValue(row, "occurredAt")}";
        }

        if (HasProperty(row, "barcode"))
        {
            return $"设备 {GetStringValue(row, "deviceCode")}，工序 {GetStringValue(row, "processName")}，条码 {GetStringValue(row, "barcode")}，工位 {GetStringValue(row, "stationName")}，结果 {GetStringValue(row, "result")}，时间 {GetStringValue(row, "occurredAt")}";
        }

        return $"设备 {GetStringValue(row, "deviceCode")} / {GetStringValue(row, "deviceName")}，状态 {GetStringValue(row, "status")}，产线 {GetStringValue(row, "lineName")}，更新时间 {GetStringValue(row, "updatedAt")}";
    }

    private static bool HasProperty(JsonElement row, string propertyName)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (row.TryGetProperty(propertyName, out _))
        {
            return true;
        }

        foreach (var candidate in row.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetStringValue(JsonElement row, string propertyName)
    {
        if (row.TryGetProperty(propertyName, out var property))
        {
            return property.ToString();
        }

        foreach (var candidate in row.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Value.ToString();
            }
        }

        return "-";
    }

    private static bool TryExtractJsonObject(string text, out string jsonText)
    {
        jsonText = string.Empty;
        var bytes = Encoding.UTF8.GetBytes(text.TrimStart());
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        jsonText = document.RootElement.GetRawText();
        return true;
    }

    private static List<string> ExtractMessageTexts(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messagesElement) || messagesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();

        foreach (var message in messagesElement.EnumerateArray())
        {
            if (!message.TryGetProperty("content", out var contentElement))
            {
                continue;
            }

            switch (contentElement.ValueKind)
            {
                case JsonValueKind.String:
                    result.Add(contentElement.GetString() ?? string.Empty);
                    break;
                case JsonValueKind.Array:
                    foreach (var part in contentElement.EnumerateArray())
                    {
                        if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var textElement))
                        {
                            result.Add(textElement.GetString() ?? string.Empty);
                        }
                    }
                    break;
            }
        }

        return result;
    }

    private static string ExtractLatestUserText(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messagesElement) || messagesElement.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var messages = messagesElement.EnumerateArray().ToArray();
        Array.Reverse(messages);

        foreach (var message in messages)
        {
            if (!message.TryGetProperty("role", out var roleElement) || roleElement.GetString() != "user")
            {
                continue;
            }

            if (message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
            {
                return contentElement.GetString() ?? string.Empty;
            }

            if (message.TryGetProperty("content", out contentElement) && contentElement.ValueKind == JsonValueKind.Array)
            {
                var parts = contentElement.EnumerateArray()
                    .Where(part => part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out _))
                    .Select(part => part.GetProperty("text").GetString() ?? string.Empty)
                    .ToArray();

                return string.Join(" ", parts);
            }
        }

        return string.Empty;
    }

    private static bool HasToolResultMessage(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messagesElement) || messagesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return messagesElement.EnumerateArray().Any(message =>
            message.TryGetProperty("role", out var roleElement)
            && roleElement.GetString() == "tool");
    }

    private static string? ExtractDiagnosticToolName(JsonElement root)
    {
        if (!root.TryGetProperty("tools", out var toolsElement) || toolsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var tool in toolsElement.EnumerateArray())
        {
            if (tool.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!tool.TryGetProperty("function", out var functionElement) || functionElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!functionElement.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var name = nameElement.GetString();
            if (name != null && name.Contains("GenerateDiagnosticChecklist", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    private static List<string> ExtractEmbeddingInputs(JsonElement inputElement)
    {
        return inputElement.ValueKind switch
        {
            JsonValueKind.String => [inputElement.GetString() ?? string.Empty],
            JsonValueKind.Array => inputElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList(),
            _ => []
        };
    }

    private static float[] CreateEmbedding(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [0, 0, 0, 0];
        }

        var normalized = value.ToLowerInvariant();
        var vowels = normalized.Count("aeiou中文测试标准".Contains);
        var consonants = normalized.Count(char.IsLetterOrDigit) - vowels;
        var length = normalized.Length;
        var checksum = normalized.Sum(ch => ch);

        return
        [
            length,
            vowels,
            consonants,
            checksum % 97
        ];
    }

    private static int GetRandomUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
