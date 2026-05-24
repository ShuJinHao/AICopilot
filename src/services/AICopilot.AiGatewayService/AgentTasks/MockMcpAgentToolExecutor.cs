using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class MockMcpAgentToolExecutor : IAgentToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public bool CanExecute(ToolRegistration tool, AgentStep step)
    {
        return tool.ProviderType == ToolProviderType.MockMcp &&
               tool.TargetType == ToolRegistrationTargetType.AgentRuntime;
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var input = ParseInput(context.Step.InputJson);
        var behavior = ReadString(input, "mockBehavior");
        if (string.Equals(behavior, "slow", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), context.CancellationToken);
        }
        else if (string.Equals(behavior, "timeout", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
        }
        else if (string.Equals(behavior, "500", StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentToolExecutionException(
                "tool_execution_failed",
                $"Mock MCP tool '{context.ToolRegistration.ToolCode}' returned simulated 500.");
        }
        else if (string.Equals(behavior, "schema-invalid", StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentPlanSchemaInvalid,
                $"Mock MCP tool '{context.ToolRegistration.ToolCode}' returned simulated schema-invalid.");
        }

        var payload = context.ToolRegistration.ToolCode switch
        {
            "mock_mcp_health_check" => BuildHealthPayload(),
            "mock_mcp_kpi_formula_lookup" => BuildKpiFormulaPayload(ReadString(input, "domain")),
            "mock_mcp_artifact_quality_check" => BuildArtifactQualityPayload(input),
            "mock_mcp_external_ticket_preview" => BuildTicketPreviewPayload(input),
            _ => throw new AgentToolExecutionException(
                AppProblemCodes.ToolExecutionNotFound,
                $"Mock MCP tool '{context.ToolRegistration.ToolCode}' is not registered in the in-process provider.")
        };

        stopwatch.Stop();
        var resultHash = ComputeHash(payload);
        return AgentToolExecutionResult.From(new
        {
            isMock = true,
            providerKind = ToolProviderType.MockMcp.ToString(),
            toolCode = context.ToolRegistration.ToolCode,
            toolRunId = Guid.NewGuid(),
            toolCatalogVersion = context.ToolRegistration.CatalogVersion,
            schemaVersion = context.ToolRegistration.SchemaVersion,
            status = "Succeeded",
            durationMs = stopwatch.ElapsedMilliseconds,
            resultHash,
            payload
        });
    }

    private static IReadOnlyDictionary<string, object?> ParseInput(string? inputJson)
    {
        var validation = ToolInputSchemaValidator.ValidateAndParse(inputJson, "{}");
        if (!validation.IsValid)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentPlanSchemaInvalid,
                validation.Error ?? "Mock MCP input is invalid.");
        }

        return validation.Arguments;
    }

    private static object BuildHealthPayload()
    {
        return new
        {
            health = "Healthy",
            mockOnly = true,
            externalEndpointEnabled = false,
            checkedAt = DateTimeOffset.UtcNow
        };
    }

    private static object BuildKpiFormulaPayload(string? domain)
    {
        var normalizedDomain = string.IsNullOrWhiteSpace(domain) ? "Production" : domain.Trim();
        var formula = normalizedDomain.ToLowerInvariant() switch
        {
            "quality" => "defectRate = defectCount / inspectionCount; passRate = passedCount / inspectionCount",
            "inventory" => "turnoverDays = averageInventory / dailyIssueQuantity",
            "sales" => "onTimeDeliveryRate = onTimeDeliveredOrders / shippedOrders",
            "employee" => "attendanceRate = actualAttendanceDays / scheduledAttendanceDays",
            _ => "capacityUtilization = actualOutput / plannedCapacity; downtimeRate = downtimeMinutes / scheduledMinutes"
        };

        return new
        {
            domain = normalizedDomain,
            formula,
            source = "Mock MCP KPI formula catalog",
            isSimulationSupport = true
        };
    }

    private static object BuildArtifactQualityPayload(IReadOnlyDictionary<string, object?> input)
    {
        var preview = ReadString(input, "contentPreview") ?? string.Empty;
        var hasSimulation = preview.Contains("SimulationBusiness", StringComparison.OrdinalIgnoreCase) ||
                            preview.Contains("AI 独立模拟业务库", StringComparison.OrdinalIgnoreCase);
        var hasQueryHash = preview.Contains("queryHash", StringComparison.OrdinalIgnoreCase) ||
                           preview.Contains("query hash", StringComparison.OrdinalIgnoreCase);
        return new
        {
            artifactType = ReadString(input, "artifactType") ?? "unknown",
            passed = hasSimulation && hasQueryHash,
            checks = new
            {
                simulationMarker = hasSimulation,
                queryHash = hasQueryHash,
                noRealExternalSideEffect = true
            }
        };
    }

    private static object BuildTicketPreviewPayload(IReadOnlyDictionary<string, object?> input)
    {
        return new
        {
            title = ReadString(input, "title") ?? "SimulationBusiness follow-up",
            summary = ReadString(input, "summary") ?? string.Empty,
            sideEffectExecuted = false,
            previewOnly = true,
            externalSystem = "mock-ticket-system"
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> input, string key)
    {
        return input.TryGetValue(key, out var value)
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    private static string ComputeHash(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
