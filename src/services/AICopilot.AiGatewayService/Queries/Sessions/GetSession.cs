using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Safety;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Sessions;

[AuthorizeRequirement("AiGateway.GetSession")]
public record GetSessionQuery(Guid Id) : IQuery<Result<SessionDto>>;

public class GetSessionQueryHandler(
    IReadRepository<Session> repository,
    ICurrentUser currentUser,
    IAgentSessionStateStore agentSessionStateStore,
    ConfiguredAgentRuntimeFactory configuredAgentRuntimeFactory)
    : IQueryHandler<GetSessionQuery, Result<SessionDto>>
{
    private static readonly JsonSerializerOptions AgentSessionJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<Result<SessionDto>> Handle(GetSessionQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var result = await repository.FirstOrDefaultAsync(
            new SessionByIdForUserSpec(new SessionId(request.Id), userId),
            cancellationToken);
        if (result is null)
        {
            return Result.NotFound();
        }

        var dto = SessionDtoMapper.Map(result);
        AgentSessionStateSnapshot state;
        try
        {
            state = await agentSessionStateStore.LoadOwnedAsync(
                request.Id,
                userId,
                currentUser.CloudTenantId,
                cancellationToken);
        }
        catch (AgentSessionStateException exception) when (
            exception.Failure is AgentSessionStateFailure.Missing or
                AgentSessionStateFailure.SchemaMismatch or
                AgentSessionStateFailure.Corrupt or
                AgentSessionStateFailure.Oversize or
                AgentSessionStateFailure.Expired)
        {
            dto.AgentSessionStatus = "ResetRequired";
            dto.AgentSessionResetRequired = true;
            return Result.Success(dto);
        }
        catch (AgentSessionStateException exception) when (
            exception.Failure == AgentSessionStateFailure.OwnershipMismatch)
        {
            return Result.NotFound();
        }

        dto.AgentSessionVersion = state.Version;
        dto.AgentSessionStatus = state.Status.ToString();
        dto.HasPendingApproval = state.PendingApprovals.Count > 0;
        if (state.Status == AgentSessionRuntimeStatus.Interrupted)
        {
            return Result.Success(dto);
        }

        try
        {
            await using var runtime =
                await configuredAgentRuntimeFactory.CreateHarnessAgentAsync(
                    result.TemplateId,
                    [],
                    cancellationToken: cancellationToken);
            var harnessAgent = runtime.Agent as IHarnessRuntimeChatAgent
                ?? throw new InvalidOperationException(
                    "Session projection did not create a Harness agent.");
            var harnessSession = await harnessAgent.DeserializeSessionAsync(
                state.SerializedSessionState,
                AgentSessionJsonOptions,
                cancellationToken);
            var mode = await harnessAgent.GetModeAsync(harnessSession, cancellationToken);
            dto.AgentMode = mode == RuntimeAgentMode.Execute ? "execute" : "plan";
        }
        catch (AgentWorkflowException)
        {
            // A temporarily unavailable model provider must not make a valid,
            // restorable AgentSession look corrupt to the client.
            dto.AgentMode = null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            dto.AgentSessionStatus = "ResetRequired";
            dto.AgentSessionResetRequired = true;
            dto.AgentMode = null;
        }

        return Result.Success(dto);
    }
}
