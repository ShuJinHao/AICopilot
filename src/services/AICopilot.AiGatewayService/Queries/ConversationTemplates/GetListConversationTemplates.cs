using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Specifications.ConversationTemplate;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.ConversationTemplates;

[AuthorizeRequirement("AiGateway.GetListConversationTemplates")]
public record GetListConversationTemplatesQuery : IQuery<Result<IList<ConversationTemplateDto>>>;

public class GetListConversationTemplatesQueryHandler(IReadRepository<ConversationTemplate> repository)
    : IQueryHandler<GetListConversationTemplatesQuery, Result<IList<ConversationTemplateDto>>>
{
    public async Task<Result<IList<ConversationTemplateDto>>> Handle(
        GetListConversationTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        var templates = await repository.ListAsync(new ConversationTemplatesOrderedSpec(), cancellationToken);
        IList<ConversationTemplateDto> result = templates.Select(ConversationTemplateDtoMapper.Map).ToList();
        return Result.Success(result);
    }
}
