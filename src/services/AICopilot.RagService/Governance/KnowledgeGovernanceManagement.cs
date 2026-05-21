using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.RagService.Governance;

[AuthorizeRequirement("Rag.ManageKnowledgeGovernance")]
public sealed record UpsertKnowledgeCategoryCommand(
    Guid? Id,
    string Name,
    string BusinessDomain,
    string Visibility,
    string Department,
    int Priority,
    bool IsEnabled = true) : ICommand<Result<KnowledgeCategoryDto>>;

[AuthorizeRequirement("Rag.GetKnowledgeGovernance")]
public sealed record GetKnowledgeCategoryQuery(Guid Id) : IQuery<Result<KnowledgeCategoryDto>>;

[AuthorizeRequirement("Rag.GetKnowledgeGovernance")]
public sealed record GetListKnowledgeCategoriesQuery : IQuery<Result<IList<KnowledgeCategoryDto>>>;

[AuthorizeRequirement("Rag.ManageKnowledgeGovernance")]
public sealed record UpsertKnowledgeSupplementCommand(
    Guid? Id,
    string Title,
    string Content,
    string Priority,
    DateTime? EffectiveAt,
    DateTime? ExpiredAt,
    Guid? CategoryId,
    int? DocumentId,
    bool IsEnabled = true) : ICommand<Result<KnowledgeSupplementDto>>;

[AuthorizeRequirement("Rag.ManageKnowledgeGovernance")]
public sealed record SetKnowledgeSupplementEnabledCommand(
    Guid Id,
    bool IsEnabled) : ICommand<Result<KnowledgeSupplementDto>>;

[AuthorizeRequirement("Rag.GetKnowledgeGovernance")]
public sealed record GetKnowledgeSupplementQuery(Guid Id) : IQuery<Result<KnowledgeSupplementDto>>;

[AuthorizeRequirement("Rag.GetKnowledgeGovernance")]
public sealed record GetListKnowledgeSupplementsQuery(
    Guid? CategoryId = null,
    int? DocumentId = null,
    bool ActiveOnly = false) : IQuery<Result<IList<KnowledgeSupplementDto>>>;

[AuthorizeRequirement("Rag.GetKnowledgeGovernance")]
public sealed record GetApplicableKnowledgeSupplementsQuery(
    Guid? CategoryId = null,
    int? DocumentId = null) : IQuery<Result<IList<KnowledgeSupplementHitDto>>>;

public sealed class UpsertKnowledgeCategoryCommandHandler(
    IRepository<KnowledgeCategory> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpsertKnowledgeCategoryCommand, Result<KnowledgeCategoryDto>>
{
    public async Task<Result<KnowledgeCategoryDto>> Handle(
        UpsertKnowledgeCategoryCommand request,
        CancellationToken cancellationToken)
    {
        KnowledgeCategory? category = null;
        var created = false;
        if (request.Id.HasValue)
        {
            category = await repository.GetByIdAsync(new KnowledgeCategoryId(request.Id.Value), cancellationToken);
        }

        if (category is null)
        {
            category = new KnowledgeCategory(
                request.Name,
                request.BusinessDomain,
                request.Visibility,
                request.Department,
                request.Priority,
                request.IsEnabled);
            repository.Add(category);
            created = true;
        }
        else
        {
            category.Update(
                request.Name,
                request.BusinessDomain,
                request.Visibility,
                request.Department,
                request.Priority,
                request.IsEnabled);
            repository.Update(category);
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                created ? "Rag.CreateKnowledgeCategory" : "Rag.UpdateKnowledgeCategory",
                "KnowledgeCategory",
                category.Id.ToString(),
                category.Name,
                AuditResults.Succeeded,
                $"Knowledge category {(created ? "created" : "updated")}. name={category.Name}; domain={category.BusinessDomain}; enabled={category.IsEnabled}.",
                ["name", "businessDomain", "visibility", "department", "priority", "isEnabled"]),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(category));
    }

    internal static KnowledgeCategoryDto Map(KnowledgeCategory category)
    {
        return new KnowledgeCategoryDto(
            category.Id,
            category.Name,
            category.BusinessDomain,
            category.Visibility,
            category.Department,
            category.Priority,
            category.IsEnabled);
    }
}

public sealed class GetKnowledgeCategoryQueryHandler(IReadRepository<KnowledgeCategory> repository)
    : IQueryHandler<GetKnowledgeCategoryQuery, Result<KnowledgeCategoryDto>>
{
    public async Task<Result<KnowledgeCategoryDto>> Handle(
        GetKnowledgeCategoryQuery request,
        CancellationToken cancellationToken)
    {
        var category = await repository.GetByIdAsync(new KnowledgeCategoryId(request.Id), cancellationToken);
        return category is null ? Result.NotFound() : Result.Success(UpsertKnowledgeCategoryCommandHandler.Map(category));
    }
}

public sealed class GetListKnowledgeCategoriesQueryHandler(IReadRepository<KnowledgeCategory> repository)
    : IQueryHandler<GetListKnowledgeCategoriesQuery, Result<IList<KnowledgeCategoryDto>>>
{
    public async Task<Result<IList<KnowledgeCategoryDto>>> Handle(
        GetListKnowledgeCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        var categories = await repository.ListAsync(null, cancellationToken);
        IList<KnowledgeCategoryDto> result = categories
            .OrderByDescending(category => category.Priority)
            .ThenBy(category => category.Name)
            .Select(UpsertKnowledgeCategoryCommandHandler.Map)
            .ToList();
        return Result.Success(result);
    }
}

public sealed class UpsertKnowledgeSupplementCommandHandler(
    IRepository<KnowledgeSupplement> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpsertKnowledgeSupplementCommand, Result<KnowledgeSupplementDto>>
{
    public async Task<Result<KnowledgeSupplementDto>> Handle(
        UpsertKnowledgeSupplementCommand request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<KnowledgeSupplementPriority>(request.Priority, ignoreCase: true, out var priority) ||
            !Enum.IsDefined(typeof(KnowledgeSupplementPriority), priority))
        {
            return Result.Invalid("Invalid knowledge supplement priority.");
        }

        KnowledgeSupplement? supplement = null;
        var created = false;
        if (request.Id.HasValue)
        {
            supplement = await repository.GetByIdAsync(new KnowledgeSupplementId(request.Id.Value), cancellationToken);
        }

        var categoryId = request.CategoryId.HasValue ? new KnowledgeCategoryId(request.CategoryId.Value) : (KnowledgeCategoryId?)null;
        var documentId = request.DocumentId.HasValue ? new DocumentId(request.DocumentId.Value) : (DocumentId?)null;
        if (supplement is null)
        {
            supplement = new KnowledgeSupplement(
                request.Title,
                request.Content,
                priority,
                request.EffectiveAt,
                request.ExpiredAt,
                categoryId,
                documentId,
                request.IsEnabled);
            repository.Add(supplement);
            created = true;
        }
        else
        {
            supplement.Update(
                request.Title,
                request.Content,
                priority,
                request.EffectiveAt,
                request.ExpiredAt,
                categoryId,
                documentId,
                request.IsEnabled);
            repository.Update(supplement);
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                created ? "Rag.CreateKnowledgeSupplement" : "Rag.UpdateKnowledgeSupplement",
                "KnowledgeSupplement",
                supplement.Id.ToString(),
                supplement.Title,
                AuditResults.Succeeded,
                $"Knowledge supplement {(created ? "created" : "updated")}. title={supplement.Title}; priority={supplement.Priority}; enabled={supplement.IsEnabled}.",
                ["title", "content", "priority", "effectiveAt", "expiredAt", "categoryId", "documentId", "isEnabled"]),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(supplement));
    }

    internal static KnowledgeSupplementDto Map(KnowledgeSupplement supplement)
    {
        return new KnowledgeSupplementDto(
            supplement.Id,
            supplement.Title,
            supplement.Content,
            supplement.Priority.ToString(),
            supplement.EffectiveAt,
            supplement.ExpiredAt,
            supplement.CategoryId?.Value,
            supplement.DocumentId?.Value,
            supplement.IsEnabled);
    }
}

public sealed class SetKnowledgeSupplementEnabledCommandHandler(
    IRepository<KnowledgeSupplement> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<SetKnowledgeSupplementEnabledCommand, Result<KnowledgeSupplementDto>>
{
    public async Task<Result<KnowledgeSupplementDto>> Handle(
        SetKnowledgeSupplementEnabledCommand request,
        CancellationToken cancellationToken)
    {
        var supplement = await repository.GetByIdAsync(new KnowledgeSupplementId(request.Id), cancellationToken);
        if (supplement is null)
        {
            return Result.NotFound();
        }

        supplement.Update(
            supplement.Title,
            supplement.Content,
            supplement.Priority,
            supplement.EffectiveAt,
            supplement.ExpiredAt,
            supplement.CategoryId,
            supplement.DocumentId,
            request.IsEnabled);
        repository.Update(supplement);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "Rag.SetKnowledgeSupplementEnabled",
                "KnowledgeSupplement",
                supplement.Id.ToString(),
                supplement.Title,
                AuditResults.Succeeded,
                $"Knowledge supplement enabled={request.IsEnabled}.",
                ["isEnabled"]),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(UpsertKnowledgeSupplementCommandHandler.Map(supplement));
    }
}

public sealed class GetKnowledgeSupplementQueryHandler(IReadRepository<KnowledgeSupplement> repository)
    : IQueryHandler<GetKnowledgeSupplementQuery, Result<KnowledgeSupplementDto>>
{
    public async Task<Result<KnowledgeSupplementDto>> Handle(
        GetKnowledgeSupplementQuery request,
        CancellationToken cancellationToken)
    {
        var supplement = await repository.GetByIdAsync(new KnowledgeSupplementId(request.Id), cancellationToken);
        return supplement is null ? Result.NotFound() : Result.Success(UpsertKnowledgeSupplementCommandHandler.Map(supplement));
    }
}

public sealed class GetListKnowledgeSupplementsQueryHandler(IReadRepository<KnowledgeSupplement> repository)
    : IQueryHandler<GetListKnowledgeSupplementsQuery, Result<IList<KnowledgeSupplementDto>>>
{
    public async Task<Result<IList<KnowledgeSupplementDto>>> Handle(
        GetListKnowledgeSupplementsQuery request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var supplements = await repository.ListAsync(null, cancellationToken);
        IList<KnowledgeSupplementDto> result = supplements
            .Where(item => !request.CategoryId.HasValue || item.CategoryId?.Value == request.CategoryId.Value)
            .Where(item => !request.DocumentId.HasValue || item.DocumentId?.Value == request.DocumentId.Value)
            .Where(item => !request.ActiveOnly || item.CanApply(now))
            .OrderByDescending(item => item.Priority)
            .ThenByDescending(item => item.CreatedAt)
            .Select(UpsertKnowledgeSupplementCommandHandler.Map)
            .ToList();
        return Result.Success(result);
    }
}

public sealed class GetApplicableKnowledgeSupplementsQueryHandler(IReadRepository<KnowledgeSupplement> repository)
    : IQueryHandler<GetApplicableKnowledgeSupplementsQuery, Result<IList<KnowledgeSupplementHitDto>>>
{
    public async Task<Result<IList<KnowledgeSupplementHitDto>>> Handle(
        GetApplicableKnowledgeSupplementsQuery request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var supplements = await repository.ListAsync(null, cancellationToken);
        IList<KnowledgeSupplementHitDto> result = supplements
            .Where(item => item.CanApply(now))
            .Where(item => !request.CategoryId.HasValue || item.CategoryId?.Value == request.CategoryId.Value)
            .Where(item => !request.DocumentId.HasValue || item.DocumentId?.Value == request.DocumentId.Value)
            .OrderByDescending(item => item.Priority)
            .ThenByDescending(item => item.CreatedAt)
            .Select(item => new KnowledgeSupplementHitDto(
                item.Id,
                item.Title,
                item.Priority.ToString(),
                item.Content,
                item.CategoryId?.Value,
                item.DocumentId?.Value))
            .ToList();
        return Result.Success(result);
    }
}
