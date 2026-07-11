using System.Security.Cryptography;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Uploads;

public sealed class UploadRecordCoordinator(
    IRepository<UploadRecord> uploadRepository,
    IReadRepository<Session> sessionRepository,
    IReadRepository<AgentTask> taskRepository,
    IPersistenceFileStorageService persistenceFileStorage,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
{
    public async Task<Result<UploadRecordDto>> UploadAsync(
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
            scope is not UploadRecordScope.SessionTemp and not UploadRecordScope.AgentInput)
        {
            return Result.Invalid(
                "Upload scope must be SessionTemp or AgentInput. " +
                "Knowledge-base documents must use the RAG document API.");
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

        var fileStage = await persistenceFileStorage.StageAsync(
            normalizedFile.Stream,
            normalizedFile.FileName,
            cancellationToken);

        var record = await persistenceFileStorage.ExecuteAsync(
            fileStage,
            async commitCancellationToken =>
            {
                var createdRecord = new UploadRecord(
                    scope,
                    userId,
                    request.SessionId.HasValue ? new SessionId(request.SessionId.Value) : null,
                    request.AgentTaskId.HasValue ? new AgentTaskId(request.AgentTaskId.Value) : null,
                    normalizedFile.FileName,
                    normalizedFile.ContentType,
                    normalizedFile.FileSize,
                    sha256,
                    fileStage.StoragePath,
                    DateTimeOffset.UtcNow);

                uploadRepository.Add(createdRecord);
                await RecordUploadAuditAsync(
                    AuditResults.Succeeded,
                    "Uploaded file accepted by security policy.",
                    request,
                    createdRecord,
                    commitCancellationToken);
                await uploadRepository.SaveChangesAsync(commitCancellationToken);
                return createdRecord;
            },
            cancellationToken);

        return Result.Success(UploadRecordDtoMapper.Map(record));
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

        if (scope == UploadRecordScope.SessionTemp)
        {
            if (!request.SessionId.HasValue || request.SessionId.Value == Guid.Empty ||
                request.AgentTaskId.HasValue)
            {
                return Result.Invalid("Session uploads require only SessionId.");
            }

            var session = await sessionRepository.FirstOrDefaultAsync(
                new SessionByIdForUserSpec(new SessionId(request.SessionId.Value), userId),
                cancellationToken);
            return session is null ? Result.NotFound("Session not found.") : Result.Success();
        }

        if (!request.AgentTaskId.HasValue || request.AgentTaskId.Value == Guid.Empty ||
            request.SessionId.HasValue)
        {
            return Result.Invalid("Agent-input uploads require only AgentTaskId.");
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdForUserSpec(new AgentTaskId(request.AgentTaskId.Value), userId),
            cancellationToken);
        return task is null ? Result.NotFound("Agent task not found.") : Result.Success();
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
        var safeFileName = UploadFileNamePolicy.NormalizeForAudit(request.File.FileName);
        var metadata = new Dictionary<string, string>
        {
            ["scope"] = request.Scope,
            ["fileName"] = safeFileName,
            ["contentType"] = request.File.ContentType,
            ["fileSize"] = request.File.FileSize.ToString()
        };
        if (record is not null)
        {
            metadata["uploadId"] = record.Id.Value.ToString();
        }

        return auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.Upload",
                "UploadRecord",
                record?.Id.Value.ToString(),
                record?.FileName ?? safeFileName,
                result,
                summary,
                Metadata: metadata),
            cancellationToken);
    }
}
