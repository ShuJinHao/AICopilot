using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.ConversationTemplates;

public record CreatedConversationTemplateDto(Guid Id, string Name);

[AuthorizeRequirement("AiGateway.CreateConversationTemplate")]
public record CreateConversationTemplateCommand(
    string Name,
    string Description,
    string SystemPrompt,
    Guid ModelId,
    int? MaxTokens,
    float? Temperature) : ICommand<Result<CreatedConversationTemplateDto>>;

public class CreateConversationTemplateCommandHandler(
    IRepository<ConversationTemplate> repo,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreateConversationTemplateCommand, Result<CreatedConversationTemplateDto>>
{
    public async Task<Result<CreatedConversationTemplateDto>> Handle(
        CreateConversationTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var model = new ConversationTemplate(
            request.Name,
            request.Description,
            request.SystemPrompt,
            request.ModelId,
            new TemplateSpecification
            {
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature
            });

        repo.Add(model);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.CreateConversationTemplate",
                "ConversationTemplate",
                model.Id.ToString(),
                model.Name,
                AuditResults.Succeeded,
                $"创建会话模板：{model.Name}",
                ["name", "description", "systemPrompt", "modelId", "maxTokens", "temperature", "isEnabled"]),
            cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreatedConversationTemplateDto(model.Id, model.Name));
    }
}
