using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.Sessions;

[AuthorizeRequirement("AiGateway.RenameSession")]
public sealed record RenameSessionCommand(Guid Id, string Title) : ICommand<Result>;

public sealed class RenameSessionCommandHandler(
    IRepository<Session> repository,
    ICurrentUser currentUser)
    : ICommandHandler<RenameSessionCommand, Result>
{
    public async Task<Result> Handle(RenameSessionCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        if (request.Id == Guid.Empty)
        {
            return Result.Invalid("Session id is required.");
        }

        var sessionId = new SessionId(request.Id);
        var session = await repository.GetAsync(
            entity => entity.Id == sessionId && entity.UserId == userId,
            cancellationToken: cancellationToken);
        if (session is null)
        {
            return Result.NotFound();
        }

        session.Rename(request.Title);
        repository.Update(session);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
