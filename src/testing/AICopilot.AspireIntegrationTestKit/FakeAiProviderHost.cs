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

namespace AICopilot.AspireIntegrationTestKit;

public sealed class FakeAiProviderHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid CloudDeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CloudProcessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CloudLogId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string CloudAiReadToken = "test-cloud-ai-read-token";

    private WebApplication? _app;
    private int toolResultRequestCount;

    public Uri BaseUri { get; private set; } = null!;

    public int ToolResultRequestCount => Volatile.Read(ref toolResultRequestCount);

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
        app.MapGet("/api/v1/ai/read/devices", HandleCloudDevices);
        app.MapGet("/api/v1/ai/read/device-logs", HandleCloudDeviceLogs);

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

    private async Task HandleChatCompletionsAsync(HttpContext context)
    {
        using var document = await JsonDocument.ParseAsync(context.Request.Body);
        var root = document.RootElement;
        var stream = root.TryGetProperty("stream", out var streamElement) && streamElement.GetBoolean();

        var messageTexts = ExtractMessageTexts(root);
        var latestUserText = ExtractLatestUserText(root);
        var hasToolResult = HasToolResultMessage(root);
        if (stream)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";

            if (hasToolResult)
            {
                Interlocked.Increment(ref toolResultRequestCount);
                var toolResultText = TryExtractBusinessQueryConfirmation(
                    messageTexts,
                    out var confirmation)
                    ? $"请回复“{confirmation}”以确认本次查询范围。"
                    : "已批准并执行工具。";
                await WriteTextStreamAsync(context, toolResultText);
                return;
            }

            if (ShouldBlockControlRequest(latestUserText))
            {
                await WriteTextStreamAsync(context, BuildControlBoundaryResponse());
                return;
            }

            if (ShouldTriggerMultipleDiagnosticApprovals(latestUserText))
            {
                var toolName = ExtractDiagnosticToolName(root) ?? "GenerateDiagnosticChecklist";
                await WriteToolCallsStreamAsync(
                    context,
                    [
                        new FakeToolCall(toolName, new { deviceCode = "DEV-001" }),
                        new FakeToolCall(toolName, new { deviceCode = "DEV-002" })
                    ]);
                return;
            }

            if (ShouldTriggerDiagnosticApproval(latestUserText))
            {
                var toolName = ExtractDiagnosticToolName(root) ?? "GenerateDiagnosticChecklist";
                await WriteToolCallStreamAsync(context, toolName);
                return;
            }

            if (ShouldTriggerBusinessQuery(latestUserText) &&
                ExtractToolName(root, "BusinessQuery") is { } businessQueryToolName)
            {
                await WriteToolCallStreamAsync(
                    context,
                    businessQueryToolName,
                    new
                    {
                        semanticIntent = "Analysis.DeviceLog.Latest",
                        question = latestUserText.StartsWith(
                            "确认查询 ",
                            StringComparison.Ordinal)
                            ? latestUserText
                            : """{"queryText":"查看设备 DEV-001 最新日志","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"sort":{"field":"occurredAt","direction":"desc"},"limit":10}"""
                    });
                return;
            }

            await WriteTextStreamAsync(context, ResolvePlainTextResponse(messageTexts, latestUserText));
            return;
        }

        var content = ResolvePlainTextResponse(messageTexts, latestUserText);

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

    private static IResult HandleCloudDevices(HttpContext context)
    {
        if (!HasCloudAiReadAuthorization(context))
        {
            return Results.Unauthorized();
        }

        var requestedCode = context.Request.Query["deviceCode"].ToString();
        var items = string.IsNullOrWhiteSpace(requestedCode) ||
                    string.Equals(requestedCode, "DEV-001", StringComparison.OrdinalIgnoreCase)
            ? new object[]
            {
                new
                {
                    id = CloudDeviceId,
                    deviceCode = "DEV-001",
                    deviceName = "Cutter A",
                    processId = CloudProcessId
                }
            }
            : [];
        return Results.Json(CreateCloudAiReadEnvelope(
            items,
            "devices",
            $"deviceCode={requestedCode}"));
    }

    private static IResult HandleCloudDeviceLogs(HttpContext context)
    {
        if (!HasCloudAiReadAuthorization(context))
        {
            return Results.Unauthorized();
        }

        var requestedDeviceId = context.Request.Query["deviceId"].ToString();
        var items = string.Equals(
                requestedDeviceId,
                CloudDeviceId.ToString("D"),
                StringComparison.OrdinalIgnoreCase)
            ? new object[]
            {
                new
                {
                    id = CloudLogId,
                    deviceId = CloudDeviceId,
                    deviceName = "Cutter A",
                    level = "WARN",
                    message = "Temperature high",
                    logTime = "2026-07-31T08:00:00Z",
                    receivedAt = "2026-07-31T08:00:01Z"
                }
            }
            : [];
        return Results.Json(CreateCloudAiReadEnvelope(
            items,
            "device_logs",
            $"deviceId={requestedDeviceId}"));
    }

    private static bool HasCloudAiReadAuthorization(HttpContext context)
    {
        return string.Equals(
            context.Request.Headers.Authorization.ToString(),
            $"Bearer {CloudAiReadToken}",
            StringComparison.Ordinal);
    }

    private static object CreateCloudAiReadEnvelope(
        IReadOnlyCollection<object> items,
        string source,
        string queryScope)
    {
        return new
        {
            items,
            asOfUtc = "2026-07-31T08:00:02Z",
            source,
            queryScope,
            rowCount = items.Count,
            truncated = false,
            nextCursor = (string?)null
        };
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

    private static async Task WriteToolCallStreamAsync(
        HttpContext context,
        string toolName,
        object? arguments = null)
    {
        await WriteToolCallsStreamAsync(
            context,
            [new FakeToolCall(toolName, arguments)]);
    }

    private static async Task WriteToolCallsStreamAsync(
        HttpContext context,
        IReadOnlyCollection<FakeToolCall> calls)
    {
        var toolCalls = calls
            .Select((call, index) => new
            {
                index,
                id = $"call_{Guid.NewGuid():N}",
                type = "function",
                function = new
                {
                    name = call.ToolName,
                    arguments = JsonSerializer.Serialize(call.Arguments ?? new { })
                }
            })
            .ToArray();

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
                        tool_calls = toolCalls
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

    private static bool ShouldTriggerMultipleDiagnosticApprovals(string latestUserText)
    {
        return latestUserText.Contains(
            "force two diagnostic approvals",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldTriggerBusinessQuery(string latestUserText)
    {
        return latestUserText.Contains("inline business widget", StringComparison.OrdinalIgnoreCase)
               || Regex.IsMatch(
                   latestUserText,
                   @"^确认查询 [0-9a-f]{32}$",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed record FakeToolCall(string ToolName, object? Arguments);

    private static bool TryExtractBusinessQueryConfirmation(
        IEnumerable<string> messageTexts,
        out string confirmation)
    {
        foreach (var messageText in messageTexts.Reverse())
        {
            var match = Regex.Match(
                messageText,
                @"确认查询 [0-9a-f]{32}",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                confirmation = match.Value;
                return true;
            }
        }

        confirmation = string.Empty;
        return false;
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
            _ when messageTexts.Any(text =>
                text.Contains("正式 Cloud AiRead 数据源不可用", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("设备最后上报运行状态的正式 Cloud AiRead 数据源不可用", StringComparison.OrdinalIgnoreCase))
                => "结论：正式 Cloud AiRead 数据源不可用；本次查询未回退 Direct DB、Text-to-SQL 或 Simulation。",
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
        var queryScope = GetSemanticScope(document.RootElement);
        if (TryBuildDeviceLogDisplayAnswer(document.RootElement, queryScope, out semanticAnswer))
        {
            return true;
        }

        if (document.RootElement.TryGetProperty("semantic_summary", out var summaryElement)
            && summaryElement.ValueKind == JsonValueKind.Object)
        {
            semanticAnswer = BuildSemanticAnswerFromSummary(summaryElement, queryScope);
            if (!string.IsNullOrWhiteSpace(semanticAnswer))
            {
                return true;
            }
        }

        if ((!document.RootElement.TryGetProperty("business_data_preview", out var dataElement)
                || dataElement.ValueKind != JsonValueKind.Array)
            && (!document.RootElement.TryGetProperty("data", out dataElement)
                || dataElement.ValueKind != JsonValueKind.Array))
        {
            return false;
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

    private static bool TryBuildDeviceLogDisplayAnswer(
        JsonElement root,
        string fallbackScope,
        out string semanticAnswer)
    {
        semanticAnswer = string.Empty;
        if (!TryGetPropertyCaseInsensitive(root, "display_blocks", out var blocksElement)
            || blocksElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var metricBlock = FindDisplayBlock(blocksElement, "device_log_metrics");
        var evidenceBlock = FindDisplayBlock(blocksElement, "device_log_evidence_table");
        if (metricBlock.ValueKind != JsonValueKind.Object || evidenceBlock.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var sourceMode = GetSummaryString(root, "source_mode");
        var conclusion = GetSemanticSummaryConclusion(root);
        var rows = GetEvidenceRows(evidenceBlock).Take(3).ToArray();
        var lines = new List<string>
        {
            $"结论：{BuildDeviceLogConclusionPrefix(sourceMode)}{conclusion}"
        };

        var metrics = GetDisplayMetrics(metricBlock);
        if (metrics.Count > 0)
        {
            lines.Add("关键指标：");
            for (var index = 0; index < metrics.Count; index++)
            {
                lines.Add($"{index + 1}. {metrics[index]}");
            }
        }

        if (rows.Length > 0)
        {
            lines.Add("关键记录：");
            for (var index = 0; index < rows.Length; index++)
            {
                lines.Add($"{index + 1}. {DescribeDeviceLogEvidenceRow(rows[index])}");
            }
        }

        lines.Add("可能原因：");
        lines.Add($"1. AI 推断分析：{BuildDeviceLogPossibleReason(blocksElement)}");
        lines.Add("建议动作：");
        lines.Add("1. 由现场人员按证据表时间点核对设备、工序、报警和传感器/驱动/通信状态。");
        lines.Add("2. 优先复核同一时间窗口内重复出现的 ERROR/WARN 级别日志，再结合设备维护记录确认根因。");
        lines.Add("不能直接执行的动作：");
        lines.Add("1. AICopilot 不能直接重启设备、修改参数、下发配方、补录/删除日志或写入 Cloud 业务数据。");

        var scope = GetSummaryString(root, "query_scope");
        if (string.IsNullOrWhiteSpace(scope))
        {
            scope = fallbackScope;
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            lines.Add($"查询范围：{scope}");
        }

        semanticAnswer = string.Join(Environment.NewLine, lines);
        return true;
    }

    private static string GetSemanticScope(JsonElement root)
    {
        if (TryGetPropertyCaseInsensitive(root, "query_scope", out var queryScopeElement))
        {
            return queryScopeElement.ToString();
        }

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

    private static JsonElement FindDisplayBlock(JsonElement blocksElement, string blockId)
    {
        foreach (var block in blocksElement.EnumerateArray())
        {
            if (string.Equals(GetSummaryString(block, "id"), blockId, StringComparison.OrdinalIgnoreCase))
            {
                return block;
            }
        }

        return default;
    }

    private static string GetSemanticSummaryConclusion(JsonElement root)
    {
        if (TryGetPropertyCaseInsensitive(root, "semantic_summary", out var summaryElement)
            && summaryElement.ValueKind == JsonValueKind.Object)
        {
            var conclusion = GetSummaryString(summaryElement, "conclusion");
            if (!string.IsNullOrWhiteSpace(conclusion))
            {
                return conclusion;
            }
        }

        if (TryGetPropertyCaseInsensitive(root, "query_execution", out var queryExecution)
            && queryExecution.ValueKind == JsonValueKind.Object
            && TryGetPropertyCaseInsensitive(queryExecution, "returned_row_count", out var rowCountElement))
        {
            return $"本轮返回 {rowCountElement} 条设备日志。";
        }

        return "已基于本轮只读查询返回设备日志分析结果。";
    }

    private static string BuildDeviceLogConclusionPrefix(string sourceMode)
    {
        if (sourceMode.Contains("Cloud 已有", StringComparison.OrdinalIgnoreCase))
        {
            return "Cloud 已有数据，";
        }

        if (sourceMode.Contains("DataAnalysis", StringComparison.OrdinalIgnoreCase))
        {
            return "基于 DataAnalysis 只读查询，";
        }

        return "基于本轮只读查询，";
    }

    private static List<string> GetDisplayMetrics(JsonElement metricBlock)
    {
        var metrics = new List<string>();
        if (!TryGetPropertyCaseInsensitive(metricBlock, "metrics", out var metricsElement)
            || metricsElement.ValueKind != JsonValueKind.Array)
        {
            return metrics;
        }

        foreach (var metric in metricsElement.EnumerateArray())
        {
            var label = GetSummaryString(metric, "label");
            var value = GetSummaryString(metric, "value");
            var unit = GetSummaryString(metric, "unit");
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            metrics.Add(string.IsNullOrWhiteSpace(unit)
                ? $"{label}：{value}"
                : $"{label}：{value} {unit}");
        }

        return metrics;
    }

    private static IEnumerable<JsonElement> GetEvidenceRows(JsonElement evidenceBlock)
    {
        if (!TryGetPropertyCaseInsensitive(evidenceBlock, "rows", out var rowsElement)
            || rowsElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var row in rowsElement.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object)
            {
                yield return row;
            }
        }
    }

    private static string DescribeDeviceLogEvidenceRow(JsonElement row)
    {
        return $"设备 {GetStringValue(row, "deviceCode")}（{GetStringValue(row, "deviceName")}），工序 {GetStringValue(row, "processName")}，级别 {GetStringValue(row, "level")}，内容 {GetStringValue(row, "message")}，时间 {GetStringValue(row, "occurredAt")}";
    }

    private static string BuildDeviceLogPossibleReason(JsonElement blocksElement)
    {
        var issueCategory = TryGetTopChartItem(blocksElement, "issue_category_ranking", "category", out var category, out var categoryCount)
            ? $"{category}（{categoryCount} 条）"
            : string.Empty;
        var levelSummary = TryGetTopChartItem(blocksElement, "level_distribution", "level", out var level, out var levelCount)
            ? $"{level} 级别占比最高（{levelCount} 条）"
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(issueCategory) && !issueCategory.StartsWith("其他", StringComparison.Ordinal))
        {
            return $"日志关键词集中在 {issueCategory}，可优先排查该类别相关的设备部件、传感器、驱动或通信链路；{levelSummary}。";
        }

        if (!string.IsNullOrWhiteSpace(levelSummary))
        {
            return $"{levelSummary}，说明本轮范围内需要优先核对异常级别日志对应的设备状态和现场事件。";
        }

        return "当前展示块未形成明确分类集中趋势，需要结合证据表逐条核对现场状态。";
    }

    private static bool TryGetTopChartItem(
        JsonElement blocksElement,
        string blockId,
        string labelField,
        out string label,
        out string count)
    {
        label = string.Empty;
        count = string.Empty;
        var block = FindDisplayBlock(blocksElement, blockId);
        if (block.ValueKind != JsonValueKind.Object
            || !TryGetPropertyCaseInsensitive(block, "chart", out var chartElement)
            || chartElement.ValueKind != JsonValueKind.Object
            || !TryGetPropertyCaseInsensitive(chartElement, "dataset", out var datasetElement)
            || datasetElement.ValueKind != JsonValueKind.Object
            || !TryGetPropertyCaseInsensitive(datasetElement, "source", out var sourceElement)
            || sourceElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var firstRow = sourceElement.EnumerateArray().FirstOrDefault(row => row.ValueKind == JsonValueKind.Object);
        if (firstRow.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        label = GetStringValue(firstRow, labelField);
        count = GetStringValue(firstRow, "count");
        return !string.IsNullOrWhiteSpace(label) && label != "-";
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
            if (string.IsNullOrWhiteSpace(label))
            {
                label = GetSummaryString(metric, "name");
            }

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

        if (HasProperty(row, "runtimeStatus"))
        {
            return $"设备 {GetStringValue(row, "clientCode")} / {GetStringValue(row, "deviceName")}，最后上报运行状态 {GetStringValue(row, "runtimeStatus")}，最后心跳 {GetStringValue(row, "lastRuntimeHeartbeatAtUtc")}；不据此推断在线或离线";
        }

        return $"设备 {GetStringValue(row, "deviceCode")} / {GetStringValue(row, "deviceName")}，工序标识 {GetStringValue(row, "processId")}";
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

        if (HasProperty(row, "runtimeStatus"))
        {
            return $"设备 {GetStringValue(row, "clientCode")} / {GetStringValue(row, "deviceName")}，最后上报运行状态 {GetStringValue(row, "runtimeStatus")}，最后心跳 {GetStringValue(row, "lastRuntimeHeartbeatAtUtc")}；不据此推断在线或离线";
        }

        return $"设备 {GetStringValue(row, "deviceCode")} / {GetStringValue(row, "deviceName")}，工序标识 {GetStringValue(row, "processId")}";
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
        string? syntheticFallback = null;

        foreach (var message in messages)
        {
            if (!message.TryGetProperty("role", out var roleElement) || roleElement.GetString() != "user")
            {
                continue;
            }

            if (message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
            {
                var text = contentElement.GetString() ?? string.Empty;
                syntheticFallback ??= text;
                if (!IsHarnessContextMessage(text))
                {
                    return text;
                }

                continue;
            }

            if (message.TryGetProperty("content", out contentElement) && contentElement.ValueKind == JsonValueKind.Array)
            {
                var parts = contentElement.EnumerateArray()
                    .Where(part => part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out _))
                    .Select(part => part.GetProperty("text").GetString() ?? string.Empty)
                    .ToArray();

                var text = string.Join(" ", parts);
                syntheticFallback ??= text;
                if (!IsHarnessContextMessage(text))
                {
                    return text;
                }
            }
        }

        return syntheticFallback ?? string.Empty;
    }

    private static bool IsHarnessContextMessage(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("[Mode changed:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("### Current todo list", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasToolResultMessage(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messagesElement) || messagesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var messages = messagesElement.EnumerateArray().ToArray();
        for (var index = messages.Length - 1; index >= 0; index--)
        {
            var message = messages[index];
            if (!message.TryGetProperty("role", out var roleElement))
            {
                continue;
            }

            var role = roleElement.GetString();
            if (role == "tool")
            {
                return true;
            }

            if (role != "user" ||
                !message.TryGetProperty("content", out var contentElement))
            {
                continue;
            }

            var text = contentElement.ValueKind switch
            {
                JsonValueKind.String => contentElement.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Join(
                    " ",
                    contentElement.EnumerateArray()
                        .Where(part => part.ValueKind == JsonValueKind.Object &&
                                       part.TryGetProperty("text", out _))
                        .Select(part => part.GetProperty("text").GetString() ?? string.Empty)),
                _ => string.Empty
            };
            if (!IsHarnessContextMessage(text))
            {
                return false;
            }
        }

        return false;
    }

    private static string? ExtractDiagnosticToolName(JsonElement root)
    {
        return ExtractToolName(root, "GenerateDiagnosticChecklist");
    }

    private static string? ExtractToolName(JsonElement root, string expectedName)
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
            if (name != null && name.Contains(expectedName, StringComparison.OrdinalIgnoreCase))
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
