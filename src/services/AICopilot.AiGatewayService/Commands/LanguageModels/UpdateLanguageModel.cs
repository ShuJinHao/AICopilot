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
    int? MaxTokens = null,
    int? ContextWindowTokens = null,
    int? MaxOutputTokens = null,
    string? ProtocolType = null,
    bool? IsEnabled = null,
    IReadOnlyList<string>? Usages = null,
    float? Temperature = null) : ICommand<Result>;

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

        var contextWindowTokens = request.ContextWindowTokens ?? request.MaxTokens ?? entity.Parameters.MaxTokens;
        var maxOutputTokens = request.MaxOutputTokens ?? entity.Parameters.MaxOutputTokens;
        var temperature = request.Temperature ?? entity.Parameters.Temperature;
        var usage = request.Usages is null ? entity.Usage : LanguageModelCommandMapper.ParseUsages(request.Usages);
        var protocolType = string.IsNullOrWhiteSpace(request.ProtocolType)
            ? entity.ProtocolType
            : request.ProtocolType;
        var isEnabled = request.IsEnabled ?? entity.IsEnabled;

        if (entity.Parameters.MaxTokens != contextWindowTokens)
        {
            changedFields.Add("contextWindowTokens");
        }

        if (entity.Parameters.MaxOutputTokens != maxOutputTokens)
        {
            changedFields.Add("maxOutputTokens");
        }

        if (Math.Abs(entity.Parameters.Temperature - temperature) > 0.0001f)
        {
            changedFields.Add("temperature");
        }

        if (!string.Equals(entity.ProtocolType, protocolType, StringComparison.Ordinal))
        {
            changedFields.Add("protocolType");
        }

        if (entity.Usage != usage)
        {
            changedFields.Add("usages");
        }

        if (entity.IsEnabled != isEnabled)
        {
            changedFields.Add("isEnabled");
        }

        var normalizedApiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim();
        var apiKeyChanged = request.ClearApiKey || !string.IsNullOrWhiteSpace(normalizedApiKey);
        if (apiKeyChanged)
        {
            changedFields.Add("apiKey");
        }

        var connectivityConfigChanged = changedFields.Contains("provider")
                                        || changedFields.Contains("name")
                                        || changedFields.Contains("baseUrl")
                                        || changedFields.Contains("protocolType")
                                        || apiKeyChanged;

        entity.UpdateInfo(request.Provider, request.Name, request.BaseUrl, protocolType);
        if (apiKeyChanged)
        {
            entity.UpdateApiKey(request.ClearApiKey ? null : normalizedApiKey);
        }

        entity.UpdateParameters(new ModelParameters
        {
            MaxTokens = contextWindowTokens,
            MaxOutputTokens = maxOutputTokens,
            Temperature = temperature
        });
        entity.UpdateRuntimeFlags(usage, isEnabled);
        if (connectivityConfigChanged)
        {
            entity.ResetConnectivityStatus();
        }

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
