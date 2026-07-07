using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Sessions;

public sealed record SessionTimelinePageDto(
    IReadOnlyList<SessionTimelineEventDto> Items,
    int? BeforeSequence,
    int? AfterSequence,
    bool HasMore,
    bool HasMoreBefore,
    bool HasMoreAfter);

public sealed record SessionTimelineEventDto(
    int Sequence,
    string EventType,
    DateTimeOffset CreatedAt,
    int? MessageId,
    Guid? AgentTaskId,
    string? AgentTaskTitle,
    string? AgentTaskGoal,
    string? AgentTaskStatus,
    Guid? AgentStepId,
    int? AgentStepIndex,
    string? AgentStepTitle,
    string? AgentStepStatus,
    string? AgentStepToolCode,
    Guid? ApprovalRequestId,
    string? ApprovalType,
    string? ApprovalStatus,
    string? ApprovalTargetName,
    DateTimeOffset? ApprovalDecidedAt,
    Guid? ArtifactWorkspaceId,
    string? WorkspaceCode,
    string? WorkspaceStatus,
    Guid? ArtifactId,
    string? ArtifactName,
    string? ArtifactType,
    string? ArtifactStatus,
    string? ArtifactRelativePath,
    string? ArtifactDownloadUrl,
    string? AgentStepOutputKind,
    int? AgentStepResultCount,
    bool? AgentStepLowConfidence,
    IReadOnlyList<SessionTimelineStepSourceDto> AgentStepSources);

public sealed record SessionTimelineStepSourceDto(
    Guid? KnowledgeBaseId,
    int? DocumentId,
    string? DocumentName,
    int? ChunkIndex,
    double? Score,
    bool? IsLowConfidence,
    string? LowConfidenceReason,
    string? TextPreview);

[AuthorizeRequirement("AiGateway.GetSession")]
public sealed record GetSessionTimelineQuery(
    Guid SessionId,
    int Count = 200,
    bool IsDesc = false,
    int? BeforeSequence = null,
    int? AfterSequence = null)
    : IQuery<Result<SessionTimelinePageDto>>;

public sealed class GetSessionTimelineQueryHandler(
    SessionTimelineQueryCoordinator timelineQueryCoordinator)
    : IQueryHandler<GetSessionTimelineQuery, Result<SessionTimelinePageDto>>
{
    public Task<Result<SessionTimelinePageDto>> Handle(
        GetSessionTimelineQuery request,
        CancellationToken cancellationToken)
    {
        return timelineQueryCoordinator.GetAsync(request, cancellationToken);
    }
}
