using AICopilot.AiGatewayService.Queries.ConversationTemplates;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.Commands.Sessions;

public record CreatedSessionDto(
    Guid Id,
    string Title,
    DateTimeOffset? OnsiteConfirmedAt,
    string? OnsiteConfirmedBy,
    DateTimeOffset? OnsiteConfirmationExpiresAt);

[AuthorizeRequirement("AiGateway.CreateSession")]
public record CreateSessionCommand(Guid? TemplateId) : ICommand<Result<CreatedSessionDto>>;

public class CreateSessionCommandHandler(
    IRepository<Session> repo,
    IMediator mediator,
    ICurrentUser user)
    : ICommandHandler<CreateSessionCommand, Result<CreatedSessionDto>>
{
    public async Task<Result<CreatedSessionDto>> Handle(CreateSessionCommand request, CancellationToken ct)
    {
        if (user.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var templateId = request.TemplateId;

        if (templateId == null)
        {
            var template = await mediator.Send(new GetConversationTemplateByNameQuery("GeneralAgent"), ct);
            if (!template.IsSuccess)
            {
                return Result.NotFound();
            }

            templateId = template.Value!.Id;
        }

        var session = new Session(userId, new ConversationTemplateId(templateId.Value));
        repo.Add(session);
        await repo.SaveChangesAsync(ct);

        return Result.Success(new CreatedSessionDto(
            session.Id,
            session.Title,
            session.OnsiteConfirmedAt,
            session.OnsiteConfirmedBy,
            session.OnsiteConfirmationExpiresAt));
    }
}
