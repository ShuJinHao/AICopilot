using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.LanguageModels;

[AuthorizeRequirement("AiGateway.UpdateLanguageModel")]
public record UpdateLanguageModelCommand(
    Guid Id,
    string Provider,
    string Name,
    string BaseUrl,
    string? ApiKey,
    bool ClearApiKey,
    int MaxTokens,
    float Temperature) : ICommand<Result>;

public class UpdateLanguageModelCommandHandler(
    IRepository<LanguageModel> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdateLanguageModelCommand, Result>
{
    public async Task<Result> Handle(UpdateLanguageModelCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(new LanguageModelId(request.Id), cancellationToken);
        if (entity == null)
        {
            return Result.NotFound();
        }

        var changedFields = new List<string>();

        if (!string.Equals(entity.Provider, request.Provider, StringComparison.Ordinal))
        {
            changedFields.Add("provider");
        }

        if (!string.Equals(entity.Name, request.Name, StringComparison.Ordinal))
        {
            changedFields.Add("name");
        }

        if (!string.Equals(entity.BaseUrl, request.BaseUrl, StringComparison.Ordinal))
        {
            changedFields.Add("baseUrl");
        }

        if (entity.Parameters.MaxTokens != request.MaxTokens)
        {
            changedFields.Add("maxTokens");
        }

        if (Math.Abs(entity.Parameters.Temperature - request.Temperature) > 0.0001f)
        {
            changedFields.Add("temperature");
        }

        var normalizedApiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim();
        var apiKeyChanged = request.ClearApiKey || !string.IsNullOrWhiteSpace(normalizedApiKey);
        if (apiKeyChanged)
        {
            changedFields.Add("apiKey");
        }

        entity.UpdateInfo(request.Provider, request.Name, request.BaseUrl);
        if (apiKeyChanged)
        {
            entity.UpdateApiKey(request.ClearApiKey ? null : normalizedApiKey);
        }

        entity.UpdateParameters(new ModelParameters
        {
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature
        });

        repository.Update(entity);

        var changedDescription = changedFields.Count == 0
            ? "未修改业务字段"
            : string.Join("、", changedFields);
        var summary = apiKeyChanged
            ? $"更新模型配置：{entity.Name}，已修改 {changedDescription}，密钥保持脱敏"
            : $"更新模型配置：{entity.Name}，已修改 {changedDescription}";

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.UpdateLanguageModel",
                "LanguageModel",
                entity.Id.ToString(),
                entity.Name,
                AuditResults.Succeeded,
                summary,
                changedFields),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
