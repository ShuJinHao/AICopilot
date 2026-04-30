using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.LanguageModels;

[AuthorizeRequirement("AiGateway.DeleteLanguageModel")]
public record DeleteLanguageModelCommand(Guid Id) : ICommand<Result>;

public class DeleteLanguageModelCommandHandler(
    IRepository<LanguageModel> repo,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<DeleteLanguageModelCommand, Result>
{
    public async Task<Result> Handle(DeleteLanguageModelCommand request, CancellationToken cancellationToken)
    {
        var result = await repo.GetByIdAsync(request.Id, cancellationToken);
        if (result == null)
        {
            return Result.Success();
        }

        var targetName = result.Name;

        repo.Delete(result);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.DeleteLanguageModel",
                "LanguageModel",
                request.Id.ToString(),
                targetName,
                AuditResults.Succeeded,
                $"删除模型配置：{targetName}"),
            cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
