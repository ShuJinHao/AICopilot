using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Safety;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.Sessions;

public sealed record UpdateAgentSessionModeRequest(string Mode, long ExpectedVersion);

public sealed record AgentSessionModeDto(Guid SessionId, string Mode, long Version);

[AuthorizeRequirement("AiGateway.Chat")]
public sealed record UpdateAgentSessionModeCommand(
    Guid SessionId,
    string Mode,
    long ExpectedVersion) : ICommand<Result<AgentSessionModeDto>>;

public sealed class UpdateAgentSessionModeCommandHandler(
    IReadRepository<Session> sessionRepository,
    ICurrentUser currentUser,
    ISessionExecutionLock sessionExecutionLock,
    IAgentSessionStateStore agentSessionStateStore,
    ConfiguredAgentRuntimeFactory configuredAgentRuntimeFactory)
    : ICommandHandler<UpdateAgentSessionModeCommand, Result<AgentSessionModeDto>>
{
    private static readonly JsonSerializerOptions AgentSessionJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<Result<AgentSessionModeDto>> Handle(
        UpdateAgentSessionModeCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        if (request.SessionId == Guid.Empty ||
            request.ExpectedVersion <= 0 ||
            !TryParseMode(request.Mode, out var requestedMode))
        {
            return Result.Invalid(new ApiProblemDescriptor(
                AppProblemCodes.RequestValidationFailed,
                "Session id, expectedVersion and mode ('plan' or 'execute') are required."));
        }

        var session = await sessionRepository.FirstOrDefaultAsync(
            new SessionByIdForUserSpec(new SessionId(request.SessionId), userId),
            cancellationToken);
        if (session is null)
        {
            return Result.NotFound();
        }

        await using var sessionLock = await sessionExecutionLock.AcquireAsync(
            request.SessionId,
            cancellationToken);

        AgentSessionStateSnapshot state;
        try
        {
            state = await agentSessionStateStore.LoadOwnedAsync(
                request.SessionId,
                userId,
                currentUser.CloudTenantId,
                cancellationToken);
        }
        catch (AgentSessionStateException exception)
        {
            return MapStateFailure(exception);
        }

        if (state.Status == AgentSessionRuntimeStatus.Interrupted)
        {
            return Result.Invalid(new ApiProblemDescriptor(
                AppProblemCodes.AgentSessionInterrupted,
                "The AgentSession is interrupted and cannot change mode."));
        }

        if (state.Status == AgentSessionRuntimeStatus.Running ||
            state.Version != request.ExpectedVersion)
        {
            return Result.Conflict(new ApiProblemDescriptor(
                AppProblemCodes.AgentSessionVersionConflict,
                "The AgentSession version changed or an agent turn is active."));
        }

        if (state.PendingApprovals.Count > 0)
        {
            return Result.Conflict(new ApiProblemDescriptor(
                AppProblemCodes.ApprovalPending,
                "Agent mode cannot change while approval is pending."));
        }

        try
        {
            await using var scopedRuntime =
                await configuredAgentRuntimeFactory.CreateHarnessAgentAsync(
                    new ConversationTemplateId(session.TemplateId),
                    [],
                    checkpointSink: null,
                    cancellationToken);
            var harnessAgent = scopedRuntime.Agent as IHarnessRuntimeChatAgent
                ?? throw new InvalidOperationException(
                    "Agent mode mutation did not create a Harness agent.");
            var harnessSession = await harnessAgent.DeserializeSessionAsync(
                state.SerializedSessionState,
                AgentSessionJsonOptions,
                cancellationToken);

            await harnessAgent.SetModeAsync(
                harnessSession,
                requestedMode,
                cancellationToken);
            var serialized = await harnessAgent.SerializeSessionAsync(
                harnessSession,
                AgentSessionJsonOptions,
                cancellationToken);
            var updated = await agentSessionStateStore.PersistModeChangeAsync(
                request.SessionId,
                userId,
                currentUser.CloudTenantId,
                request.ExpectedVersion,
                serialized,
                cancellationToken);

            return Result.Success(new AgentSessionModeDto(
                request.SessionId,
                FormatMode(requestedMode),
                updated.Version));
        }
        catch (AgentSessionStateException exception)
        {
            return MapStateFailure(exception);
        }
        catch (AgentWorkflowException exception)
        {
            return Result.Invalid(new ApiProblemDescriptor(
                exception.Code,
                exception.UserFacingMessage));
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            return Result.Invalid(new ApiProblemDescriptor(
                AppProblemCodes.AgentSessionResetRequired,
                "The persisted AgentSession cannot be restored safely."));
        }
    }

    private static Result MapStateFailure(AgentSessionStateException exception)
    {
        return exception.Failure switch
        {
            AgentSessionStateFailure.OwnershipMismatch =>
                Result.NotFound(),
            AgentSessionStateFailure.AlreadyRunning or
                AgentSessionStateFailure.VersionConflict =>
                Result.Conflict(new ApiProblemDescriptor(
                    AppProblemCodes.AgentSessionVersionConflict,
                    "The AgentSession version changed or an agent turn is active.")),
            AgentSessionStateFailure.ApprovalPending =>
                Result.Conflict(new ApiProblemDescriptor(
                    AppProblemCodes.ApprovalPending,
                    "Agent mode cannot change while approval is pending.")),
            AgentSessionStateFailure.Interrupted =>
                Result.Invalid(new ApiProblemDescriptor(
                    AppProblemCodes.AgentSessionInterrupted,
                    "The AgentSession is interrupted and cannot change mode.")),
            AgentSessionStateFailure.Missing or
                AgentSessionStateFailure.SchemaMismatch or
                AgentSessionStateFailure.Corrupt or
                AgentSessionStateFailure.Oversize or
                AgentSessionStateFailure.Expired =>
                Result.Invalid(new ApiProblemDescriptor(
                    AppProblemCodes.AgentSessionResetRequired,
                    "The persisted AgentSession cannot be restored safely.")),
            _ => Result.Invalid(new ApiProblemDescriptor(
                AppProblemCodes.ChatStreamFailed,
                "The AgentSession mode transition was rejected."))
        };
    }

    private static bool TryParseMode(string? mode, out RuntimeAgentMode result)
    {
        if (string.Equals(mode?.Trim(), "plan", StringComparison.OrdinalIgnoreCase))
        {
            result = RuntimeAgentMode.Plan;
            return true;
        }

        if (string.Equals(mode?.Trim(), "execute", StringComparison.OrdinalIgnoreCase))
        {
            result = RuntimeAgentMode.Execute;
            return true;
        }

        result = default;
        return false;
    }

    private static string FormatMode(RuntimeAgentMode mode) =>
        mode == RuntimeAgentMode.Execute ? "execute" : "plan";
}
