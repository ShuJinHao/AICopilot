using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.Infrastructure.Mcp;

public sealed record McpDiscoveredToolRegistration(
    string ToolCode,
    string ToolName,
    string? Description,
    string InputSchemaJson,
    string OutputSchemaJson,
    AiToolRiskLevel RiskLevel);

public sealed class McpToolRegistrySynchronizer(IRepository<ToolRegistration> toolRepository)
{
    public async Task UpsertDiscoveredToolsAsync(
        string serverName,
        IReadOnlyCollection<McpDiscoveredToolRegistration> tools,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var discoveredToolCodes = tools
            .Select(tool => tool.ToolCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var existingTools = await toolRepository.GetListAsync(
            item => (item.ProviderType == ToolProviderType.Mcp &&
                     item.TargetType == ToolRegistrationTargetType.McpServer &&
                     item.TargetName == serverName) ||
                    discoveredToolCodes.Contains(item.ToolCode),
            cancellationToken);
        var existingByCode = existingTools.ToDictionary(
            item => item.ToolCode,
            StringComparer.Ordinal);
        var discoveredCodes = new HashSet<string>(StringComparer.Ordinal);
        var hasChanges = false;

        foreach (var discoveredTool in tools)
        {
            if (!discoveredCodes.Add(discoveredTool.ToolCode))
            {
                if (existingByCode.TryGetValue(discoveredTool.ToolCode, out var duplicateExisting) &&
                    duplicateExisting.DisableForUnavailableContract(
                        SelectConservativeRiskLevel(
                            duplicateExisting.RiskLevel,
                            discoveredTool.RiskLevel),
                        now))
                {
                    toolRepository.Update(duplicateExisting);
                    hasChanges = true;
                }

                continue;
            }

            existingByCode.TryGetValue(discoveredTool.ToolCode, out var existing);
            var canonicalToolCode = AiToolIdentity.CreateRuntimeName(
                AiToolTargetType.McpServer,
                serverName,
                discoveredTool.ToolName);
            if (!string.Equals(
                    discoveredTool.ToolCode,
                    canonicalToolCode,
                    StringComparison.Ordinal))
            {
                if (existing is not null && existing.DisableForUnavailableContract(
                        SelectConservativeRiskLevel(existing.RiskLevel, discoveredTool.RiskLevel),
                        now))
                {
                    toolRepository.Update(existing);
                    hasChanges = true;
                }

                continue;
            }

            var inputSchemaContract = ToolInputSchemaContractV1.Validate(discoveredTool.InputSchemaJson);
            var outputSchemaContract = ToolOutputSchemaContractV1.Validate(discoveredTool.OutputSchemaJson);
            if (!inputSchemaContract.IsValid || !outputSchemaContract.IsValid)
            {
                // An invalid discovery contract is not registered as an open schema.
                // If a previous version exists, make it non-executable so catalog and
                // capability resolution see a typed unavailable tool.
                if (existing is not null)
                {
                    if (existing.DisableForUnavailableContract(
                            SelectConservativeRiskLevel(existing.RiskLevel, discoveredTool.RiskLevel),
                            now))
                    {
                        toolRepository.Update(existing);
                        hasChanges = true;
                    }
                }

                continue;
            }

            var canonicalInputSchema = inputSchemaContract.CanonicalJson!;
            var canonicalOutputSchema = outputSchemaContract.CanonicalJson!;

            if (existing is null)
            {
                toolRepository.Add(new ToolRegistration(
                    discoveredTool.ToolCode,
                    BuildDisplayName(serverName, discoveredTool.ToolName),
                    BuildDescription(serverName, discoveredTool),
                    ToolProviderType.Mcp,
                    ToolRegistrationTargetType.McpServer,
                    serverName,
                    canonicalInputSchema,
                    canonicalOutputSchema,
                    discoveredTool.RiskLevel,
                    requiredPermission: null,
                    requiresApproval: true,
                    isEnabled: false,
                    timeoutSeconds: 120,
                    ToolAuditLevel.Standard,
                    now));
                hasChanges = true;
                continue;
            }

            var conservativeRiskLevel = SelectConservativeRiskLevel(
                existing.RiskLevel,
                discoveredTool.RiskLevel);
            var hasGovernedContractDrift =
                existing.ProviderType != ToolProviderType.Mcp ||
                existing.TargetType != ToolRegistrationTargetType.McpServer ||
                !string.Equals(existing.TargetName, serverName, StringComparison.Ordinal) ||
                !string.Equals(existing.InputSchemaJson, canonicalInputSchema, StringComparison.Ordinal) ||
                !string.Equals(existing.OutputSchemaJson, canonicalOutputSchema, StringComparison.Ordinal) ||
                existing.RiskLevel != conservativeRiskLevel;

            if (hasGovernedContractDrift)
            {
                existing.Update(
                    BuildDisplayName(serverName, discoveredTool.ToolName),
                    BuildDescription(serverName, discoveredTool),
                    ToolProviderType.Mcp,
                    ToolRegistrationTargetType.McpServer,
                    serverName,
                    canonicalInputSchema,
                    canonicalOutputSchema,
                    conservativeRiskLevel,
                    existing.RequiredPermission,
                    requiresApproval: true,
                    isEnabled: false,
                    existing.TimeoutSeconds,
                    existing.AuditLevel,
                    now,
                    isExecutableByAgent: false,
                    schemaVersion: checked(existing.SchemaVersion + 1),
                    catalogVersion: checked(existing.CatalogVersion + 1));
                toolRepository.Update(existing);
                hasChanges = true;
                continue;
            }

            var displayName = BuildDisplayName(serverName, discoveredTool.ToolName);
            var description = BuildDescription(serverName, discoveredTool);
            if (!string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal) ||
                !string.Equals(existing.Description, description, StringComparison.Ordinal))
            {
                existing.Update(
                    displayName,
                    description,
                    ToolProviderType.Mcp,
                    ToolRegistrationTargetType.McpServer,
                    serverName,
                    canonicalInputSchema,
                    canonicalOutputSchema,
                    existing.RiskLevel,
                    existing.RequiredPermission,
                    existing.RequiresApproval,
                    existing.IsEnabled,
                    existing.TimeoutSeconds,
                    existing.AuditLevel,
                    now);
                toolRepository.Update(existing);
                hasChanges = true;
            }
        }

        foreach (var staleTool in existingTools.Where(tool =>
                     tool.ProviderType == ToolProviderType.Mcp &&
                     tool.TargetType == ToolRegistrationTargetType.McpServer &&
                     string.Equals(tool.TargetName, serverName, StringComparison.Ordinal) &&
                     !discoveredCodes.Contains(tool.ToolCode)))
        {
            if (staleTool.DisableForUnavailableContract(staleTool.RiskLevel, now))
            {
                toolRepository.Update(staleTool);
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await toolRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private static AiToolRiskLevel SelectConservativeRiskLevel(
        AiToolRiskLevel existing,
        AiToolRiskLevel discovered)
    {
        // AiToolRiskLevel values are intentionally not ordered by severity, so
        // rediscovery governance must never rely on numeric Max/CompareTo.
        if (existing == AiToolRiskLevel.Critical || discovered == AiToolRiskLevel.Critical)
        {
            return AiToolRiskLevel.Critical;
        }

        if (existing == AiToolRiskLevel.Blocked || discovered == AiToolRiskLevel.Blocked)
        {
            return AiToolRiskLevel.Blocked;
        }

        if (existing == AiToolRiskLevel.High || discovered == AiToolRiskLevel.High)
        {
            return AiToolRiskLevel.High;
        }

        if (existing == AiToolRiskLevel.RequiresApproval || discovered == AiToolRiskLevel.RequiresApproval)
        {
            return AiToolRiskLevel.RequiresApproval;
        }

        if (existing == AiToolRiskLevel.Medium || discovered == AiToolRiskLevel.Medium)
        {
            return AiToolRiskLevel.Medium;
        }

        return AiToolRiskLevel.Low;
    }

    private static string BuildDisplayName(string serverName, string toolName)
    {
        var displayName = $"{serverName}:{toolName}";
        return displayName.Length <= 160 ? displayName : displayName[..160];
    }

    private static string BuildDescription(string serverName, McpDiscoveredToolRegistration tool)
    {
        var description = string.IsNullOrWhiteSpace(tool.Description)
            ? $"MCP tool discovered from server '{serverName}'."
            : tool.Description.Trim();
        return description.Length <= 1000 ? description : description[..1000];
    }
}
