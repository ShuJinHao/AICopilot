using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.ConversationTemplates;

[AuthorizeRequirement("AiGateway.DeleteConversationTemplate")]
public record DeleteConversationTemplateCommand(Guid Id) : ICommand<Result>;

public class DeleteConversationTemplateCommandHandler(
    IRepository<ConversationTemplate> modelRepo,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<DeleteConversationTemplateCommand, Result>
{
    public async Task<Result> Handle(DeleteConversationTemplateCommand request, CancellationToken cancellationToken)
    {
        var model = await modelRepo.GetByIdAsync(new ConversationTemplateId(request.Id), cancellationToken);
        if (model == null)
        {
            return Result.Success();
        }

        var targetName = model.Name;

        modelRepo.Delete(model);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.DeleteConversationTemplate",
                "ConversationTemplate",
                request.Id.ToString(),
                targetName,
                AuditResults.Succeeded,
                $"删除会话模板：{targetName}"),
            cancellationToken);
        await modelRepo.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
