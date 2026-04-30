using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Ids;
using AICopilot.Core.Rag.Specifications.EmbeddingModel;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.RagService.EmbeddingModels;

public record EmbeddingModelDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required string BaseUrl { get; init; }
    public required string ModelName { get; init; }
    public int Dimensions { get; init; }
    public int MaxTokens { get; init; }
    public bool IsEnabled { get; init; }
    public bool HasApiKey { get; init; }
    public string? ApiKeyMasked { get; init; }
}

public record CreatedEmbeddingModelDto(Guid Id, string Name);

[AuthorizeRequirement("Rag.CreateEmbeddingModel")]
public record CreateEmbeddingModelCommand(
    string Name,
    string Provider,
    string BaseUrl,
    string? ApiKey,
    string ModelName,
    int Dimensions,
    int MaxTokens,
    bool IsEnabled = true) : ICommand<Result<CreatedEmbeddingModelDto>>;

public class CreateEmbeddingModelCommandHandler(IRepository<EmbeddingModel> repository)
    : ICommandHandler<CreateEmbeddingModelCommand, Result<CreatedEmbeddingModelDto>>
{
    public async Task<Result<CreatedEmbeddingModelDto>> Handle(
        CreateEmbeddingModelCommand request,
        CancellationToken cancellationToken)
    {
        var entity = new EmbeddingModel(
            request.Name,
            request.Provider,
            request.BaseUrl,
            request.ModelName,
            request.Dimensions,
            request.MaxTokens,
            request.ApiKey,
            request.IsEnabled);

        repository.Add(entity);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreatedEmbeddingModelDto(entity.Id, entity.Name));
    }
}

[AuthorizeRequirement("Rag.UpdateEmbeddingModel")]
public record UpdateEmbeddingModelCommand(
    Guid Id,
    string Name,
    string Provider,
    string BaseUrl,
    string? ApiKey,
    string ModelName,
    int Dimensions,
    int MaxTokens,
    bool IsEnabled) : ICommand<Result>;

public class UpdateEmbeddingModelCommandHandler(IRepository<EmbeddingModel> repository)
    : ICommandHandler<UpdateEmbeddingModelCommand, Result>
{
    public async Task<Result> Handle(UpdateEmbeddingModelCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(new EmbeddingModelId(request.Id), cancellationToken);
        if (entity == null)
        {
            return Result.NotFound();
        }

        var apiKey = request.ApiKey ?? entity.ApiKey;

        entity.Update(
            request.Name,
            request.Provider,
            request.BaseUrl,
            apiKey,
            request.ModelName,
            request.Dimensions,
            request.MaxTokens,
            request.IsEnabled);

        repository.Update(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

[AuthorizeRequirement("Rag.DeleteEmbeddingModel")]
public record DeleteEmbeddingModelCommand(Guid Id) : ICommand<Result>;

public class DeleteEmbeddingModelCommandHandler(IRepository<EmbeddingModel> repository)
    : ICommandHandler<DeleteEmbeddingModelCommand, Result>
{
    public async Task<Result> Handle(DeleteEmbeddingModelCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(new EmbeddingModelId(request.Id), cancellationToken);
        if (entity == null)
        {
            return Result.Success();
        }

        repository.Delete(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

[AuthorizeRequirement("Rag.GetEmbeddingModel")]
public record GetEmbeddingModelQuery(Guid Id) : IQuery<Result<EmbeddingModelDto>>;

public class GetEmbeddingModelQueryHandler(IReadRepository<EmbeddingModel> repository)
    : IQueryHandler<GetEmbeddingModelQuery, Result<EmbeddingModelDto>>
{
    public async Task<Result<EmbeddingModelDto>> Handle(
        GetEmbeddingModelQuery request,
        CancellationToken cancellationToken)
    {
        var result = await repository.FirstOrDefaultAsync(
            new EmbeddingModelByIdSpec(new EmbeddingModelId(request.Id)),
            cancellationToken);

        return result == null ? Result.NotFound() : Result.Success(EmbeddingModelDtoMapper.Map(result));
    }
}

[AuthorizeRequirement("Rag.GetListEmbeddingModels")]
public record GetListEmbeddingModelsQuery : IQuery<Result<IList<EmbeddingModelDto>>>;

public class GetListEmbeddingModelsQueryHandler(IReadRepository<EmbeddingModel> repository)
    : IQueryHandler<GetListEmbeddingModelsQuery, Result<IList<EmbeddingModelDto>>>
{
    public async Task<Result<IList<EmbeddingModelDto>>> Handle(
        GetListEmbeddingModelsQuery request,
        CancellationToken cancellationToken)
    {
        var result = await repository.ListAsync(new EmbeddingModelsOrderedSpec(), cancellationToken);
        IList<EmbeddingModelDto> items = result.Select(EmbeddingModelDtoMapper.Map).ToList();
        return Result.Success(items);
    }
}

internal static class EmbeddingModelDtoMapper
{
    public static EmbeddingModelDto Map(EmbeddingModel model)
    {
        return new EmbeddingModelDto
        {
            Id = model.Id,
            Name = model.Name,
            Provider = model.Provider,
            BaseUrl = model.BaseUrl,
            ModelName = model.ModelName,
            Dimensions = model.Dimensions,
            MaxTokens = model.MaxTokens,
            IsEnabled = model.IsEnabled,
            HasApiKey = !string.IsNullOrEmpty(model.ApiKey),
            ApiKeyMasked = string.IsNullOrEmpty(model.ApiKey) ? null : "******"
        };
    }
}
