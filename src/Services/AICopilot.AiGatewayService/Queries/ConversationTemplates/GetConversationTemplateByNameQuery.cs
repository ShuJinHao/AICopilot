using AICopilot.Services.Common.Attributes;
using AICopilot.Services.Common.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.AiGatewayService.Queries.ConversationTemplates;

[AuthorizeRequirement("AiGateway.GetConversationTemplateByName")]
public record GetConversationTemplateByNameQuery(string Name) : IQuery<Result<ConversationTemplateDto>>;

public class GetConversationTemplateByNameQueryHandler(
    IDataQueryService dataQueryService) : IQueryHandler<GetConversationTemplateByNameQuery, Result<ConversationTemplateDto>>
{
    public async Task<Result<ConversationTemplateDto>> Handle(GetConversationTemplateByNameQuery request,
        CancellationToken cancellationToken)
    {
        var queryable = dataQueryService.ConversationTemplates
            .Where(template => template.Name == request.Name)
            .Select(ct => new ConversationTemplateDto
            {
                Id = ct.Id,
                Name = ct.Name,
                Description = ct.Description,
                SystemPrompt = ct.SystemPrompt,
                MaxTokens = ct.Specification.MaxTokens,
                Temperature = ct.Specification.Temperature
            });
        var result = await dataQueryService.FirstOrDefaultAsync(queryable);

        return result == null ? Result.NotFound() : Result.Success(result);
    }
}