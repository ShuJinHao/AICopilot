using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Specifications.ConversationTemplate;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.ConversationTemplates;

[AuthorizeRequirement("AiGateway.GetConversationTemplateByName")]
public record GetConversationTemplateByNameQuery(string Name) : IQuery<Result<ConversationTemplateDto>>;

public class GetConversationTemplateByNameQueryHandler(IReadRepository<ConversationTemplate> repository)
    : IQueryHandler<GetConversationTemplateByNameQuery, Result<ConversationTemplateDto>>
{
    public async Task<Result<ConversationTemplateDto>> Handle(
        GetConversationTemplateByNameQuery request,
        CancellationToken cancellationToken)
    {
        var result = await repository.FirstOrDefaultAsync(
            new ConversationTemplateByNameSpec(request.Name),
            cancellationToken);

        return result == null ? Result.NotFound() : Result.Success(ConversationTemplateDtoMapper.Map(result));
    }
}
