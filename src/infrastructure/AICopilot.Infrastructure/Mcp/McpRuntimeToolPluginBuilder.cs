using AICopilot.AgentPlugin;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace AICopilot.Infrastructure.Mcp;

internal sealed class McpRuntimeToolPluginBuilder(ILogger logger)
{
    public (McpClientTool Tool, McpAllowedTool Exposure)[] SelectExposedTools(
        McpServerInfo mcpServerInfo,
        IEnumerable<McpClientTool> tools)
    {
        var allowlist = mcpServerInfo.AllowedTools.ToDictionary(
            tool => tool.ToolName,
            StringComparer.OrdinalIgnoreCase);

        return tools
            .Where(tool => allowlist.ContainsKey(tool.Name))
            .Select(tool => (Tool: tool, Exposure: allowlist[tool.Name]))
            .Where(candidate => CanExposeTool(mcpServerInfo, candidate.Exposure, candidate.Tool))
            .ToArray();
    }

    public GenericBridgePlugin BuildMcpPlugin(
        McpServerInfo mcpServerInfo,
        IReadOnlyCollection<(McpClientTool Tool, McpAllowedTool Exposure)> mcpTools,
        HashSet<string> protectedNames,
        McpRuntimeClientHandle clientHandle)
    {
        var tools = mcpTools
            .Select(candidate => ToToolDefinition(
                mcpServerInfo.Name,
                candidate.Exposure.EffectiveExternalSystemType(mcpServerInfo.ExternalSystemType),
                candidate.Exposure.EffectiveCapabilityKind(mcpServerInfo.CapabilityKind),
                candidate.Exposure.EffectiveRiskLevel(mcpServerInfo.RiskLevel),
                candidate.Exposure.ReadOnlyDeclared,
                candidate.Exposure.McpReadOnlyHint ?? ReadMcpAnnotationHint(candidate.Tool, "ReadOnlyHint", "readOnlyHint"),
                candidate.Exposure.McpDestructiveHint ?? ReadMcpAnnotationHint(candidate.Tool, "DestructiveHint", "destructiveHint"),
                candidate.Exposure.McpIdempotentHint ?? ReadMcpAnnotationHint(candidate.Tool, "IdempotentHint", "idempotentHint"),
                candidate.Tool,
                protectedNames,
                clientHandle))
            .ToArray();

        return new GenericBridgePlugin
        {
            Name = mcpServerInfo.Name,
            Description = mcpServerInfo.Description,
            Tools = tools,
            ChatExposureMode = mcpServerInfo.ChatExposureMode
        };
    }

    private static AiToolDefinition ToToolDefinition(
        string serverName,
        AiToolExternalSystemType externalSystemType,
        AiToolCapabilityKind capabilityKind,
        AiToolRiskLevel riskLevel,
        bool readOnlyDeclared,
        bool? mcpReadOnlyHint,
        bool? mcpDestructiveHint,
        bool? mcpIdempotentHint,
        McpClientTool tool,
        HashSet<string> protectedNames,
        McpRuntimeClientHandle clientHandle)
    {
        var requiresApproval = protectedNames.Contains(tool.Name)
                               || riskLevel == AiToolRiskLevel.RequiresApproval;

        return new AiToolDefinition
        {
            Name = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, serverName, tool.Name),
            ToolName = tool.Name,
            Description = tool.Description,
            Kind = AiToolCallKind.Mcp,
            TargetType = AiToolTargetType.McpServer,
            TargetName = serverName,
            ServerName = serverName,
            RequiresApproval = requiresApproval,
            ExternalSystemType = externalSystemType,
            CapabilityKind = capabilityKind,
            RiskLevel = riskLevel,
            ReadOnlyDeclared = readOnlyDeclared,
            McpReadOnlyHint = mcpReadOnlyHint,
            McpDestructiveHint = mcpDestructiveHint,
            McpIdempotentHint = mcpIdempotentHint,
            JsonSchema = tool.JsonSchema.Clone(),
            ReturnJsonSchema = tool.ReturnJsonSchema?.Clone(),
            InvokeAsync = async (context, cancellationToken) =>
            {
                using var invocation = clientHandle.AcquireInvocation();
                var argumentValues = context.Arguments.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase);
                var arguments = new AIFunctionArguments(argumentValues);

                return await tool.InvokeAsync(arguments, cancellationToken);
            }
        };
    }

    private bool CanExposeTool(McpServerInfo server, McpAllowedTool exposure, McpClientTool tool)
    {
        var externalSystemType = exposure.EffectiveExternalSystemType(server.ExternalSystemType);
        var capabilityKind = exposure.EffectiveCapabilityKind(server.CapabilityKind);
        var riskLevel = exposure.EffectiveRiskLevel(server.RiskLevel);
        var descriptor = AiToolSafetyDescriptor.Create(
            exposure.ReadOnlyDeclared,
            exposure.McpReadOnlyHint ?? ReadMcpAnnotationHint(tool, "ReadOnlyHint", "readOnlyHint"),
            exposure.McpDestructiveHint ?? ReadMcpAnnotationHint(tool, "DestructiveHint", "destructiveHint"),
            exposure.McpIdempotentHint ?? ReadMcpAnnotationHint(tool, "IdempotentHint", "idempotentHint"),
            capabilityKind,
            externalSystemType,
            riskLevel);
        var decision = AiToolSafetyPolicy.Evaluate(
            descriptor,
            tool.Name,
            tool.Description,
            tool.JsonSchema,
            tool.ReturnJsonSchema);

        if (decision.IsAllowed)
        {
            logger.LogInformation(
                "MCP server {ServerName} tool {ToolName} passed safety policy. RuntimeName={RuntimeName}; ReadOnlyDeclared={ReadOnlyDeclared}; McpReadOnlyHint={McpReadOnlyHint}; McpDestructiveHint={McpDestructiveHint}",
                server.Name,
                tool.Name,
                AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, server.Name, tool.Name),
                descriptor.ReadOnlyDeclared,
                descriptor.McpReadOnlyHint,
                descriptor.McpDestructiveHint);
            return true;
        }

        logger.LogWarning(
            "MCP server {ServerName} tool {ToolName} was blocked by safety policy. RuntimeName={RuntimeName}; Reasons={Reasons}",
            server.Name,
            tool.Name,
            AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, server.Name, tool.Name),
            string.Join("; ", decision.BlockReasons ?? [decision.Reason ?? "Unknown"]));
        return false;
    }

    private static bool? ReadMcpAnnotationHint(McpClientTool tool, params string[] propertyNames)
    {
        var annotations = tool.GetType().GetProperty("Annotations")?.GetValue(tool);
        if (annotations is null)
        {
            return ReadBooleanProperty(tool, propertyNames);
        }

        return ReadBooleanProperty(annotations, propertyNames) ?? ReadBooleanProperty(tool, propertyNames);
    }

    private static bool? ReadBooleanProperty(object target, params string[] propertyNames)
    {
        var type = target.GetType();
        foreach (var propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property?.GetValue(target) is bool value)
            {
                return value;
            }
        }

        return null;
    }
}
