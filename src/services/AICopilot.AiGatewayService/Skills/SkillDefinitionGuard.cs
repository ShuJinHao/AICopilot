using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Specifications.Skills;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Skills;

public sealed record SkillToolFilterResult(
    SkillDefinition? Skill,
    IReadOnlyCollection<ToolRegistration> Tools)
{
    public string? SkillCode => Skill?.SkillCode;
}

public sealed class SkillDefinitionGuard(IReadRepository<SkillDefinition> repository)
{
    public async Task<Result<SkillDefinition?>> ResolveAsync(
        string? skillCode,
        CancellationToken cancellationToken)
    {
        var normalized = string.IsNullOrWhiteSpace(skillCode)
            ? BuiltInSkillDefinitions.DefaultSkillCode
            : skillCode.Trim();
        var skill = await repository.FirstOrDefaultAsync(
            new SkillDefinitionByCodeSpec(normalized),
            cancellationToken);
        if (skill is null && string.IsNullOrWhiteSpace(skillCode))
        {
            return Result.Success<SkillDefinition?>(null);
        }

        if (skill is null || !skill.IsEnabled)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanToolDenied,
                $"Skill '{normalized}' is not enabled or does not exist."));
        }

        return Result.Success<SkillDefinition?>(skill);
    }

    public async Task<Result<SkillToolFilterResult>> FilterToolsAsync(
        IEnumerable<ToolRegistration> tools,
        string? skillCode,
        CancellationToken cancellationToken)
    {
        var skillResult = await ResolveAsync(skillCode, cancellationToken);
        if (!skillResult.IsSuccess)
        {
            return Result.From(skillResult);
        }

        var skill = skillResult.Value;
        if (skill is null)
        {
            return Result.Success(new SkillToolFilterResult(null, tools.ToArray()));
        }

        var filtered = tools
            .Where(tool => skill.AllowsTool(tool.ToolCode))
            .ToArray();
        return Result.Success(new SkillToolFilterResult(skill, filtered));
    }

    public async Task<Result<SkillDefinition?>> ValidateToolAsync(
        string? skillCode,
        string toolCode,
        CancellationToken cancellationToken)
    {
        var skillResult = await ResolveAsync(skillCode, cancellationToken);
        if (!skillResult.IsSuccess)
        {
            return Result.From(skillResult);
        }

        var skill = skillResult.Value;
        if (skill is null || skill.AllowsTool(toolCode))
        {
            return Result.Success(skill);
        }

        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentPlanToolDenied,
            $"Tool '{toolCode}' is outside skill '{skill.SkillCode}'."));
    }
}
