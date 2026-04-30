using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.Sessions;

[AuthorizeRequirement("AiGateway.Chat")]
public record UpdateSessionSafetyAttestationCommand(
    Guid SessionId,
    bool IsOnsiteConfirmed,
    int? ExpiresInMinutes) : ICommand<Result<SessionDto>>;

public class UpdateSessionSafetyAttestationCommandHandler(
    IRepository<Session> repository,
    ICurrentUser currentUser,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdateSessionSafetyAttestationCommand, Result<SessionDto>>
{
    private const int DefaultExpirationMinutes = 30;
    private const int MaxExpirationMinutes = 30;

    public async Task<Result<SessionDto>> Handle(
        UpdateSessionSafetyAttestationCommand request,
        CancellationToken cancellationToken)
    {
        var session = await repository.GetByIdAsync(new SessionId(request.SessionId), cancellationToken);
        if (session == null)
        {
            return Result.NotFound();
        }

        if (currentUser.Id != session.UserId)
        {
            return Result.Forbidden();
        }

        if (request.IsOnsiteConfirmed)
        {
            var confirmedAt = DateTimeOffset.UtcNow;
            var ttl = request.ExpiresInMinutes.GetValueOrDefault(DefaultExpirationMinutes);
            ttl = Math.Clamp(ttl <= 0 ? DefaultExpirationMinutes : ttl, 1, MaxExpirationMinutes);
            var expiresAt = confirmedAt.AddMinutes(ttl);
            var operatorName = string.IsNullOrWhiteSpace(currentUser.UserName) ? "Unknown" : currentUser.UserName!;

            session.SetOnsiteAttestation(operatorName, confirmedAt, expiresAt);

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Approval,
                    "AiGateway.SetOnsiteAttestation",
                    "Session",
                    session.Id.ToString(),
                    session.Title,
                    AuditResults.Succeeded,
                    $"会话在岗声明已确认，{operatorName} 设置了 {ttl} 分钟有效期。",
                    ["onsiteConfirmedAt", "onsiteConfirmedBy", "onsiteConfirmationExpiresAt"]),
                cancellationToken);
        }
        else
        {
            session.ClearOnsiteAttestation();

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Approval,
                    "AiGateway.ClearOnsiteAttestation",
                    "Session",
                    session.Id.ToString(),
                    session.Title,
                    AuditResults.Succeeded,
                    "会话在岗声明已清除。",
                    ["onsiteConfirmedAt", "onsiteConfirmedBy", "onsiteConfirmationExpiresAt"]),
                cancellationToken);
        }

        repository.Update(session);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(new SessionDto
        {
            Id = session.Id,
            Title = session.Title,
            OnsiteConfirmedAt = session.OnsiteConfirmedAt,
            OnsiteConfirmedBy = session.OnsiteConfirmedBy,
            OnsiteConfirmationExpiresAt = session.OnsiteConfirmationExpiresAt
        });
    }
}
