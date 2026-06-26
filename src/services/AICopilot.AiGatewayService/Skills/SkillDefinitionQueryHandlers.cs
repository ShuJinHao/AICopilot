using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Specifications.Skills;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Skills;

public sealed class GetListSkillDefinitionsQueryHandler(
    IReadRepository<SkillDefinition> repository)
    : IQueryHandler<GetListSkillDefinitionsQuery, Result<IReadOnlyCollection<SkillDefinitionDto>>>
{
    public async Task<Result<IReadOnlyCollection<SkillDefinitionDto>>> Handle(
        GetListSkillDefinitionsQuery request,
        CancellationToken cancellationToken)
    {
        var skills = request.EnabledOnly
            ? await repository.ListAsync(new EnabledSkillDefinitionsSpec(), cancellationToken)
            : await repository.ListAsync(cancellationToken: cancellationToken);

        return Result.Success<IReadOnlyCollection<SkillDefinitionDto>>(
            skills
                .OrderByDescending(skill => string.Equals(
                    skill.SkillCode,
                    BuiltInSkillDefinitions.DefaultSkillCode,
                    StringComparison.OrdinalIgnoreCase))
                .ThenBy(skill => skill.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(SkillDefinitionMapper.Map)
                .ToArray());
    }
}
