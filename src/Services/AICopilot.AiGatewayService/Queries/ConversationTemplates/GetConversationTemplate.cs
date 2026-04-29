using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Specifications.ConversationTemplate;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.ConversationTemplates;

[AuthorizeRequirement("AiGateway.GetConversationTemplate")]
public record GetConversationTemplateQuery(Guid Id) : IQuery<Result<ConversationTemplateDto>>;

public class GetConversationTemplateQueryHandler(IReadRepository<ConversationTemplate> repository)
    : IQueryHandler<GetConversationTemplateQuery, Result<ConversationTemplateDto>>
{
    public async Task<Result<ConversationTemplateDto>> Handle(
        GetConversationTemplateQuery request,
        CancellationToken cancellationToken)
    {
        var result = await repository.FirstOrDefaultAsync(
            new ConversationTemplateByIdSpec(request.Id),
            cancellationToken);

        return result == null ? Result.NotFound() : Result.Success(ConversationTemplateDtoMapper.Map(result));
    }
}
