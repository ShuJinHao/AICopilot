using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.LanguageModel;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.LanguageModels;

[AuthorizeRequirement("AiGateway.GetLanguageModel")]
public record GetLanguageModelQuery(Guid Id) : IQuery<Result<LanguageModelDto>>;

public class GetLanguageModelQueryHandler(IReadRepository<LanguageModel> repository)
    : IQueryHandler<GetLanguageModelQuery, Result<LanguageModelDto>>
{
    public async Task<Result<LanguageModelDto>> Handle(GetLanguageModelQuery request, CancellationToken cancellationToken)
    {
        var result = await repository.FirstOrDefaultAsync(
            new LanguageModelByIdSpec(new LanguageModelId(request.Id)),
            cancellationToken);

        return result == null ? Result.NotFound() : Result.Success(LanguageModelDtoMapper.Map(result));
    }
}
