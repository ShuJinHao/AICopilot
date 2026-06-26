using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Skills;

public sealed record SkillDefinitionDto(
    Guid Id,
    string SkillCode,
    string DisplayName,
    string Description,
    IReadOnlyCollection<string> AllowedToolCodes,
    string RiskLevel,
    string ApprovalPolicy,
    IReadOnlyCollection<string> AllowedDataSourceModes,
    IReadOnlyCollection<string> AllowedKnowledgeScopes,
    IReadOnlyCollection<string> OutputComponentTypes,
    bool IsEnabled,
    bool IsBuiltIn,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

[AuthorizeRequirement("AiGateway.PlanAgentTask")]
public sealed record GetListSkillDefinitionsQuery(bool EnabledOnly = true)
    : IQuery<Result<IReadOnlyCollection<SkillDefinitionDto>>>;

internal static class SkillDefinitionMapper
{
    public static SkillDefinitionDto Map(SkillDefinition skill)
    {
        return new SkillDefinitionDto(
            skill.Id.Value,
            skill.SkillCode,
            skill.DisplayName,
            skill.Description,
            skill.AllowedToolCodes,
            skill.RiskLevel.ToString(),
            skill.ApprovalPolicy,
            skill.AllowedDataSourceModes,
            skill.AllowedKnowledgeScopes,
            skill.OutputComponentTypes,
            skill.IsEnabled,
            skill.IsBuiltIn,
            skill.Version,
            skill.CreatedAt,
            skill.UpdatedAt);
    }
}
