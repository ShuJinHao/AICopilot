using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class McpAgentToolExecutor(
    IAgentPluginCatalog pluginCatalog,
    IServiceProvider serviceProvider)
    : IAgentToolExecutor
{
    private const int MaxOutputSummaryLength = 6000;

    public bool CanExecute(ToolRegistration tool, AgentStep step)
    {
        return tool.ProviderType == ToolProviderType.Mcp &&
               tool.TargetType == ToolRegistrationTargetType.McpServer;
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context)
    {
        var tool = ResolveRuntimeTool(context.ToolRegistration);
        EnsureRegistryMatchesRuntimeTool(context.ToolRegistration, tool);
        EnsureMcpToolSafety(context.ToolRegistration, tool);

        if (tool.InvokeAsync is null)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.ToolExecutionNotFound,
                $"MCP tool '{context.ToolRegistration.ToolCode}' has no runtime invocation delegate.");
        }

        var inputValidation = ToolInputSchemaValidator.ValidateAndParse(
            context.Step.InputJson,
            context.ToolRegistration.InputSchemaJson);
        if (!inputValidation.IsValid)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.AgentPlanSchemaInvalid,
                inputValidation.Error ?? "MCP tool input does not match registry schema.");
        }

        var invocationContext = new AiToolInvocationContext(
            inputValidation.Arguments,
            serviceProvider,
            new Dictionary<object, object?>
            {
                ["taskId"] = context.Task.Id.Value,
                ["stepId"] = context.Step.Id.Value,
                ["toolCode"] = context.ToolRegistration.ToolCode,
                ["providerType"] = context.ToolRegistration.ProviderType.ToString(),
                ["targetType"] = context.ToolRegistration.TargetType.ToString(),
                ["targetName"] = context.ToolRegistration.TargetName
            });

        var rawOutput = await tool.InvokeAsync(invocationContext, context.CancellationToken);
        return AgentToolExecutionResult.From(BuildSafeOutput(context.ToolRegistration, tool, rawOutput));
    }

    private AiToolDefinition ResolveRuntimeTool(ToolRegistration registration)
    {
        var tool = pluginCatalog.GetAllTools()
            .FirstOrDefault(item => string.Equals(item.Name, registration.ToolCode, StringComparison.OrdinalIgnoreCase));
        return tool ?? throw new AgentToolExecutionException(
            AppProblemCodes.ToolExecutionNotFound,
            $"MCP tool '{registration.ToolCode}' is not available in the current runtime.");
    }

    private static void EnsureRegistryMatchesRuntimeTool(ToolRegistration registration, AiToolDefinition tool)
    {
        if (tool.TargetType != AiToolTargetType.McpServer ||
            !string.Equals(tool.TargetName, registration.TargetName, StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.ToolBlocked,
                $"MCP tool '{registration.ToolCode}' runtime identity does not match registry target.");
        }
    }

    private static void EnsureMcpToolSafety(ToolRegistration registration, AiToolDefinition tool)
    {
        if (McpTargetTrustPolicy.RequiresCloudReadOnly(
                registration.TargetName,
                tool.TargetName,
                tool.ServerName,
                tool.ToolName,
                tool.Description)
            && tool.ExternalSystemType != AiToolExternalSystemType.CloudReadOnly)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.ToolBlocked,
                $"MCP tool '{tool.Name}' target trust requires CloudReadOnly classification.");
        }

        var decision = AiToolSafetyPolicy.EvaluateConfigured(
            tool.ReadOnlyDeclared,
            tool.McpReadOnlyHint,
            tool.McpDestructiveHint,
            tool.McpIdempotentHint,
            tool.CapabilityKind,
            tool.ExternalSystemType,
            tool.RiskLevel,
            tool.ToolName ?? tool.Name,
            tool.Description,
            tool.JsonSchema,
            tool.ReturnJsonSchema);
        if (!decision.IsAllowed)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.ToolBlocked,
            decision.Reason ?? $"MCP tool '{tool.Name}' violates runtime safety policy.");
        }
    }

    private static object BuildSafeOutput(ToolRegistration registration, AiToolDefinition tool, object? rawOutput)
    {
        var serialized = SerializeSafe(rawOutput);
        var sanitized = ToolExecutionRecordSanitizer.Sanitize(serialized, MaxOutputSummaryLength) ?? "{}";
        return new
        {
            providerType = registration.ProviderType.ToString(),
            toolCode = registration.ToolCode,
            serverName = tool.ServerName ?? tool.TargetName ?? registration.TargetName,
            toolName = tool.ToolName ?? tool.Name,
            resultJson = sanitized,
            isTruncated = serialized.Length > MaxOutputSummaryLength
        };
    }

    private static string SerializeSafe(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        try
        {
            return JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                type = value.GetType().Name,
                text = value.ToString(),
                serializationError = ex.GetType().Name
            }, JsonSerializerOptions.Web);
        }
    }
}
