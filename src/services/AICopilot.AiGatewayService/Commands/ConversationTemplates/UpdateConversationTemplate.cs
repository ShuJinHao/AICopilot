using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.ConversationTemplates;

[AuthorizeRequirement("AiGateway.UpdateConversationTemplate")]
public record UpdateConversationTemplateCommand(
    Guid Id,
    string Name,
    string Description,
    string SystemPrompt,
    Guid ModelId,
    int? MaxTokens,
    float? Temperature,
    bool IsEnabled) : ICommand<Result>;

public class UpdateConversationTemplateCommandHandler(
    IRepository<ConversationTemplate> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdateConversationTemplateCommand, Result>
{
    public async Task<Result> Handle(UpdateConversationTemplateCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity == null)
        {
            return Result.NotFound();
        }

        var changedFields = new List<string>();

        if (!string.Equals(entity.Name, request.Name, StringComparison.Ordinal))
        {
            changedFields.Add("name");
        }

        if (!string.Equals(entity.Description, request.Description, StringComparison.Ordinal))
        {
            changedFields.Add("description");
        }

        if (!string.Equals(entity.SystemPrompt, request.SystemPrompt, StringComparison.Ordinal))
        {
            changedFields.Add("systemPrompt");
        }

        if (entity.ModelId != request.ModelId)
        {
            changedFields.Add("modelId");
        }

        if (entity.Specification.MaxTokens != request.MaxTokens)
        {
            changedFields.Add("maxTokens");
        }

        if (entity.Specification.Temperature != request.Temperature)
        {
            changedFields.Add("temperature");
        }

        if (entity.IsEnabled != request.IsEnabled)
        {
            changedFields.Add("isEnabled");
        }

        entity.UpdateInfo(request.Name, request.Description, request.SystemPrompt, request.ModelId, request.IsEnabled);
        entity.UpdateSpecification(new TemplateSpecification
        {
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature
        });

        repository.Update(entity);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.UpdateConversationTemplate",
                "ConversationTemplate",
                entity.Id.ToString(),
                entity.Name,
                AuditResults.Succeeded,
                $"更新会话模板：{entity.Name}，已修改 {(changedFields.Count == 0 ? "未修改业务字段" : string.Join("、", changedFields))}",
                changedFields),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
