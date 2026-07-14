using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.Core.AiGateway.Specifications.RuntimeSettings;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Runtime;

public sealed record ChatRuntimeSettingsDto(
    int RoutingHistoryCount,
    int AnswerHistoryCount,
    int RagRewriteHistoryCount,
    int AgentPlanningHistoryCount,
    int ContextTokenLimit);

[AuthorizeRequirement("AiGateway.GetRuntimeSettings")]
public sealed record GetChatRuntimeSettingsQuery : IQuery<Result<ChatRuntimeSettingsDto>>;

[AuthorizeRequirement("AiGateway.UpdateRuntimeSettings")]
public sealed record UpdateChatRuntimeSettingsCommand(
    int RoutingHistoryCount,
    int AnswerHistoryCount,
    int RagRewriteHistoryCount,
    int AgentPlanningHistoryCount,
    int ContextTokenLimit) : ICommand<Result<ChatRuntimeSettingsDto>>;

public interface IAgentRuntimeSettingsProvider
{
    Task<ChatRuntimeSettingsDto> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class AgentRuntimeSettingsProvider(IRepository<ChatRuntimeSettings> repository)
    : IAgentRuntimeSettingsProvider
{
    public async Task<ChatRuntimeSettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await repository.FirstOrDefaultAsync(new GlobalChatRuntimeSettingsSpec(), cancellationToken);
        if (settings is not null)
        {
            return Map(settings);
        }

        return Map(ChatRuntimeSettings.CreateDefault(DateTimeOffset.UtcNow));
    }

    public static ChatRuntimeSettingsDto Map(ChatRuntimeSettings settings)
    {
        return new ChatRuntimeSettingsDto(
            settings.RoutingHistoryCount,
            settings.AnswerHistoryCount,
            settings.RagRewriteHistoryCount,
            settings.AgentPlanningHistoryCount,
            settings.ContextTokenLimit);
    }
}

public sealed class GetChatRuntimeSettingsQueryHandler(IAgentRuntimeSettingsProvider provider)
    : IQueryHandler<GetChatRuntimeSettingsQuery, Result<ChatRuntimeSettingsDto>>
{
    public async Task<Result<ChatRuntimeSettingsDto>> Handle(
        GetChatRuntimeSettingsQuery request,
        CancellationToken cancellationToken)
    {
        return Result.Success(await provider.GetAsync(cancellationToken));
    }
}

public sealed class UpdateChatRuntimeSettingsCommandHandler(IRepository<ChatRuntimeSettings> repository)
    : ICommandHandler<UpdateChatRuntimeSettingsCommand, Result<ChatRuntimeSettingsDto>>
{
    public async Task<Result<ChatRuntimeSettingsDto>> Handle(
        UpdateChatRuntimeSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var settings = await repository.FirstOrDefaultAsync(new GlobalChatRuntimeSettingsSpec(), cancellationToken);
        if (settings is null)
        {
            settings = new ChatRuntimeSettings(
                request.RoutingHistoryCount,
                request.AnswerHistoryCount,
                request.RagRewriteHistoryCount,
                request.AgentPlanningHistoryCount,
                request.ContextTokenLimit,
                now);
            repository.Add(settings);
        }
        else
        {
            settings.Update(
                request.RoutingHistoryCount,
                request.AnswerHistoryCount,
                request.RagRewriteHistoryCount,
                request.AgentPlanningHistoryCount,
                request.ContextTokenLimit,
                now);
            repository.Update(settings);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(AgentRuntimeSettingsProvider.Map(settings));
    }
}
