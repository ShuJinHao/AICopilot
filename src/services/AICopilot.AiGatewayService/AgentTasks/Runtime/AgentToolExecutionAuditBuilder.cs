using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentToolExecutionAuditBuilder
{
    private const string ToolExecutionFailedCode = "tool_execution_failed";

    internal static string ResolveExecutionErrorCode(Exception ex, AgentStep step, ToolRegistration? tool)
    {
        if (ex is AgentToolExecutionException toolExecutionException)
        {
            return toolExecutionException.Code;
        }

        if (ex is CloudAiReadException cloudAiReadException)
        {
            return cloudAiReadException.Code;
        }

        if (ex.Message.StartsWith(AppProblemCodes.ArtifactFinalized, StringComparison.OrdinalIgnoreCase))
        {
            return AppProblemCodes.ArtifactFinalized;
        }

        return step.StepType is AgentStepType.ArtifactGeneration or AgentStepType.ChartGeneration ||
               tool?.ProviderType == ToolProviderType.Artifact
            ? AppProblemCodes.ArtifactGenerationFailed
            : ToolExecutionFailedCode;
    }

    internal static string BuildSafeExceptionSummary(Exception ex)
    {
        if (ex is AgentToolExecutionException toolExecutionException)
        {
            return $"Tool execution failed. ErrorCode={toolExecutionException.Code}; ErrorType={ex.GetType().Name}.";
        }

        if (ex is CloudAiReadException cloudAiReadException)
        {
            var statusCode = cloudAiReadException.StatusCode.HasValue
                ? ((int)cloudAiReadException.StatusCode.Value).ToString()
                : "none";
            return $"Cloud read-only tool failed. ErrorCode={cloudAiReadException.Code}; StatusCode={statusCode}; ErrorType={ex.GetType().Name}.";
        }

        if (ex.Message.StartsWith(AppProblemCodes.ArtifactFinalized, StringComparison.OrdinalIgnoreCase))
        {
            return $"{AppProblemCodes.ArtifactFinalized}: Artifact workspace is finalized.";
        }

        return $"Tool execution failed. ErrorType={ex.GetType().Name}.";
    }

    internal static string BuildInputSummary(AgentStep step, ToolRegistration? tool)
    {
        var payload = new
        {
            stepIndex = step.StepIndex,
            stepType = step.StepType.ToString(),
            toolCode = step.ToolCode ?? tool?.ToolCode,
            targetName = tool?.TargetName,
            inputJsonLength = step.InputJson?.Length ?? 0
        };
        return SanitizeSummary(JsonSerializer.Serialize(payload, AgentRuntimeJson.Options), 2000) ?? "{}";
    }

    internal static string? BuildOutputSummary(object output)
    {
        return SanitizeSummary(JsonSerializer.Serialize(output, AgentRuntimeJson.Options), 4000);
    }

    internal static string BuildAuditMetadata(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        ToolRegistration? tool,
        object? output = null)
    {
        var businessQuery = TryReadBusinessQueryAudit(output);
        var toolOutputAudit = TryReadToolOutputAudit(output);
        var payload = new
        {
            taskId = task.Id.Value,
            taskCode = task.TaskCode,
            workspaceCode = workspace.WorkspaceCode,
            stepIndex = step.StepIndex,
            toolCode = step.ToolCode ?? tool?.ToolCode,
            providerType = tool?.ProviderType.ToString(),
            providerKind = toolOutputAudit.ProviderKind ?? tool?.ProviderType.ToString(),
            isMock = toolOutputAudit.IsMock ?? (tool?.ProviderType == ToolProviderType.MockMcp),
            targetType = tool?.TargetType.ToString(),
            targetName = tool?.TargetName,
            timeoutSeconds = tool?.TimeoutSeconds,
            auditLevel = tool?.AuditLevel.ToString(),
            riskLevel = tool?.RiskLevel.ToString(),
            requiresApproval = tool?.RequiresApproval,
            approvalPolicy = tool?.ApprovalPolicy,
            approvalStatus = ResolveApprovalStatus(step, tool),
            dataBoundary = tool?.DataBoundary.ToString(),
            schemaVersion = tool?.SchemaVersion,
            toolCatalogVersion = tool?.CatalogVersion,
            toolRunId = toolOutputAudit.ToolRunId,
            resultHash = toolOutputAudit.ResultHash,
            businessQuery
        };
        return SanitizeSummary(JsonSerializer.Serialize(payload, AgentRuntimeJson.Options), 4000) ?? "{}";
    }

    private static string ResolveApprovalStatus(AgentStep step, ToolRegistration? tool)
    {
        var requiresApproval = step.RequiresApproval ||
                               tool?.RequiresApproval == true ||
                               tool?.RiskLevel is AiToolRiskLevel.RequiresApproval
                                   or AiToolRiskLevel.High
                                   or AiToolRiskLevel.Critical;
        if (!requiresApproval)
        {
            return "NotRequired";
        }

        return step.Status is AgentStepStatus.Approved or AgentStepStatus.Running
            ? "Approved"
            : "Required";
    }

    private static ToolOutputAudit TryReadToolOutputAudit(object? output)
    {
        if (output is null)
        {
            return new ToolOutputAudit(null, null, null, null);
        }

        try
        {
            var serialized = JsonSerializer.Serialize(output, AgentRuntimeJson.Options);
            using var document = JsonDocument.Parse(serialized);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ToolOutputAudit(null, null, null, ComputeSha256(serialized));
            }

            var providerKind = TryGetString(root, "providerKind") ?? TryGetString(root, "providerType");
            var isMock = TryGetBool(root, "isMock");
            var toolRunId = TryGetString(root, "toolRunId");
            var resultHash = TryGetString(root, "resultHash") ?? ComputeSha256(serialized);
            return new ToolOutputAudit(providerKind, isMock, toolRunId, resultHash);
        }
        catch (JsonException)
        {
            return new ToolOutputAudit(null, null, null, null);
        }
    }

    private static object? TryReadBusinessQueryAudit(object? output)
    {
        if (output is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(output, AgentRuntimeJson.Options));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("sourceType", out var sourceType) ||
                !string.Equals(sourceType.GetString(), "BusinessDatabase", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new
            {
                SourceType = sourceType.GetString(),
                SourceMode = TryGetString(root, "sourceMode"),
                IsSimulation = TryGetBool(root, "isSimulation"),
                SourceLabel = TryGetString(root, "sourceLabel"),
                QueryHash = TryGetString(root, "queryHash"),
                RowCount = TryGetInt(root, "rowCount"),
                IsTruncated = TryGetBool(root, "isTruncated")
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? TryGetBool(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static int? TryGetInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;
    }

    private static string ComputeSha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string? SanitizeSummary(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = Regex.Replace(
            value,
            @"(?i)(api[_-]?key|token|password|secret|connection\s*string)\s*[""':=]+\s*[^,""}\s]+",
            "$1=******");
        sanitized = Regex.Replace(
            sanitized,
            @"[A-Za-z]:\\[^\s""']+",
            "[redacted-path]");
        sanitized = Regex.Replace(
            sanitized,
            @"(?i)(Host|Username|Password|Database|Port)\s*=\s*[^;""'}]+",
            "$1=******");

        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    internal static string? ExtractArtifactId(object output)
    {
        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(output, AgentRuntimeJson.Options));
            return document.RootElement.TryGetProperty("artifactId", out var artifactId)
                ? artifactId.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }



    private sealed record ToolOutputAudit(
        string? ProviderKind,
        bool? IsMock,
        string? ToolRunId,
        string? ResultHash);

}
