using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.SharedKernel.Ai;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AICopilot.Infrastructure.Mcp;

internal sealed record McpRuntimeToolCandidate(
    McpClientTool Tool,
    McpAllowedTool Exposure,
    JsonElement InputSchema,
    JsonElement OutputSchema,
    bool? McpReadOnlyHint,
    bool? McpDestructiveHint,
    bool? McpIdempotentHint,
    AiToolExternalSystemType ExternalSystemType,
    AiToolCapabilityKind CapabilityKind,
    AiToolRiskLevel RiskLevel);

internal sealed record McpRuntimeToolBinding(
    McpRuntimeToolCandidate Candidate,
    McpRuntimeToolGovernance Governance);

internal sealed record McpRuntimeValueValidation(
    bool IsValid,
    JsonElement? Value,
    string? Error)
{
    public static McpRuntimeValueValidation Success(JsonElement value) => new(true, value, null);

    public static McpRuntimeValueValidation Failure(string error) => new(false, null, error);
}

internal static class McpRuntimeToolContract
{
    public static bool TryCreateCandidate(
        McpServerInfo server,
        McpAllowedTool exposure,
        McpClientTool tool,
        out McpRuntimeToolCandidate? candidate,
        out string? error)
    {
        candidate = null;
        error = null;

        var protocolTool = tool.ProtocolTool;
        if (string.IsNullOrWhiteSpace(protocolTool.Name) ||
            !string.Equals(tool.Name, protocolTool.Name, StringComparison.Ordinal))
        {
            error = "MCP discovery did not provide a stable canonical tool identity.";
            return false;
        }

        var inputContract = ToolInputSchemaContractV1.Validate(protocolTool.InputSchema.GetRawText());
        if (!inputContract.IsValid)
        {
            error = inputContract.Error ?? "MCP input schema is outside the supported strict subset.";
            return false;
        }

        if (!protocolTool.OutputSchema.HasValue)
        {
            error = "MCP output schema is required for governed model exposure.";
            return false;
        }

        var outputContract = ToolOutputSchemaContractV1.Validate(protocolTool.OutputSchema.Value.GetRawText());
        if (!outputContract.IsValid)
        {
            error = outputContract.Error ?? "MCP output schema is outside the supported strict subset.";
            return false;
        }

        var annotations = protocolTool.Annotations;
        if (!TryResolveHint(
                "read-only",
                exposure.McpReadOnlyHint,
                annotations?.ReadOnlyHint,
                out var readOnlyHint,
                out error) ||
            !TryResolveHint(
                "destructive",
                exposure.McpDestructiveHint,
                annotations?.DestructiveHint,
                out var destructiveHint,
                out error) ||
            !TryResolveHint(
                "idempotent",
                exposure.McpIdempotentHint,
                annotations?.IdempotentHint,
                out var idempotentHint,
                out error))
        {
            return false;
        }

        var inputSchema = ParseCanonicalSchema(inputContract.CanonicalJson!);
        var outputSchema = ParseCanonicalSchema(outputContract.CanonicalJson!);
        var metadata = new AiToolConfiguredMcpMetadata(
            exposure.ReadOnlyDeclared,
            readOnlyHint,
            destructiveHint,
            idempotentHint,
            exposure.EffectiveCapabilityKind(server.CapabilityKind),
            exposure.EffectiveExternalSystemType(server.ExternalSystemType),
            exposure.EffectiveRiskLevel(server.RiskLevel));
        var safety = AiToolSafetyPolicy.EvaluateConfiguredMcp(
            metadata,
            protocolTool.Name,
            protocolTool.Description,
            inputSchema,
            outputSchema);
        if (!safety.IsAllowed)
        {
            error = safety.Reason ?? "MCP tool was rejected by the shared safety policy.";
            return false;
        }

        candidate = new McpRuntimeToolCandidate(
            tool,
            exposure,
            inputSchema,
            outputSchema,
            readOnlyHint,
            destructiveHint,
            idempotentHint,
            metadata.ExternalSystemType,
            metadata.CapabilityKind,
            metadata.RiskLevel);
        return true;
    }

    public static string ComputeFingerprint(
        string serverName,
        IReadOnlyCollection<McpRuntimeToolBinding> bindings)
    {
        var fingerprintJson = JsonSerializer.Serialize(
            new
            {
                serverName,
                tools = bindings
                    .OrderBy(binding => binding.Candidate.Tool.Name, StringComparer.Ordinal)
                    .Select(binding => new
                    {
                        runtimeName = AiToolIdentity.CreateRuntimeName(
                            AiToolTargetType.McpServer,
                            serverName,
                            binding.Candidate.Tool.Name),
                        toolName = binding.Candidate.Tool.Name,
                        binding.Candidate.Tool.Description,
                        inputSchema = binding.Candidate.InputSchema,
                        outputSchema = binding.Candidate.OutputSchema,
                        binding.Candidate.Exposure.ReadOnlyDeclared,
                        binding.Candidate.McpReadOnlyHint,
                        binding.Candidate.McpDestructiveHint,
                        binding.Candidate.McpIdempotentHint,
                        binding.Candidate.ExternalSystemType,
                        binding.Candidate.CapabilityKind,
                        binding.Governance.RiskLevel,
                        binding.Governance.RequiresApproval,
                        binding.Governance.RequiredPermission,
                        binding.Governance.AuditLevel,
                        binding.Governance.DataBoundary,
                        binding.Governance.SchemaVersion
                    })
                    .ToArray()
            });
        var canonical = AgentCanonicalJsonV1.Canonicalize(fingerprintJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    public static McpRuntimeValueValidation ValidateArguments(
        McpRuntimeToolCandidate candidate,
        IReadOnlyDictionary<string, object?> arguments)
    {
        string serialized;
        try
        {
            serialized = JsonSerializer.Serialize(arguments, JsonSerializerOptions.Web);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return McpRuntimeValueValidation.Failure("MCP tool arguments could not be serialized safely.");
        }

        var normalized = AgentStructuredPayloadPolicyV1.NormalizeNodeToolInput(serialized);
        if (!normalized.IsValid)
        {
            return McpRuntimeValueValidation.Failure(
                normalized.Error ?? "MCP tool arguments violate the bounded input policy.");
        }

        using var document = JsonDocument.Parse(normalized.CanonicalJson!);
        var validationError = ToolInputSchemaContractV1.ValidateValue(
            document.RootElement,
            candidate.InputSchema);
        return validationError is null
            ? McpRuntimeValueValidation.Success(document.RootElement.Clone())
            : McpRuntimeValueValidation.Failure(validationError);
    }

    public static McpRuntimeValueValidation ValidateStructuredResult(
        McpRuntimeToolCandidate candidate,
        CallToolResult result)
    {
        return ValidateStructuredResult(candidate.OutputSchema, result);
    }

    internal static McpRuntimeValueValidation ValidateStructuredResult(
        JsonElement outputSchema,
        CallToolResult result)
    {
        if (result.IsError == true)
        {
            return McpRuntimeValueValidation.Failure(
                "MCP tool execution returned a governed error result.");
        }

        if (!result.StructuredContent.HasValue)
        {
            return McpRuntimeValueValidation.Failure(
                "MCP tool execution did not return schema-bound structured content.");
        }

        var normalized = AgentStructuredPayloadPolicyV1.NormalizeInlineOutput(
            result.StructuredContent.Value.GetRawText());
        if (!normalized.IsValid)
        {
            return McpRuntimeValueValidation.Failure(
                normalized.Error ?? "MCP tool output violates the bounded inline-output policy.");
        }

        using var document = JsonDocument.Parse(normalized.CanonicalJson!);
        var validationError = ToolOutputSchemaContractV1.ValidateValue(
            document.RootElement,
            outputSchema);
        return validationError is null
            ? McpRuntimeValueValidation.Success(document.RootElement.Clone())
            : McpRuntimeValueValidation.Failure(validationError);
    }

    private static bool TryResolveHint(
        string hintName,
        bool? configured,
        bool? discovered,
        out bool? effective,
        out string? error)
    {
        if (configured.HasValue && discovered.HasValue && configured.Value != discovered.Value)
        {
            effective = null;
            error = $"MCP {hintName} annotation conflicts with local governance metadata.";
            return false;
        }

        effective = discovered ?? configured;
        error = null;
        return true;
    }

    private static JsonElement ParseCanonicalSchema(string canonicalJson)
    {
        using var document = JsonDocument.Parse(canonicalJson);
        return document.RootElement.Clone();
    }
}
