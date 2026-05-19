using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.RoutingModel;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.LanguageModel;
using AICopilot.Core.AiGateway.Specifications.RoutingModel;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using static AICopilot.AiGatewayService.RoutingModels.RoutingModelConfigurationHelpers;

namespace AICopilot.AiGatewayService.RoutingModels;

[AuthorizeRequirement("AiGateway.CreateRoutingModel")]
public record CreateRoutingModelConfigurationCommand(
    string Name,
    Guid ModelId,
    bool IsActive = false) : ICommand<Result<RoutingModelConfigurationDto>>;

[AuthorizeRequirement("AiGateway.UpdateRoutingModel")]
public record UpdateRoutingModelConfigurationCommand(
    Guid Id,
    string Name,
    Guid ModelId,
    bool IsActive) : ICommand<Result>;

[AuthorizeRequirement("AiGateway.DeleteRoutingModel")]
public record DeleteRoutingModelConfigurationCommand(Guid Id) : ICommand<Result>;

[AuthorizeRequirement("AiGateway.UpdateRoutingModel")]
public record ActivateRoutingModelConfigurationCommand(Guid Id) : ICommand<Result>;

[AuthorizeRequirement("AiGateway.GetListRoutingModels")]
public record GetListRoutingModelConfigurationsQuery : IQuery<Result<IList<RoutingModelConfigurationDto>>>;

[AuthorizeRequirement("AiGateway.GetRoutingModel")]
public record GetRoutingModelConfigurationQuery(Guid Id) : IQuery<Result<RoutingModelConfigurationDto>>;

public class CreateRoutingModelConfigurationCommandHandler(
    IRepository<RoutingModelConfiguration> repository,
    IReadRepository<LanguageModel> modelRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreateRoutingModelConfigurationCommand, Result<RoutingModelConfigurationDto>>
{
    public async Task<Result<RoutingModelConfigurationDto>> Handle(
        CreateRoutingModelConfigurationCommand request,
        CancellationToken cancellationToken)
    {
        var model = await GetUsableRoutingModelAsync(modelRepository, request.ModelId, cancellationToken);
        if (model is null)
        {
            return Result.Invalid("请选择一个已启用且用途包含 Routing 的语言模型。");
        }

        var entity = new RoutingModelConfiguration(request.Name, model.Id, request.IsActive);
        if (request.IsActive)
        {
            await DeactivateAllAsync(repository, cancellationToken);
        }

        repository.Add(entity);
        await WriteAuditAsync(auditLogWriter, "AiGateway.CreateRoutingModel", entity, $"创建路由模型配置：{entity.Name}", cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(Map(entity, model));
    }
}

public class UpdateRoutingModelConfigurationCommandHandler(
    IRepository<RoutingModelConfiguration> repository,
    IReadRepository<LanguageModel> modelRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdateRoutingModelConfigurationCommand, Result>
{
    public async Task<Result> Handle(UpdateRoutingModelConfigurationCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(new RoutingModelConfigurationId(request.Id), cancellationToken);
        if (entity is null)
        {
            return Result.NotFound();
        }

        var model = await GetUsableRoutingModelAsync(modelRepository, request.ModelId, cancellationToken);
        if (model is null)
        {
            return Result.Invalid("请选择一个已启用且用途包含 Routing 的语言模型。");
        }

        entity.Update(request.Name, model.Id);
        if (request.IsActive)
        {
            await DeactivateAllAsync(repository, cancellationToken);
            entity.Activate();
        }
        else
        {
            entity.Deactivate();
        }

        repository.Update(entity);
        await WriteAuditAsync(auditLogWriter, "AiGateway.UpdateRoutingModel", entity, $"更新路由模型配置：{entity.Name}", cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public class DeleteRoutingModelConfigurationCommandHandler(
    IRepository<RoutingModelConfiguration> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<DeleteRoutingModelConfigurationCommand, Result>
{
    public async Task<Result> Handle(DeleteRoutingModelConfigurationCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(new RoutingModelConfigurationId(request.Id), cancellationToken);
        if (entity is null)
        {
            return Result.Success();
        }

        var name = entity.Name;
        repository.Delete(entity);
        await WriteAuditAsync(auditLogWriter, "AiGateway.DeleteRoutingModel", entity, $"删除路由模型配置：{name}", cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public class ActivateRoutingModelConfigurationCommandHandler(
    IRepository<RoutingModelConfiguration> repository,
    IReadRepository<LanguageModel> modelRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<ActivateRoutingModelConfigurationCommand, Result>
{
    public async Task<Result> Handle(ActivateRoutingModelConfigurationCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(new RoutingModelConfigurationId(request.Id), cancellationToken);
        if (entity is null)
        {
            return Result.NotFound();
        }

        var model = await GetUsableRoutingModelAsync(modelRepository, entity.ModelId, cancellationToken);
        if (model is null)
        {
            return Result.Invalid("路由模型引用的语言模型未启用或不支持 Routing。");
        }

        await DeactivateAllAsync(repository, cancellationToken);
        entity.Activate();
        repository.Update(entity);
        await WriteAuditAsync(auditLogWriter, "AiGateway.UpdateRoutingModel", entity, $"激活路由模型配置：{entity.Name}", cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public class GetListRoutingModelConfigurationsQueryHandler(
    IReadRepository<RoutingModelConfiguration> repository,
    IReadRepository<LanguageModel> modelRepository)
    : IQueryHandler<GetListRoutingModelConfigurationsQuery, Result<IList<RoutingModelConfigurationDto>>>
{
    public async Task<Result<IList<RoutingModelConfigurationDto>>> Handle(
        GetListRoutingModelConfigurationsQuery request,
        CancellationToken cancellationToken)
    {
        var configurations = await repository.ListAsync(new RoutingModelConfigurationsOrderedSpec(), cancellationToken);
        var models = await modelRepository.ListAsync(cancellationToken: cancellationToken);
        var modelIndex = models.ToDictionary(model => model.Id.Value);
        IList<RoutingModelConfigurationDto> result = configurations
            .Select(configuration => Map(configuration, modelIndex.GetValueOrDefault(configuration.ModelId.Value)))
            .ToList();

        return Result.Success(result);
    }
}

public class GetRoutingModelConfigurationQueryHandler(
    IReadRepository<RoutingModelConfiguration> repository,
    IReadRepository<LanguageModel> modelRepository)
    : IQueryHandler<GetRoutingModelConfigurationQuery, Result<RoutingModelConfigurationDto>>
{
    public async Task<Result<RoutingModelConfigurationDto>> Handle(
        GetRoutingModelConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        var configuration = await repository.FirstOrDefaultAsync(
            new RoutingModelConfigurationByIdSpec(new RoutingModelConfigurationId(request.Id)),
            cancellationToken);
        if (configuration is null)
        {
            return Result.NotFound();
        }

        var model = await modelRepository.FirstOrDefaultAsync(new LanguageModelByIdSpec(configuration.ModelId), cancellationToken);
        return Result.Success(Map(configuration, model));
    }
}

internal static class RoutingModelConfigurationMapping
{
    public static RoutingModelConfigurationDto Map(RoutingModelConfiguration configuration, LanguageModel? model)
    {
        return new RoutingModelConfigurationDto
        {
            Id = configuration.Id,
            Name = configuration.Name,
            ModelId = configuration.ModelId,
            ModelName = model?.Name ?? "已删除模型",
            ModelProvider = model?.Provider ?? "-",
            IsActive = configuration.IsActive
        };
    }
}

internal static partial class RoutingModelConfigurationHelpers
{
    public static async Task<LanguageModel?> GetUsableRoutingModelAsync(
        IReadRepository<LanguageModel> repository,
        Guid modelId,
        CancellationToken cancellationToken)
    {
        return await GetUsableRoutingModelAsync(repository, new LanguageModelId(modelId), cancellationToken);
    }

    public static async Task<LanguageModel?> GetUsableRoutingModelAsync(
        IReadRepository<LanguageModel> repository,
        LanguageModelId modelId,
        CancellationToken cancellationToken)
    {
        var model = await repository.FirstOrDefaultAsync(new LanguageModelByIdSpec(modelId), cancellationToken);
        return model is { IsEnabled: true } && model.SupportsUsage(LanguageModelUsage.Routing)
            ? model
            : null;
    }

    public static async Task DeactivateAllAsync(
        IRepository<RoutingModelConfiguration> repository,
        CancellationToken cancellationToken)
    {
        var configurations = await repository.ListAsync(cancellationToken: cancellationToken);
        foreach (var configuration in configurations.Where(configuration => configuration.IsActive))
        {
            configuration.Deactivate();
            repository.Update(configuration);
        }
    }

    public static Task WriteAuditAsync(
        IAuditLogWriter auditLogWriter,
        string action,
        RoutingModelConfiguration entity,
        string summary,
        CancellationToken cancellationToken)
    {
        return auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                action,
                "RoutingModelConfiguration",
                entity.Id.ToString(),
                entity.Name,
                AuditResults.Succeeded,
                summary,
                ["name", "modelId", "isActive"]),
            cancellationToken);
    }

    public static RoutingModelConfigurationDto Map(RoutingModelConfiguration configuration, LanguageModel? model)
    {
        return RoutingModelConfigurationMapping.Map(configuration, model);
    }
}
