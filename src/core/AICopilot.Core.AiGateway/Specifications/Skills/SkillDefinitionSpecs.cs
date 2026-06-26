using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.Skills;

public sealed class SkillDefinitionByCodeSpec : Specification<SkillDefinition>
{
    public SkillDefinitionByCodeSpec(string skillCode)
    {
        var normalized = skillCode.Trim();
        FilterCondition = skill => skill.SkillCode == normalized;
    }
}

public sealed class EnabledSkillDefinitionsSpec : Specification<SkillDefinition>
{
    public EnabledSkillDefinitionsSpec()
    {
        FilterCondition = skill => skill.IsEnabled;
        SetOrderBy(skill => skill.DisplayName);
    }
}
