using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Specifications.KnowledgeBase;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.RagService.KnowledgeBases;

public record KnowledgeBaseDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public Guid EmbeddingModelId { get; init; }
    public int DocumentCount { get; init; }
}

[AuthorizeRequirement("Rag.UpdateKnowledgeBase")]
public record UpdateKnowledgeBaseCommand(
    Guid Id,
    string Name,
    string Description,
    Guid EmbeddingModelId) : ICommand<Result>;

public class UpdateKnowledgeBaseCommandHandler(
    IRepository<KnowledgeBase> repository,
    IReadRepository<EmbeddingModel> embeddingRepository)
    : ICommandHandler<UpdateKnowledgeBaseCommand, Result>
{
    public async Task<Result> Handle(UpdateKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var embeddingModel = await embeddingRepository.GetByIdAsync(request.EmbeddingModelId, cancellationToken);
        if (embeddingModel == null)
        {
            return Result.NotFound("指定的嵌入模型不存在");
        }

        var entity = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity == null)
        {
            return Result.NotFound();
        }

        entity.UpdateInfo(request.Name, request.Description);
        entity.UpdateEmbeddingModel(request.EmbeddingModelId);

        repository.Update(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

[AuthorizeRequirement("Rag.DeleteKnowledgeBase")]
public record DeleteKnowledgeBaseCommand(Guid Id) : ICommand<Result>;

public class DeleteKnowledgeBaseCommandHandler(IRepository<KnowledgeBase> repository)
    : ICommandHandler<DeleteKnowledgeBaseCommand, Result>
{
    public async Task<Result> Handle(DeleteKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity == null)
        {
            return Result.Success();
        }

        repository.Delete(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

[AuthorizeRequirement("Rag.GetKnowledgeBase")]
public record GetKnowledgeBaseQuery(Guid Id) : IQuery<Result<KnowledgeBaseDto>>;

public class GetKnowledgeBaseQueryHandler(IReadRepository<KnowledgeBase> repository)
    : IQueryHandler<GetKnowledgeBaseQuery, Result<KnowledgeBaseDto>>
{
    public async Task<Result<KnowledgeBaseDto>> Handle(
        GetKnowledgeBaseQuery request,
        CancellationToken cancellationToken)
    {
        var result = await repository.FirstOrDefaultAsync(
            new KnowledgeBaseByIdWithDocumentsSpec(request.Id),
            cancellationToken);

        return result == null ? Result.NotFound() : Result.Success(Map(result));
    }

    private static KnowledgeBaseDto Map(KnowledgeBase knowledgeBase)
    {
        return new KnowledgeBaseDto
        {
            Id = knowledgeBase.Id,
            Name = knowledgeBase.Name,
            Description = knowledgeBase.Description,
            EmbeddingModelId = knowledgeBase.EmbeddingModelId,
            DocumentCount = knowledgeBase.Documents.Count
        };
    }
}

[AuthorizeRequirement("Rag.GetListKnowledgeBases")]
public record GetListKnowledgeBasesQuery : IQuery<Result<IList<KnowledgeBaseDto>>>;

public class GetListKnowledgeBasesQueryHandler(IReadRepository<KnowledgeBase> repository)
    : IQueryHandler<GetListKnowledgeBasesQuery, Result<IList<KnowledgeBaseDto>>>
{
    public async Task<Result<IList<KnowledgeBaseDto>>> Handle(
        GetListKnowledgeBasesQuery request,
        CancellationToken cancellationToken)
    {
        var result = await repository.ListAsync(new KnowledgeBasesOrderedWithDocumentsSpec(), cancellationToken);
        IList<KnowledgeBaseDto> items = result.Select(Map).ToList();
        return Result.Success(items);
    }

    private static KnowledgeBaseDto Map(KnowledgeBase knowledgeBase)
    {
        return new KnowledgeBaseDto
        {
            Id = knowledgeBase.Id,
            Name = knowledgeBase.Name,
            Description = knowledgeBase.Description,
            EmbeddingModelId = knowledgeBase.EmbeddingModelId,
            DocumentCount = knowledgeBase.Documents.Count
        };
    }
}
