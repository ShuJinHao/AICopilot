using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.Queries.ConversationTemplates;

internal static class ConversationTemplateDtoMapper
{
    public static ConversationTemplateDto Map(ConversationTemplate template)
    {
        return new ConversationTemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Description = template.Description,
            SystemPrompt = template.SystemPrompt,
            ModelId = template.ModelId,
            MaxTokens = template.Specification.MaxTokens,
            Temperature = template.Specification.Temperature,
            IsEnabled = template.IsEnabled
        };
    }
}
