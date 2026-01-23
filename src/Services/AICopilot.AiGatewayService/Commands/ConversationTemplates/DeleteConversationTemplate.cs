using System;
using System.Threading;
using System.Threading.Tasks;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Services.Common.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.ConversationTemplates;

[AuthorizeRequirement("AiGateway.DeleteConversationTemplate")]
public record DeleteConversationTemplateCommand(Guid Id) : ICommand<Result>;

public class DeleteConversationTemplateCommandHandler(IRepository<ConversationTemplate> modelRepo)
    : ICommandHandler<DeleteConversationTemplateCommand, Result>
{
    public async Task<Result> Handle(DeleteConversationTemplateCommand request, CancellationToken cancellationToken)
    {
        var model = await modelRepo.GetByIdAsync(request.Id, cancellationToken);
        if (model == null) return Result.Success();

        modelRepo.Delete(model);
        await modelRepo.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}