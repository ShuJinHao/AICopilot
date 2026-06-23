using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.Core.Rag.Specifications.KnowledgeBase;
using AICopilot.RagService.KnowledgeBases;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Events;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.RagService.Documents;

public record KnowledgeDocumentDto
{
    public int Id { get; init; }
    public Guid KnowledgeBaseId { get; init; }
    public required string Name { get; init; }
    public required string Extension { get; init; }
    public required DocumentStatus Status { get; init; }
    public int ChunkCount { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ProcessedAt { get; init; }
    public required string Classification { get; init; }
    public required string SourceType { get; init; }
    public bool IsSanitized { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public DateTime? EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public bool AllowedForFinalPrompt { get; init; }
    public string? BlockedReason { get; init; }
}

[AuthorizeRequirement("Rag.GetListDocuments")]
public record GetListDocumentsQuery(Guid KnowledgeBaseId) : IQuery<Result<IList<KnowledgeDocumentDto>>>;

public class GetListDocumentsQueryHandler(
    IReadRepository<KnowledgeBase> repository,
    ICurrentUser currentUser)
    : IQueryHandler<GetListDocumentsQuery, Result<IList<KnowledgeDocumentDto>>>
{
    public async Task<Result<IList<KnowledgeDocumentDto>>> Handle(
        GetListDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        var knowledgeBase = await repository.FirstOrDefaultAsync(
            new KnowledgeBaseByIdWithDocumentsSpec(new KnowledgeBaseId(request.KnowledgeBaseId)),
            cancellationToken);

        if (knowledgeBase == null ||
            currentUser.Id is not { } userId ||
            !KnowledgeBaseAccessPolicy.CanRead(knowledgeBase, userId, KnowledgeBaseAccessPolicy.IsAdmin(currentUser)))
        {
            return Result.NotFound();
        }

        IList<KnowledgeDocumentDto> result = knowledgeBase?.Documents
            .OrderByDescending(document => document.CreatedAt)
            .Select(document => new KnowledgeDocumentDto
            {
                Id = document.Id,
                KnowledgeBaseId = document.KnowledgeBaseId,
                Name = document.Name,
                Extension = document.Extension,
                Status = document.Status,
                ChunkCount = document.ChunkCount,
                ErrorMessage = document.ErrorMessage,
                CreatedAt = document.CreatedAt,
                ProcessedAt = document.ProcessedAt,
                Classification = document.Classification.ToString(),
                SourceType = document.SourceType.ToString(),
                IsSanitized = document.IsSanitized,
                ReviewedBy = document.ReviewedBy,
                ReviewedAt = document.ReviewedAt,
                EffectiveFrom = document.EffectiveFrom,
                EffectiveTo = document.EffectiveTo,
                AllowedForFinalPrompt = document.AllowedForFinalPrompt,
                BlockedReason = document.BlockedReason
            })
            .ToList() ?? [];

        return Result.Success(result);
    }
}

[AuthorizeRequirement("Rag.DeleteDocument")]
public record DeleteDocumentCommand(int Id) : ICommand<Result>;

[AuthorizeRequirement("Rag.UploadDocument")]
public record RetryDocumentIndexingCommand(int Id) : ICommand<Result>;

[AuthorizeRequirement("Rag.UpdateDocumentGovernance")]
public record UpdateDocumentGovernanceCommand(
    int Id,
    string? Classification,
    string? SourceType,
    bool IsSanitized,
    DateTime? EffectiveFrom,
    DateTime? EffectiveTo,
    bool AllowedForFinalPrompt,
    string? BlockedReason) : ICommand<Result>;

public class UpdateDocumentGovernanceCommandHandler(
    IRepository<KnowledgeBase> repository,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
    : ICommandHandler<UpdateDocumentGovernanceCommand, Result>
{
    public async Task<Result> Handle(UpdateDocumentGovernanceCommand request, CancellationToken cancellationToken)
    {
        if (!TryParseEnum(request.Classification, out DocumentClassification classification))
        {
            return Result.Invalid("Invalid document classification.");
        }

        if (!TryParseEnum(request.SourceType, out DocumentSourceType sourceType))
        {
            return Result.Invalid("Invalid document source type.");
        }

        if (request.EffectiveFrom.HasValue &&
            request.EffectiveTo.HasValue &&
            request.EffectiveTo.Value < request.EffectiveFrom.Value)
        {
            return Result.Invalid("Document effective end time cannot be earlier than start time.");
        }

        var documentId = new DocumentId(request.Id);
        var knowledgeBase = await repository.GetAsync(
            kb => kb.Documents.Any(document => document.Id == documentId),
            [kb => kb.Documents],
            cancellationToken);

        if (knowledgeBase == null)
        {
            return Result.NotFound("Document not found.");
        }

        if (currentUser.Id is not { } userId ||
            !KnowledgeBaseAccessPolicy.CanWrite(knowledgeBase, userId, KnowledgeBaseAccessPolicy.IsAdmin(currentUser)))
        {
            return Result.NotFound();
        }

        var document = knowledgeBase.Documents.First(document => document.Id == documentId);
        var changedFields = BuildChangedFields(document, request, classification, sourceType);
        document.ConfigureGovernance(
            classification,
            sourceType,
            request.IsSanitized,
            document.ReviewedBy,
            document.ReviewedAt,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.AllowedForFinalPrompt,
            request.BlockedReason);

        repository.Update(knowledgeBase);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "Rag.UpdateDocumentGovernance",
                "KnowledgeDocument",
                document.Id.ToString(),
                document.Name,
                AuditResults.Succeeded,
                $"Updated document governance: {document.Name}; knowledgeBaseId={knowledgeBase.Id}; changed={(changedFields.Count == 0 ? "none" : string.Join(", ", changedFields))}.",
                changedFields),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static IReadOnlyCollection<string> BuildChangedFields(
        Document document,
        UpdateDocumentGovernanceCommand request,
        DocumentClassification classification,
        DocumentSourceType sourceType)
    {
        var changedFields = new List<string>();

        if (document.Classification != classification)
        {
            changedFields.Add("classification");
        }

        if (document.SourceType != sourceType)
        {
            changedFields.Add("sourceType");
        }

        if (document.IsSanitized != request.IsSanitized)
        {
            changedFields.Add("isSanitized");
        }

        if (document.EffectiveFrom != request.EffectiveFrom)
        {
            changedFields.Add("effectiveFrom");
        }

        if (document.EffectiveTo != request.EffectiveTo)
        {
            changedFields.Add("effectiveTo");
        }

        if (document.AllowedForFinalPrompt != request.AllowedForFinalPrompt)
        {
            changedFields.Add("allowedForFinalPrompt");
        }

        if (!string.Equals(document.BlockedReason, NormalizeOptionalText(request.BlockedReason), StringComparison.Ordinal))
        {
            changedFields.Add("blockedReason");
        }

        return changedFields;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryParseEnum<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(value) &&
               Enum.TryParse(value, ignoreCase: true, out parsed) &&
               Enum.IsDefined(typeof(TEnum), parsed);
    }
}

public class DeleteDocumentCommandHandler(
    IRepository<KnowledgeBase> repository,
    IIntegrationEventStager eventStager,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
    : ICommandHandler<DeleteDocumentCommand, Result>
{
    public async Task<Result> Handle(DeleteDocumentCommand request, CancellationToken cancellationToken)
    {
        var documentId = new DocumentId(request.Id);
        var knowledgeBase = await repository.GetAsync(
            kb => kb.Documents.Any(document => document.Id == documentId),
            [kb => kb.Documents],
            cancellationToken);

        if (knowledgeBase == null)
        {
            return Result.Success();
        }

        if (currentUser.Id is not { } userId ||
            !KnowledgeBaseAccessPolicy.CanWrite(knowledgeBase, userId, KnowledgeBaseAccessPolicy.IsAdmin(currentUser)))
        {
            return Result.NotFound();
        }

        var document = knowledgeBase.Documents.First(document => document.Id == documentId);
        var targetName = document.Name;
        var filePath = document.FilePath;
        var knowledgeBaseId = knowledgeBase.Id.Value;
        document.SoftDelete();
        repository.Update(knowledgeBase);
        eventStager.Stage(() => new DocumentFileDeletionRequestedEvent
        {
            DocumentId = request.Id,
            KnowledgeBaseId = knowledgeBaseId,
            FilePath = filePath,
            FileName = targetName
        });
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "Rag.DeleteDocument",
                "KnowledgeDocument",
                request.Id.ToString(),
                targetName,
                AuditResults.Succeeded,
                $"Soft-deleted document: {targetName}; knowledgeBaseId={knowledgeBase.Id}."),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public class RetryDocumentIndexingCommandHandler(
    IRepository<KnowledgeBase> repository,
    IIntegrationEventStager eventStager,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
    : ICommandHandler<RetryDocumentIndexingCommand, Result>
{
    public async Task<Result> Handle(RetryDocumentIndexingCommand request, CancellationToken cancellationToken)
    {
        var documentId = new DocumentId(request.Id);
        var knowledgeBase = await repository.GetAsync(
            kb => kb.Documents.Any(document => document.Id == documentId),
            [kb => kb.Documents],
            cancellationToken);

        if (knowledgeBase == null)
        {
            return Result.NotFound("Document not found.");
        }

        if (currentUser.Id is not { } userId ||
            !KnowledgeBaseAccessPolicy.CanWrite(knowledgeBase, userId, KnowledgeBaseAccessPolicy.IsAdmin(currentUser)))
        {
            return Result.NotFound();
        }

        var document = knowledgeBase.Documents.First(document => document.Id == documentId);
        if (!CanRetryIndexing(document.Status))
        {
            return Result.Invalid("Document status does not allow retry.");
        }

        eventStager.Stage(() => new DocumentUploadedEvent
        {
            DocumentId = document.Id,
            KnowledgeBaseId = knowledgeBase.Id.Value,
            FilePath = document.FilePath,
            FileName = document.Name
        });

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "Rag.RetryDocumentIndexing",
                "KnowledgeDocument",
                document.Id.ToString(),
                document.Name,
                AuditResults.Succeeded,
                $"Queued document indexing retry: {document.Name}; knowledgeBaseId={knowledgeBase.Id}; status={document.Status}."),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static bool CanRetryIndexing(DocumentStatus status)
    {
        return status is DocumentStatus.Pending
            or DocumentStatus.Failed
            or DocumentStatus.Parsing
            or DocumentStatus.Splitting
            or DocumentStatus.Embedding;
    }
}
