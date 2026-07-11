using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.RagService.KnowledgeBases;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Events;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace AICopilot.RagService.Commands.Documents;

public record UploadDocumentDto(int Id, string Status);

public record FileUploadStream(
    string FileName,
    Stream Stream,
    string? ContentType = null,
    long? FileSize = null);

[AuthorizeRequirement("Rag.UploadDocument")]
public record UploadDocumentCommand(
    Guid KnowledgeBaseId,
    FileUploadStream File,
    string? Classification = null,
    string? SourceType = null,
    bool IsSanitized = false,
    string? ReviewedBy = null,
    DateTime? ReviewedAt = null,
    DateTime? EffectiveFrom = null,
    DateTime? EffectiveTo = null,
    bool AllowedForFinalPrompt = true,
    string? BlockedReason = null) : ICommand<Result<UploadDocumentDto>>;

public class UploadDocumentCommandHandler(
    IRepository<KnowledgeBase> kbRepo,
    IDocumentIdAllocator documentIdAllocator,
    IPersistenceFileStorageService persistenceFileStorage,
    IDocumentFormatPolicy documentFormatPolicy,
    IIntegrationEventStager eventStager,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
    : ICommandHandler<UploadDocumentCommand, Result<UploadDocumentDto>>
{
    public async Task<Result<UploadDocumentDto>> Handle(
        UploadDocumentCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        // 1. 获取知识库聚合根（并急切加载 Documents 集合）
        // 使用我们刚扩展的 GetAsync 方法，通过 includes 参数加载子实体
        var kb = await kbRepo.GetAsync(
            kb => kb.Id == request.KnowledgeBaseId,
            includes: [k => k.Documents],
            cancellationToken);

        if (kb == null) return Result.NotFound("知识库不存在");

        if (!KnowledgeBaseAccessPolicy.CanWrite(kb, userId, KnowledgeBaseAccessPolicy.IsAdmin(currentUser)))
        {
            return Result.NotFound();
        }

        if (!TryParseEnum(request.Classification, DocumentClassification.Internal, out var classification))
        {
            return Result.Invalid("Invalid document classification.");
        }

        if (!TryParseEnum(request.SourceType, DocumentSourceType.UserUploaded, out var sourceType))
        {
            return Result.Invalid("Invalid document source type.");
        }

        var normalizedFile = await RagDocumentUploadSecurityPolicy.NormalizeStreamAsync(request.File, cancellationToken);
        var securityResult = await RagDocumentUploadSecurityPolicy.ValidateAndNormalizeAsync(
            normalizedFile,
            documentFormatPolicy,
            cancellationToken);
        if (!securityResult.IsValid || securityResult.File is null)
        {
            await RecordUploadAuditAsync(
                AuditResults.Rejected,
                securityResult.ErrorMessage ?? "RAG document upload rejected by security policy.",
                request,
                null,
                cancellationToken);
            await auditLogWriter.SaveChangesAsync(cancellationToken);
            return Result.Invalid(securityResult.ErrorMessage ?? "RAG document upload rejected by security policy.");
        }

        normalizedFile = securityResult.File;
        var extension = Path.GetExtension(normalizedFile.FileName).ToLowerInvariant();
        if (!documentFormatPolicy.IsSupported(extension))
        {
            var supported = string.Join(", ", documentFormatPolicy.SupportedExtensions);
            return Result.Invalid($"不支持的文件格式: {extension}. 支持的格式: {supported}");
        }

        // 2. 计算文件 Hash (SHA256)
        string fileHash;
        using (var sha256 = SHA256.Create())
        {
            // 确保流从头开始
            if (normalizedFile.Stream.CanSeek) normalizedFile.Stream.Position = 0;

            var hashBytes = await sha256.ComputeHashAsync(normalizedFile.Stream, cancellationToken);
            fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            // 计算完 Hash 后，必须重置流位置，否则后续保存文件时会读到空内容
            if (normalizedFile.Stream.CanSeek) normalizedFile.Stream.Position = 0;
        }

        // 3. 检查文件是否已存在 (基于 Hash 实现幂等性)
        // 因为 Documents 已经加载到内存中，我们可以直接使用 LINQ 查询
        var existingDoc = kb.Documents.FirstOrDefault(d => d.FileHash == fileHash);
        if (existingDoc != null)
        {
            // 如果文件已存在，直接返回成功，并返回现有的文档 ID
            // 这实现了接口的幂等性：多次上传同一文件不会产生副作用
            return Result.Success(new UploadDocumentDto(existingDoc.Id, existingDoc.Status.ToString()));
        }

        var documentId = await documentIdAllocator.AllocateAsync(cancellationToken);

        // 先持久化对账意图，再写物理文件；后续数据库提交复用同一 commit id。
        var fileStage = await persistenceFileStorage.StageAsync(
            normalizedFile.Stream,
            normalizedFile.FileName,
            cancellationToken);
        var document = await persistenceFileStorage.ExecuteAsync(
            fileStage,
            async commitCancellationToken =>
            {
                // 5. 领域模型行为：添加文档
                var createdDocument = kb.AddDocument(
                    documentId,
                    normalizedFile.FileName,
                    fileStage.StoragePath,
                    extension,
                    fileHash,
                    classification,
                    sourceType,
                    request.IsSanitized,
                    request.ReviewedBy,
                    request.ReviewedAt,
                    request.EffectiveFrom,
                    request.EffectiveTo,
                    request.AllowedForFinalPrompt,
                    request.BlockedReason);

                // 6. 暂存集成事件，和文档聚合在同一个 RagDbContext SaveChanges 中提交。
                eventStager.Stage(() => new DocumentUploadedEvent
                {
                    DocumentId = createdDocument.Id,
                    KnowledgeBaseId = kb.Id,
                    FilePath = fileStage.StoragePath,
                    FileName = normalizedFile.FileName
                });

                await RecordUploadAuditAsync(
                    AuditResults.Succeeded,
                    "RAG document upload accepted by security policy.",
                    request,
                    createdDocument,
                    commitCancellationToken);

                // 7. 持久化到数据库，同时提交文档和 outbox 行。
                await kbRepo.SaveChangesAsync(commitCancellationToken);
                return createdDocument;
            },
            cancellationToken);

        return Result.Success(new UploadDocumentDto(document.Id, document.Status.ToString()));
    }

    private Task RecordUploadAuditAsync(
        string result,
        string summary,
        UploadDocumentCommand request,
        Document? document,
        CancellationToken cancellationToken)
    {
        var safeFileName = UploadFileNamePolicy.NormalizeForAudit(request.File.FileName);
        return auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "Rag.UploadDocument",
                "KnowledgeDocument",
                document?.Id.ToString(),
                document?.Name ?? safeFileName,
                result,
                summary,
                Metadata: new Dictionary<string, string>
                {
                    ["knowledgeBaseId"] = request.KnowledgeBaseId.ToString(),
                    ["fileName"] = safeFileName,
                    ["contentType"] = request.File.ContentType ?? string.Empty,
                    ["fileSize"] = (request.File.FileSize ?? 0).ToString()
                }),
            cancellationToken);
    }

    private static bool TryParseEnum<TEnum>(string? value, TEnum defaultValue, out TEnum parsed)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = defaultValue;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out parsed) && Enum.IsDefined(typeof(TEnum), parsed);
    }
}
