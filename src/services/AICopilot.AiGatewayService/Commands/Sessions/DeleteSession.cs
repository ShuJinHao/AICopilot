using System;
using System.Threading;
using System.Threading.Tasks;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.Sessions;

[AuthorizeRequirement("AiGateway.DeleteSession")]
public record DeleteSessionCommand(Guid Id) : ICommand<Result>;

public class DeleteSessionCommandHandler(
    IRepository<Session> repo,
    ICurrentUser currentUser,
    IFinalAgentContextStore finalAgentContextStore)
    : ICommandHandler<DeleteSessionCommand, Result>
{
    public async Task<Result> Handle(DeleteSessionCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var result = await repo.FirstOrDefaultAsync(
            new SessionByIdForUserSpec(new SessionId(request.Id), userId),
            cancellationToken);
        if (result == null) return Result.Success();

        repo.Delete(result);
        await repo.SaveChangesAsync(cancellationToken);
        await finalAgentContextStore.RemoveAsync(request.Id, cancellationToken);

        return Result.Success();
    }
}
