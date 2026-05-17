using System.Security.Cryptography;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Sessions;
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
    IRepository<UploadRecord> uploadRepository,
    IReadRepository<Session> sessionRepository,
    IReadRepository<AgentTask> taskRepository,
    IFileStorageService fileStorage,
    IEnumerable<IRagDocumentUploadBridge> ragUploadBridges,
    IEnumerable<IKnowledgeBaseAccessChecker> knowledgeBaseAccessCheckers,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
    : ICommandHandler<UploadRecordCommand, Result<UploadRecordDto>>
{
    public async Task<Result<UploadRecordDto>> Handle(
        UploadRecordCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        if (!Enum.TryParse<UploadRecordScope>(request.Scope, ignoreCase: true, out var scope) ||
            !Enum.IsDefined(scope))
        {
            return Result.Invalid("Upload scope is invalid.");
        }

        var validation = await ValidateScopeAsync(request, scope, userId, cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result.From(validation);
        }

        var normalizedFile = await NormalizeStreamAsync(request.File, cancellationToken);
        var fileValidation = await AiGatewayUploadSecurityPolicy.ValidateAndNormalizeAsync(
            normalizedFile,
            cancellationToken);
        if (!fileValidation.IsValid || fileValidation.File is null)
        {
            await RecordUploadAuditAsync(
                AuditResults.Rejected,
                fileValidation.ErrorMessage ?? "Uploaded file rejected by security policy.",
                request,
                null,
                cancellationToken);
            await auditLogWriter.SaveChangesAsync(cancellationToken);
            return Result.Invalid(fileValidation.ErrorMessage ?? "Uploaded file rejected by security policy.");
        }

        normalizedFile = fileValidation.File;
        var sha256 = await ComputeSha256Async(normalizedFile.Stream, cancellationToken);
        normalizedFile.Stream.Position = 0;

        string? storagePath = null;
        int? ragDocumentId = null;

        if (scope == UploadRecordScope.KnowledgeBase)
        {
            var ragUploadBridge = ragUploadBridges.FirstOrDefault();
            if (ragUploadBridge is null)
            {
                return Result.Failure("RAG document upload bridge is not configured.");
            }

            var ragResult = await ragUploadBridge.UploadAsync(
                new RagDocumentUploadBridgeRequest(
                    request.KnowledgeBaseId!.Value,
                    normalizedFile.FileName,
                    normalizedFile.Stream,
                    normalizedFile.ContentType,
                    normalizedFile.FileSize,
                    SourceType: "UserUploaded"),
                cancellationToken);
            ragDocumentId = ragResult.DocumentId;
        }
        else
        {
            storagePath = await fileStorage.SaveAsync(
                normalizedFile.Stream,
                normalizedFile.FileName,
                cancellationToken);
        }

        var record = new UploadRecord(
            scope,
            userId,
            request.SessionId.HasValue ? new SessionId(request.SessionId.Value) : null,
            request.AgentTaskId.HasValue ? new AgentTaskId(request.AgentTaskId.Value) : null,
            request.KnowledgeBaseId,
            ragDocumentId,
            normalizedFile.FileName,
            normalizedFile.ContentType,
            normalizedFile.FileSize,
            sha256,
            storagePath,
            DateTimeOffset.UtcNow);

        uploadRepository.Add(record);
        await RecordUploadAuditAsync(
            AuditResults.Succeeded,
            "Uploaded file accepted by security policy.",
            request,
            record,
            cancellationToken);
        await uploadRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(record));
    }

    private async Task<Result> ValidateScopeAsync(
        UploadRecordCommand request,
        UploadRecordScope scope,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (request.File.FileSize <= 0)
        {
            return Result.Invalid("Uploaded file is empty.");
        }

        if (scope == UploadRecordScope.KnowledgeBase)
        {
            if (!request.KnowledgeBaseId.HasValue || request.KnowledgeBaseId.Value == Guid.Empty)
            {
                return Result.Invalid("KnowledgeBaseId is required for knowledge-base uploads.");
            }

            var accessChecker = knowledgeBaseAccessCheckers.FirstOrDefault();
            if (accessChecker is null)
            {
                return Result.Failure("RAG knowledge base access checker is not configured.");
            }

            var canWrite = await accessChecker.CanWriteAsync(
                request.KnowledgeBaseId.Value,
                userId,
                IsAdmin(),
                cancellationToken);
            return canWrite ? Result.Success() : Result.NotFound();
        }

        if (scope == UploadRecordScope.SessionTemp)
        {
            if (!request.SessionId.HasValue || request.SessionId.Value == Guid.Empty)
            {
                return Result.Invalid("SessionId is required for session uploads.");
            }

            var session = await sessionRepository.FirstOrDefaultAsync(
                new SessionByIdForUserSpec(new SessionId(request.SessionId.Value), userId),
                cancellationToken);
            return session is null ? Result.NotFound("Session not found.") : Result.Success();
        }

        if (!request.AgentTaskId.HasValue || request.AgentTaskId.Value == Guid.Empty)
        {
            return Result.Invalid("AgentTaskId is required for agent input uploads.");
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdForUserSpec(new AgentTaskId(request.AgentTaskId.Value), userId),
            cancellationToken);
        return task is null ? Result.NotFound("Agent task not found.") : Result.Success();
    }

    private bool IsAdmin()
    {
        return string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<AiGatewayUploadStream> NormalizeStreamAsync(
        AiGatewayUploadStream file,
        CancellationToken cancellationToken)
    {
        if (file.Stream.CanSeek)
        {
            file.Stream.Position = 0;
            return file;
        }

        var memory = new MemoryStream();
        await file.Stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return file with { Stream = memory, FileSize = memory.Length };
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private Task RecordUploadAuditAsync(
        string result,
        string summary,
        UploadRecordCommand request,
        UploadRecord? record,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            ["scope"] = request.Scope,
            ["fileName"] = Path.GetFileName(request.File.FileName),
            ["contentType"] = request.File.ContentType,
            ["fileSize"] = request.File.FileSize.ToString()
        };
        if (record is not null)
        {
            metadata["uploadId"] = record.Id.Value.ToString();
            metadata["status"] = record.Status.ToString();
        }

        return auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.Upload",
                "UploadRecord",
                record?.Id.Value.ToString(),
                record?.FileName ?? Path.GetFileName(request.File.FileName),
                result,
                summary,
                Metadata: metadata),
            cancellationToken);
    }

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
            records.Select(UploadRecordCommandHandler.Map).ToArray());
    }
}
