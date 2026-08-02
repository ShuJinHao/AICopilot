using AICopilot.SharedKernel.Ai;

namespace AICopilot.Core.AiGateway.Aggregates.Tools;

public static class ToolOutputSchemaContractAuthority
{
    public static ToolOutputSchemaContractResult Validate(
        ToolProviderType providerType,
        string? schemaJson)
    {
        return providerType == ToolProviderType.Mcp
            ? McpToolOutputSchemaContractV1.Validate(schemaJson)
            : ToolOutputSchemaContractV1.Validate(schemaJson);
    }
}
