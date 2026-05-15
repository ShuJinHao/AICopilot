using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.LanguageModels;

[AuthorizeRequirement("AiGateway.Chat")]
public record GetSelectableChatModelsQuery : IQuery<Result<IList<SelectableChatModelDto>>>;

public class GetSelectableChatModelsQueryHandler(IReadRepository<LanguageModel> repository)
    : IQueryHandler<GetSelectableChatModelsQuery, Result<IList<SelectableChatModelDto>>>
{
    public async Task<Result<IList<SelectableChatModelDto>>> Handle(
        GetSelectableChatModelsQuery request,
        CancellationToken cancellationToken)
    {
        var models = await repository.ListAsync(cancellationToken: cancellationToken);
        IList<SelectableChatModelDto> result = models
            .Where(model => model.IsEnabled && model.SupportsUsage(LanguageModelUsage.Chat))
            .OrderBy(model => model.Provider)
            .ThenBy(model => model.Name)
            .Select(model => new SelectableChatModelDto
            {
                Id = model.Id,
                Provider = model.Provider,
                ProtocolType = model.ProtocolType,
                Name = model.Name,
                ContextWindowTokens = model.Parameters.MaxTokens,
                MaxOutputTokens = model.Parameters.MaxOutputTokens
            })
            .ToList();

        return Result.Success(result);
    }
}
