using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.Core.Rag.Specifications.KnowledgeBase;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
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
    public Guid? OwnerUserId { get; init; }
    public required string AccessScope { get; init; }
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
    IReadRepository<EmbeddingModel> embeddingRepository,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
    : ICommandHandler<UpdateKnowledgeBaseCommand, Result>
{
    public async Task<Result> Handle(UpdateKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var embeddingModelId = new EmbeddingModelId(request.EmbeddingModelId);
        var embeddingModel = await embeddingRepository.GetByIdAsync(embeddingModelId, cancellationToken);
        if (embeddingModel == null)
        {
            return Result.NotFound("指定的嵌入模型不存在");
        }

        var entity = await repository.GetByIdAsync(new KnowledgeBaseId(request.Id), cancellationToken);
        if (entity == null)
        {
            return Result.NotFound();
        }

        if (!KnowledgeBaseAccessPolicy.CanWrite(entity, userId, KnowledgeBaseAccessPolicy.IsAdmin(currentUser)))
        {
            return Result.NotFound();
        }

        var changedFields = new List<string>();
        if (!string.Equals(entity.Name, request.Name, StringComparison.Ordinal))
        {
            changedFields.Add("name");
        }

        if (!string.Equals(entity.Description, request.Description, StringComparison.Ordinal))
        {
            changedFields.Add("description");
        }

        if (entity.EmbeddingModelId != embeddingModelId)
        {
            changedFields.Add("embeddingModelId");
        }

        entity.UpdateInfo(request.Name, request.Description);
        entity.UpdateEmbeddingModel(embeddingModelId);

        repository.Update(entity);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "Rag.UpdateKnowledgeBase",
                "KnowledgeBase",
                entity.Id.ToString(),
                entity.Name,
                AuditResults.Succeeded,
                $"Updated knowledge base: {entity.Name}; changed={(changedFields.Count == 0 ? "none" : string.Join(", ", changedFields))}.",
                changedFields),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

[AuthorizeRequirement("Rag.DeleteKnowledgeBase")]
public record DeleteKnowledgeBaseCommand(Guid Id) : ICommand<Result>;

public class DeleteKnowledgeBaseCommandHandler(
    IRepository<KnowledgeBase> repository,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
    : ICommandHandler<DeleteKnowledgeBaseCommand, Result>
{
    public async Task<Result> Handle(DeleteKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var entity = await repository.GetByIdAsync(new KnowledgeBaseId(request.Id), cancellationToken);
        if (entity == null)
        {
            return Result.Success();
        }

        if (!KnowledgeBaseAccessPolicy.CanWrite(entity, userId, KnowledgeBaseAccessPolicy.IsAdmin(currentUser)))
        {
            return Result.NotFound();
        }

        var targetName = entity.Name;
        repository.Delete(entity);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "Rag.DeleteKnowledgeBase",
                "KnowledgeBase",
                request.Id.ToString(),
                targetName,
                AuditResults.Succeeded,
                $"Deleted knowledge base: {targetName}."),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

[AuthorizeRequirement("Rag.GetKnowledgeBase")]
public record GetKnowledgeBaseQuery(Guid Id) : IQuery<Result<KnowledgeBaseDto>>;

public class GetKnowledgeBaseQueryHandler(
    IReadRepository<KnowledgeBase> repository,
    ICurrentUser currentUser)
    : IQueryHandler<GetKnowledgeBaseQuery, Result<KnowledgeBaseDto>>
{
    public async Task<Result<KnowledgeBaseDto>> Handle(
        GetKnowledgeBaseQuery request,
        CancellationToken cancellationToken)
    {
        var result = await repository.FirstOrDefaultAsync(
            new KnowledgeBaseByIdWithDocumentsSpec(new KnowledgeBaseId(request.Id)),
            cancellationToken);

        if (result == null || currentUser.Id is not { } userId)
        {
            return Result.NotFound();
        }

        return KnowledgeBaseAccessPolicy.CanRead(result, userId, KnowledgeBaseAccessPolicy.IsAdmin(currentUser))
            ? Result.Success(Map(result))
            : Result.NotFound();
    }

    private static KnowledgeBaseDto Map(KnowledgeBase knowledgeBase)
    {
        return new KnowledgeBaseDto
        {
            Id = knowledgeBase.Id,
            Name = knowledgeBase.Name,
            Description = knowledgeBase.Description,
            EmbeddingModelId = knowledgeBase.EmbeddingModelId,
            OwnerUserId = knowledgeBase.OwnerUserId,
            AccessScope = knowledgeBase.AccessScope.ToString(),
            DocumentCount = knowledgeBase.Documents.Count
        };
    }
}

[AuthorizeRequirement("Rag.GetListKnowledgeBases")]
public record GetListKnowledgeBasesQuery : IQuery<Result<IList<KnowledgeBaseDto>>>;

public class GetListKnowledgeBasesQueryHandler(
    IReadRepository<KnowledgeBase> repository,
    ICurrentUser currentUser)
    : IQueryHandler<GetListKnowledgeBasesQuery, Result<IList<KnowledgeBaseDto>>>
{
    public async Task<Result<IList<KnowledgeBaseDto>>> Handle(
        GetListKnowledgeBasesQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Success<IList<KnowledgeBaseDto>>([]);
        }

        var result = await repository.ListAsync(new KnowledgeBasesOrderedWithDocumentsSpec(), cancellationToken);
        var isAdmin = KnowledgeBaseAccessPolicy.IsAdmin(currentUser);
        IList<KnowledgeBaseDto> items = result
            .Where(knowledgeBase => KnowledgeBaseAccessPolicy.CanRead(knowledgeBase, userId, isAdmin))
            .Select(Map)
            .ToList();
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
            OwnerUserId = knowledgeBase.OwnerUserId,
            AccessScope = knowledgeBase.AccessScope.ToString(),
            DocumentCount = knowledgeBase.Documents.Count
        };
    }
}
