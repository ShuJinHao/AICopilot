using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Agents;

public sealed class MainChatToolGate(
    ToolRegistryGuard registryGuard,
    IIdentityAccessService identityAccessService,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<AiToolDefinition>> FilterRegisteredAsync(
        IEnumerable<AiToolDefinition> candidates,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return [];
        }

        var access = await identityAccessService.GetCurrentUserAccessAsync(
            userId,
            cancellationToken);
        if (access is null ||
            !access.Permissions.Contains("AiGateway.Chat", StringComparer.Ordinal))
        {
            return [];
        }

        var exposed = new List<AiToolDefinition>();
        foreach (var candidate in candidates)
        {
            var decision = await registryGuard.ValidateAsync(
                candidate.Name,
                userId,
                cancellationToken);
            if (!decision.IsAllowed || decision.Tool is null ||
                !MatchesRegisteredContract(candidate, decision.Tool) ||
                !IsSafeForMainChat(candidate))
            {
                continue;
            }

            exposed.Add(candidate.WithGovernance(
                decision.Tool.RequiresApproval,
                decision.Tool.RiskLevel,
                decision.Tool.RequiredPermission,
                decision.Tool.AuditLevel.ToString(),
                decision.Tool.DataBoundary.ToString(),
                decision.Tool.SchemaVersion));
        }

        return exposed;
    }

    public async Task<bool> CanExposeFixedAsync(
        CancellationToken cancellationToken,
        params string[] requiredPermissions)
    {
        if (currentUser.Id is not { } userId)
        {
            return false;
        }

        var access = await identityAccessService.GetCurrentUserAccessAsync(
            userId,
            cancellationToken);
        return access is not null &&
               requiredPermissions
                   .Append("AiGateway.Chat")
                   .Distinct(StringComparer.Ordinal)
                   .All(permission =>
                       access.Permissions.Contains(permission, StringComparer.Ordinal));
    }

    internal static bool MatchesRegisteredContract(
        AiToolDefinition runtime,
        ToolRegistration registration)
    {
        if (runtime.Identity is not { } identity ||
            !string.Equals(runtime.Name, registration.ToolCode, StringComparison.Ordinal) ||
            !string.Equals(identity.TargetName, registration.TargetName, StringComparison.Ordinal) ||
            MapTargetType(identity.TargetType) != registration.TargetType ||
            !MatchesProvider(identity.TargetType, registration.ProviderType) ||
            runtime.RiskLevel != registration.RiskLevel ||
            runtime.RequiresApproval != registration.RequiresApproval ||
            !string.Equals(
                runtime.RequiredPermission,
                registration.RequiredPermission,
                StringComparison.Ordinal) ||
            !string.Equals(
                runtime.AuditLevel,
                registration.AuditLevel.ToString(),
                StringComparison.Ordinal) ||
            !string.Equals(
                runtime.DataBoundary,
                registration.DataBoundary.ToString(),
                StringComparison.Ordinal) ||
            runtime.SchemaVersion != registration.SchemaVersion)
        {
            return false;
        }

        var runtimeInput = ToolInputSchemaValidator.ValidateSchema(
            runtime.JsonSchema?.GetRawText() ?? "{}");
        var runtimeOutput = ToolOutputSchemaValidator.ValidateSchema(
            runtime.ReturnJsonSchema?.GetRawText() ?? "{}");
        var registeredInput = ToolInputSchemaValidator.ValidateSchema(
            registration.InputSchemaJson);
        var registeredOutput = ToolOutputSchemaValidator.ValidateSchema(
            registration.OutputSchemaJson);
        return runtimeInput.IsValid &&
               runtimeOutput.IsValid &&
               registeredInput.IsValid &&
               registeredOutput.IsValid &&
               string.Equals(
                   runtimeInput.CanonicalJson,
                   registeredInput.CanonicalJson,
                   StringComparison.Ordinal) &&
               string.Equals(
                   runtimeOutput.CanonicalJson,
                   registeredOutput.CanonicalJson,
                   StringComparison.Ordinal);
    }

    private static ToolRegistrationTargetType MapTargetType(AiToolTargetType targetType)
    {
        return targetType == AiToolTargetType.McpServer
            ? ToolRegistrationTargetType.McpServer
            : ToolRegistrationTargetType.Plugin;
    }

    private static bool MatchesProvider(
        AiToolTargetType targetType,
        ToolProviderType providerType)
    {
        return targetType == AiToolTargetType.McpServer
            ? providerType == ToolProviderType.Mcp
            : providerType == ToolProviderType.BuiltIn;
    }

    private static bool IsSafeForMainChat(AiToolDefinition tool)
    {
        if (tool.RiskLevel is AiToolRiskLevel.Blocked or AiToolRiskLevel.Critical ||
            tool.ExternalSystemType == AiToolExternalSystemType.Unknown ||
            tool.CapabilityKind == AiToolCapabilityKind.SideEffecting ||
            string.Equals(
                tool.RequiredPermission,
                "DataSource.TextToSql",
                StringComparison.Ordinal))
        {
            return false;
        }

        if (tool.TargetType == AiToolTargetType.McpServer)
        {
            return AiToolSafetyPolicy.EvaluateConfiguredMcp(tool).IsAllowed;
        }

        return AiToolSafetyPolicy.Evaluate(
                   tool.ExternalSystemType,
                   tool.CapabilityKind,
                   tool.RiskLevel,
                   tool.ToolName ?? tool.Name,
                   tool.Description,
                   tool.ReadOnlyDeclared,
                   tool.JsonSchema,
                   tool.ReturnJsonSchema)
               .IsAllowed &&
               tool.CapabilityKind is
                   AiToolCapabilityKind.ReadOnlyQuery or
                   AiToolCapabilityKind.Diagnostics or
                   AiToolCapabilityKind.LocalSuggestion;
    }
}
