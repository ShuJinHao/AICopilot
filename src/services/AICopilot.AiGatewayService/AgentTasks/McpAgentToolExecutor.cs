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
    public bool CanExecute(ToolRegistration tool, AgentStep step)
    {
        return tool.ProviderType == ToolProviderType.Mcp &&
               tool.TargetType == ToolRegistrationTargetType.McpServer;
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext context)
    {
        var tool = ResolveRuntimeTool(context.ToolRegistration);
        EnsureRegistryMatchesRuntimeTool(context.ToolRegistration, tool);
        EnsureMcpToolSafety(tool);

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
        var outputValidation = ToolOutputSchemaValidator.ValidateAndCanonicalize(
            rawOutput,
            context.ToolRegistration.OutputSchemaJson);
        if (!outputValidation.IsValid)
        {
            throw new AgentToolExecutionException(
                outputValidation.IsPayloadTooLarge
                    ? AppProblemCodes.EvidencePayloadTooLarge
                    : AppProblemCodes.ToolOutputSchemaInvalid,
                outputValidation.Error ?? "MCP tool output does not match the registry schema.");
        }

        return AgentToolExecutionResult.FromValidatedProviderOutput(
            context.ToolRegistration,
            outputValidation,
            AgentToolDurableOutputBuilder.BuildProviderEnvelope(
                context.ToolRegistration,
                outputValidation.CanonicalJson!));
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

        var registrySchema = ToolInputSchemaContractV1.Validate(registration.InputSchemaJson);
        var runtimeSchema = ToolInputSchemaContractV1.Validate(tool.JsonSchema?.GetRawText());
        if (!registrySchema.IsValid ||
            !runtimeSchema.IsValid ||
            !string.Equals(registrySchema.CanonicalJson, runtimeSchema.CanonicalJson, StringComparison.Ordinal))
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.PlannerToolSchemaUnsupported,
                $"MCP tool '{registration.ToolCode}' runtime input contract does not exactly match the registry schema.");
        }

        var registryOutputSchema = ToolOutputSchemaContractV1.Validate(registration.OutputSchemaJson);
        var runtimeOutputSchema = ToolOutputSchemaContractV1.Validate(tool.ReturnJsonSchema?.GetRawText());
        if (!registryOutputSchema.IsValid ||
            !runtimeOutputSchema.IsValid ||
            !string.Equals(
                registryOutputSchema.CanonicalJson,
                runtimeOutputSchema.CanonicalJson,
                StringComparison.Ordinal))
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.PlannerToolSchemaUnsupported,
                $"MCP tool '{registration.ToolCode}' runtime output contract does not exactly match the registry schema.");
        }
    }

    private static void EnsureMcpToolSafety(AiToolDefinition tool)
    {
        var decision = AiToolSafetyPolicy.EvaluateConfiguredMcp(tool);
        if (!decision.IsAllowed)
        {
            throw new AgentToolExecutionException(
                AppProblemCodes.ToolBlocked,
                decision.Reason!);
        }
    }

}
