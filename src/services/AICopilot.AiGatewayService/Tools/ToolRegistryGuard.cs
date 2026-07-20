using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Tools;

public sealed record ToolRegistryDecision(
    bool IsAllowed,
    ToolRegistration? Tool,
    ApiProblemDescriptor? Problem)
{
    public static ToolRegistryDecision Allow(ToolRegistration tool) => new(true, tool, null);

    public static ToolRegistryDecision Reject(string code, string detail) =>
        new(false, null, new ApiProblemDescriptor(code, detail));
}

public sealed class ToolRegistryGuard(
    IReadRepository<ToolRegistration> toolRepository,
    IIdentityAccessService identityAccessService)
{
    public async Task<ToolRegistryDecision> ValidateAsync(
        string? toolCode,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toolCode))
        {
            return ToolRegistryDecision.Reject(AppProblemCodes.ToolNotRegistered, "Agent step has no registered tool code.");
        }

        var tool = await toolRepository.GetAsync(
            item => item.ToolCode == toolCode.Trim(),
            cancellationToken: cancellationToken);
        if (tool is null)
        {
            return ToolRegistryDecision.Reject(
                AppProblemCodes.ToolNotRegistered,
                $"Tool '{toolCode}' is not registered.");
        }

        if (tool.RiskLevel is AiToolRiskLevel.Blocked or AiToolRiskLevel.Critical)
        {
            return ToolRegistryDecision.Reject(
                AppProblemCodes.ToolBlocked,
                $"Tool '{tool.ToolCode}' is blocked by registry policy.");
        }

        if (!tool.IsEnabled)
        {
            return ToolRegistryDecision.Reject(
                tool.ProviderType == ToolProviderType.CloudReadonly
                    ? AppProblemCodes.CloudReadonlyToolDisabled
                    : AppProblemCodes.ToolDisabled,
                $"Tool '{tool.ToolCode}' is disabled.");
        }

        if (!tool.IsExecutableByAgent)
        {
            return ToolRegistryDecision.Reject(
                AppProblemCodes.ToolDisabled,
                $"Tool '{tool.ToolCode}' is not executable by Agent policy.");
        }

        var inputSchema = ToolInputSchemaValidator.ValidateSchema(tool.InputSchemaJson);
        if (!inputSchema.IsValid)
        {
            return ToolRegistryDecision.Reject(
                AppProblemCodes.PlannerToolSchemaUnsupported,
                $"Tool '{tool.ToolCode}' has an unavailable input contract: {inputSchema.Error}");
        }

        var outputSchema = ToolOutputSchemaValidator.ValidateSchema(tool.OutputSchemaJson);
        if (!outputSchema.IsValid)
        {
            return ToolRegistryDecision.Reject(
                AppProblemCodes.PlannerToolSchemaUnsupported,
                $"Tool '{tool.ToolCode}' has an unavailable output contract: {outputSchema.Error}");
        }

        var builtIn = BuiltInToolRegistrations.FindAgentRuntimeTool(tool.ToolCode);
        if (builtIn is not null)
        {
            var builtInSchema = ToolInputSchemaContractV1.Validate(builtIn.InputSchemaJson);
            var builtInOutputSchema = ToolOutputSchemaContractV1.Validate(builtIn.OutputSchemaJson);
            if (!builtInSchema.IsValid ||
                !builtInOutputSchema.IsValid ||
                !string.Equals(inputSchema.CanonicalJson, builtInSchema.CanonicalJson, StringComparison.Ordinal) ||
                !string.Equals(outputSchema.CanonicalJson, builtInOutputSchema.CanonicalJson, StringComparison.Ordinal) ||
                tool.SchemaVersion != builtIn.SchemaVersion ||
                tool.CatalogVersion != builtIn.CatalogVersion)
            {
                return ToolRegistryDecision.Reject(
                    AppProblemCodes.PlannerToolSchemaUnsupported,
                    $"Built-in tool '{tool.ToolCode}' does not match its frozen catalog input/output contract/version.");
            }
        }

        if (tool.ProviderType == ToolProviderType.CloudReadonly)
        {
            var cloudInputSchema = TryParseJson(inputSchema.CanonicalJson);
            var cloudOutputSchema = TryParseJson(outputSchema.CanonicalJson);
            var safety = AiToolSafetyPolicy.Evaluate(
                AiToolExternalSystemType.CloudReadOnly,
                AiToolCapabilityKind.ReadOnlyQuery,
                tool.RiskLevel,
                tool.ToolCode,
                tool.Description,
                readOnlyDeclared: true,
                cloudInputSchema,
                cloudOutputSchema);
            if (!safety.IsAllowed)
            {
                return ToolRegistryDecision.Reject(
                    AppProblemCodes.ToolBlocked,
                    safety.Reason ?? $"Tool '{tool.ToolCode}' violates Cloud read-only policy.");
            }
        }

        if (!string.IsNullOrWhiteSpace(tool.RequiredPermission))
        {
            var access = await identityAccessService.GetCurrentUserAccessAsync(userId, cancellationToken);
            if (access is null || !access.Permissions.Contains(tool.RequiredPermission, StringComparer.Ordinal))
            {
                return ToolRegistryDecision.Reject(
                    AppProblemCodes.ToolPermissionDenied,
                    $"Current user lacks required tool permission '{tool.RequiredPermission}'.");
            }
        }

        return ToolRegistryDecision.Allow(tool);
    }

    public async Task<IReadOnlyDictionary<string, ToolRegistration>> ValidateAllAsync(
        IEnumerable<string?> toolCodes,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ToolRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var toolCode in toolCodes.Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var decision = await ValidateAsync(toolCode, userId, cancellationToken);
            if (!decision.IsAllowed)
            {
                throw new ToolRegistryGuardException(decision.Problem!);
            }

            result[decision.Tool!.ToolCode] = decision.Tool!;
        }

        return result;
    }

    public async Task<IReadOnlyCollection<ToolRegistration>> ListAllowedAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var tools = await toolRepository.ListAsync(cancellationToken: cancellationToken);
        var allowed = new List<ToolRegistration>();
        foreach (var tool in tools.OrderBy(item => item.ToolCode, StringComparer.OrdinalIgnoreCase))
        {
            var decision = await ValidateAsync(tool.ToolCode, userId, cancellationToken);
            if (decision.IsAllowed)
            {
                allowed.Add(decision.Tool!);
            }
            else if (decision.Problem?.Code == AppProblemCodes.PlannerToolSchemaUnsupported)
            {
                throw new ToolRegistryGuardException(decision.Problem);
            }
        }

        return allowed;
    }

    private static JsonElement? TryParseJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class ToolRegistryGuardException(ApiProblemDescriptor problem) : Exception(problem.Detail)
{
    public ApiProblemDescriptor Problem { get; } = problem;
}
