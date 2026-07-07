using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Uploads;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Uploads;

public sealed record AiGatewayUploadStream(string FileName, string ContentType, long FileSize, Stream Stream);

public sealed record UploadRecordDto(
    Guid Id,
    string Scope,
    Guid? SessionId,
    Guid? AgentTaskId,
    Guid? KnowledgeBaseId,
    int? RagDocumentId,
    string FileName,
    string ContentType,
    long FileSize,
    string Sha256,
    string Status,
    DateTimeOffset CreatedAt);

[AuthorizeRequirement("AiGateway.Upload")]
public sealed record UploadRecordCommand(
    string Scope,
    AiGatewayUploadStream File,
    Guid? SessionId = null,
    Guid? AgentTaskId = null,
    Guid? KnowledgeBaseId = null) : ICommand<Result<UploadRecordDto>>;

[AuthorizeRequirement("AiGateway.GetUpload")]
public sealed record GetListUploadRecordsQuery(Guid? SessionId = null, Guid? AgentTaskId = null)
    : IQuery<Result<IReadOnlyCollection<UploadRecordDto>>>;

public sealed class UploadRecordCommandHandler(
    UploadRecordCoordinator uploadRecordCoordinator)
    : ICommandHandler<UploadRecordCommand, Result<UploadRecordDto>>
{
    public Task<Result<UploadRecordDto>> Handle(
        UploadRecordCommand request,
        CancellationToken cancellationToken) =>
        uploadRecordCoordinator.UploadAsync(request, cancellationToken);
}

public static class UploadRecordDtoMapper
{
    public static UploadRecordDto Map(UploadRecord record)
    {
        return new UploadRecordDto(
            record.Id,
            record.Scope.ToString(),
            record.SessionId?.Value,
            record.AgentTaskId?.Value,
            record.KnowledgeBaseId,
            record.RagDocumentId,
            record.FileName,
            record.ContentType,
            record.FileSize,
            record.Sha256,
            record.Status.ToString(),
            record.CreatedAt);
    }
}

public sealed class GetListUploadRecordsQueryHandler(
    IReadRepository<UploadRecord> repository,
    ICurrentUser currentUser)
    : IQueryHandler<GetListUploadRecordsQuery, Result<IReadOnlyCollection<UploadRecordDto>>>
{
    public async Task<Result<IReadOnlyCollection<UploadRecordDto>>> Handle(
        GetListUploadRecordsQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        List<UploadRecord> records;
        if (request.AgentTaskId.HasValue)
        {
            records = await repository.ListAsync(
                new UploadRecordsByAgentTaskForUserSpec(new AgentTaskId(request.AgentTaskId.Value), userId),
                cancellationToken);
        }
        else if (request.SessionId.HasValue)
        {
            records = await repository.ListAsync(
                new UploadRecordsBySessionForUserSpec(new SessionId(request.SessionId.Value), userId),
                cancellationToken);
        }
        else
        {
            return Result.Invalid("SessionId or AgentTaskId is required.");
        }

        return Result.Success<IReadOnlyCollection<UploadRecordDto>>(
            records.Select(UploadRecordDtoMapper.Map).ToArray());
    }
}
