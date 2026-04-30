using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Specifications.KnowledgeBase;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
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
}

[AuthorizeRequirement("Rag.GetListDocuments")]
public record GetListDocumentsQuery(Guid KnowledgeBaseId) : IQuery<Result<IList<KnowledgeDocumentDto>>>;

public class GetListDocumentsQueryHandler(IReadRepository<KnowledgeBase> repository)
    : IQueryHandler<GetListDocumentsQuery, Result<IList<KnowledgeDocumentDto>>>
{
    public async Task<Result<IList<KnowledgeDocumentDto>>> Handle(
        GetListDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        var knowledgeBase = await repository.FirstOrDefaultAsync(
            new KnowledgeBaseByIdWithDocumentsSpec(request.KnowledgeBaseId),
            cancellationToken);

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
                ProcessedAt = document.ProcessedAt
            })
            .ToList() ?? [];

        return Result.Success(result);
    }
}

[AuthorizeRequirement("Rag.DeleteDocument")]
public record DeleteDocumentCommand(int Id) : ICommand<Result>;

public class DeleteDocumentCommandHandler(
    IRepository<KnowledgeBase> repository,
    IFileStorageService fileStorage)
    : ICommandHandler<DeleteDocumentCommand, Result>
{
    public async Task<Result> Handle(DeleteDocumentCommand request, CancellationToken cancellationToken)
    {
        var knowledgeBase = await repository.GetAsync(
            kb => kb.Documents.Any(document => document.Id == request.Id),
            [kb => kb.Documents],
            cancellationToken);

        if (knowledgeBase == null)
        {
            return Result.Success();
        }

        var document = knowledgeBase.Documents.First(document => document.Id == request.Id);
        knowledgeBase.RemoveDocument(request.Id);
        repository.Update(knowledgeBase);
        await repository.SaveChangesAsync(cancellationToken);

        await fileStorage.DeleteAsync(document.FilePath, cancellationToken);
        return Result.Success();
    }
}
