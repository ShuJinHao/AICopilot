using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Skills;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentPlanToolGuard(
    ToolRegistryGuard toolRegistryGuard,
    IAgentPluginCatalog pluginCatalog,
    SkillDefinitionGuard? skillDefinitionGuard = null,
    IOptions<MockMcpOptions>? mockMcpOptions = null,
    IHostEnvironment? hostEnvironment = null)
{
    public async Task<Result<PlannerToolCatalog>> GetAvailableToolCatalogAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await GetAvailableToolCatalogAsync(
            userId,
            simulationOnly: false,
            businessDomains: null,
            cancellationToken);
    }

    public async Task<Result<PlannerToolCatalog>> GetAvailableToolCatalogAsync(
        Guid userId,
        bool simulationOnly,
        IReadOnlyCollection<string>? businessDomains,
        CancellationToken cancellationToken,
        string? skillCode = null)
    {
        var runtimeMcpTools = ResolveRuntimeMcpToolCodes();
        var tools = await toolRegistryGuard.ListAllowedAsync(userId, cancellationToken);
        var filtered = tools
            .Where(tool => IsPlannerVisible(tool, simulationOnly, businessDomains))
            .ToArray();
        if (skillDefinitionGuard is not null)
        {
            var skillFiltered = await skillDefinitionGuard.FilterToolsAsync(
                filtered,
                skillCode,
                cancellationToken);
            if (!skillFiltered.IsSuccess)
            {
                return Result.From(skillFiltered);
            }

            filtered = skillFiltered.Value!.Tools.ToArray();
        }

        return PlannerToolCatalogBuilder.Build(filtered, runtimeMcpTools);
    }

    public async Task<IReadOnlyCollection<AgentPlannerToolSummary>> GetAvailableToolSummariesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var catalog = await GetAvailableToolCatalogAsync(userId, cancellationToken);
        if (!catalog.IsSuccess)
        {
            throw new ToolRegistryGuardException(catalog.Errors?.OfType<ApiProblemDescriptor>().FirstOrDefault()
                                                 ?? new ApiProblemDescriptor(
                                                     AppProblemCodes.PlannerToolSchemaUnsupported,
                                                     "Planner tool catalog could not be built."));
        }

        return catalog.Value!.Tools;
    }

    public async Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> ValidateStepsAsync(
        IEnumerable<AgentStepPlanDto> steps,
        AgentTaskType taskType,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await ValidateStepsAsync(
            steps,
            taskType,
            userId,
            simulationOnly: false,
            businessDomains: null,
            cancellationToken);
    }

    public async Task<Result<IReadOnlyCollection<AgentStepPlanDto>>> ValidateStepsAsync(
        IEnumerable<AgentStepPlanDto> steps,
        AgentTaskType taskType,
        Guid userId,
        bool simulationOnly,
        IReadOnlyCollection<string>? businessDomains,
        CancellationToken cancellationToken,
        string? skillCode = null)
    {
        var runtimeMcpTools = ResolveRuntimeMcpToolCodes();
        var validated = new List<AgentStepPlanDto>();
        foreach (var step in steps)
        {
            var unsafeProblem = ValidateStepText(step);
            if (unsafeProblem is not null)
            {
                return Result.Failure(unsafeProblem);
            }

            var decision = await toolRegistryGuard.ValidateAsync(
                step.ToolCode,
                userId,
                cancellationToken);
            if (!decision.IsAllowed)
            {
                return Result.Failure(decision.Problem!);
            }

            var tool = decision.Tool!;
            if (!tool.IsExecutableByAgent || !tool.IsVisibleToPlanner)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentPlanToolDenied,
                    $"Tool '{tool.ToolCode}' is not available for planner and agent execution."));
            }

            if (skillDefinitionGuard is not null)
            {
                var skillDecision = await skillDefinitionGuard.ValidateToolAsync(
                    skillCode,
                    tool.ToolCode,
                    cancellationToken);
                if (!skillDecision.IsSuccess)
                {
                    return Result.From(skillDecision);
                }
            }

            if (simulationOnly && !IsPlannerVisible(tool, simulationOnly, businessDomains))
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentPlanToolDenied,
                    $"Tool '{tool.ToolCode}' is outside the SimulationBusiness tool boundary."));
            }

            if (tool.ProviderType == ToolProviderType.CloudReadonly)
            {
                if (taskType != AgentTaskType.CloudDataReport ||
                    CloudReadonlyAgentTextGuard.ContainsForbiddenWriteSemantic(step.Title) ||
                    CloudReadonlyAgentTextGuard.ContainsForbiddenWriteSemantic(step.Description) ||
                    CloudReadonlyAgentTextGuard.ContainsForbiddenWriteSemantic(step.InputJson) ||
                    CloudReadonlyAgentTextGuard.ContainsUnsafePersistedPayload(step.InputJson))
                {
                    return Result.Failure(new ApiProblemDescriptor(
                        AppProblemCodes.AgentPlanToolDenied,
                        $"Cloud readonly tool '{tool.ToolCode}' is not allowed for this plan."));
                }
            }

            if (tool.ProviderType == ToolProviderType.Mcp && !runtimeMcpTools.Contains(tool.ToolCode))
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentPlanToolDenied,
                    $"MCP tool '{tool.ToolCode}' is not available in the current runtime."));
            }

            if (tool.ProviderType == ToolProviderType.MockMcp && !IsMockMcpRuntimeAvailable())
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentPlanToolDenied,
                    $"Mock MCP tool '{tool.ToolCode}' is not available in the current runtime."));
            }

            var schemaValidation = ToolInputSchemaValidator.ValidateAndParse(step.InputJson, tool.InputSchemaJson);
            if (!schemaValidation.IsValid)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentPlanSchemaInvalid,
                    schemaValidation.Error ?? $"Tool '{tool.ToolCode}' input does not match registry schema."));
            }

            validated.Add(step with
            {
                RequiresApproval = step.RequiresApproval ||
                                   tool.RequiresApproval ||
                                   tool.RiskLevel is AiToolRiskLevel.RequiresApproval
                                       or AiToolRiskLevel.High
                                       or AiToolRiskLevel.Critical
            });
        }

        if (validated.Count == 0)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Agent plan must contain at least one step."));
        }

        return Result.Success<IReadOnlyCollection<AgentStepPlanDto>>(validated);
    }

    private HashSet<string> ResolveRuntimeMcpToolCodes()
    {
        return pluginCatalog.GetAllTools()
            .Where(tool => tool.TargetType == AiToolTargetType.McpServer)
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool IsPlannerVisible(
        ToolRegistration tool,
        bool simulationOnly,
        IReadOnlyCollection<string>? businessDomains)
    {
        if (!tool.IsEnabled ||
            !tool.IsVisibleToPlanner ||
            !tool.IsExecutableByAgent ||
            tool.RiskLevel is AiToolRiskLevel.Blocked or AiToolRiskLevel.Critical)
        {
            return false;
        }

        if (tool.ProviderType == ToolProviderType.MockMcp && !IsMockMcpRuntimeAvailable())
        {
            return false;
        }

        if (simulationOnly)
        {
            if (tool.ProviderType is ToolProviderType.CloudReadonly or ToolProviderType.Mcp)
            {
                return false;
            }

            if (tool.DataBoundary is not (ToolDataBoundary.NoData
                or ToolDataBoundary.SimulationBusinessOnly
                or ToolDataBoundary.RagContextOnly
                or ToolDataBoundary.ArtifactDraftOnly))
            {
                return false;
            }
        }

        var requestedDomains = (businessDomains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .ToArray();
        if (requestedDomains.Length == 0 || tool.BusinessDomains.Length == 0)
        {
            return true;
        }

        return tool.BusinessDomains.Any(domain =>
            requestedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase));
    }

    private bool IsMockMcpRuntimeAvailable()
    {
        return hostEnvironment?.IsDevelopment() == true &&
               mockMcpOptions?.Value.Enabled == true;
    }

    private static ApiProblemDescriptor? ValidateStepText(AgentStepPlanDto step)
    {
        var combined = string.Join('\n', step.Title, step.Description, step.ToolCode, step.InputJson);
        if (ContainsShellOrPathSemantic(combined))
        {
            return new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanToolDenied,
                "Agent plan contains shell or arbitrary path semantics.");
        }

        if (ContainsSqlStatementSemantic(combined))
        {
            return new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanToolDenied,
                "Agent plan contains SQL statement semantics.");
        }

        return null;
    }

    private static bool ContainsShellOrPathSemantic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("..\\", StringComparison.Ordinal) ||
               value.Contains("../", StringComparison.Ordinal) ||
               value.Contains("/etc/", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("/var/", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("C:\\", StringComparison.OrdinalIgnoreCase) ||
               ContainsToken(value, "shell") ||
               ContainsToken(value, "powershell") ||
               ContainsToken(value, "cmd") ||
               ContainsToken(value, "bash") ||
               ContainsToken(value, "terminal") ||
               ContainsToken(value, "exec") ||
               ContainsToken(value, "process") ||
               ContainsToken(value, "run_command") ||
               value.Contains("任意路径", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsToken(string value, string token)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            value,
            $@"(?i)(^|[^\p{{L}}\p{{N}}_]){System.Text.RegularExpressions.Regex.Escape(token)}([^\p{{L}}\p{{N}}_]|$)");
    }

    private static bool ContainsSqlStatementSemantic(string value)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
                   value,
                   @"(?is)\bselect\b.+\bfrom\b") ||
               System.Text.RegularExpressions.Regex.IsMatch(
                   value,
                   @"(?i)\b(insert\s+into|update\s+\w+|delete\s+from|drop\s+(table|view|database)|alter\s+(table|view)|create\s+(table|view|database)|truncate\s+table|merge\s+into)\b");
    }
}
