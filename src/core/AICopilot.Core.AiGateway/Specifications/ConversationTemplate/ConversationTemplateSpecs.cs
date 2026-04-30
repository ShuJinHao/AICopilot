using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.ConversationTemplate;

public sealed class ConversationTemplateByIdSpec : Specification<Aggregates.ConversationTemplate.ConversationTemplate>
{
    public ConversationTemplateByIdSpec(Guid id)
    {
        FilterCondition = template => template.Id == id;
    }
}

public sealed class ConversationTemplateByNameSpec : Specification<Aggregates.ConversationTemplate.ConversationTemplate>
{
    public ConversationTemplateByNameSpec(string name)
    {
        FilterCondition = template => template.Name == name;
    }
}

public sealed class ConversationTemplatesOrderedSpec : Specification<Aggregates.ConversationTemplate.ConversationTemplate>
{
    public ConversationTemplatesOrderedSpec()
    {
        SetOrderBy(template => template.Name);
    }
}
