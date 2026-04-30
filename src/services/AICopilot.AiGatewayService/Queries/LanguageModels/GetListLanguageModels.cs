using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.LanguageModels;

[AuthorizeRequirement("AiGateway.GetListLanguageModels")]
public record GetListLanguageModelsQuery : IQuery<Result<IList<LanguageModelDto>>>;

public class GetListLanguageModelsQueryHandler(IReadRepository<LanguageModel> repository)
    : IQueryHandler<GetListLanguageModelsQuery, Result<IList<LanguageModelDto>>>
{
    public async Task<Result<IList<LanguageModelDto>>> Handle(
        GetListLanguageModelsQuery request,
        CancellationToken cancellationToken)
    {
        var models = await repository.ListAsync(cancellationToken: cancellationToken);
        IList<LanguageModelDto> result = models
            .OrderBy(model => model.Provider)
            .ThenBy(model => model.Name)
            .Select(LanguageModelDtoMapper.Map)
            .ToList();

        return Result.Success(result);
    }
}
