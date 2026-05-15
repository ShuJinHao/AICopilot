using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.LanguageModels;

public record CreatedLanguageModelDto(Guid Id, string Provider, string Name);

[AuthorizeRequirement("AiGateway.CreateLanguageModel")]
public record CreateLanguageModelCommand(
    string Provider,
    string Name,
    string BaseUrl,
    string? ApiKey,
    int? MaxTokens = null,
    int? ContextWindowTokens = null,
    int MaxOutputTokens = 1024,
    string ProtocolType = LanguageModelProtocolTypes.OpenAICompatible,
    bool IsEnabled = true,
    IReadOnlyList<string>? Usages = null,
    float Temperature = 0.7f) : ICommand<Result<CreatedLanguageModelDto>>;

public class CreateLanguageModelCommandHandler(
    IRepository<LanguageModel> repo,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreateLanguageModelCommand, Result<CreatedLanguageModelDto>>
{
    public async Task<Result<CreatedLanguageModelDto>> Handle(
        CreateLanguageModelCommand request,
        CancellationToken cancellationToken)
    {
        var contextWindowTokens = LanguageModelCommandMapper.ResolveContextWindowTokens(
            request.ContextWindowTokens,
            request.MaxTokens);

        var result = new LanguageModel(
            request.Provider,
            request.Name,
            request.BaseUrl,
            string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim(),
            new ModelParameters
            {
                MaxTokens = contextWindowTokens,
                MaxOutputTokens = request.MaxOutputTokens,
                Temperature = request.Temperature
            },
            request.ProtocolType,
            LanguageModelCommandMapper.ParseUsages(request.Usages),
            request.IsEnabled);

        repo.Add(result);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.CreateLanguageModel",
                "LanguageModel",
                result.Id.ToString(),
                result.Name,
                AuditResults.Succeeded,
                $"创建模型配置：{result.Name}",
                ["provider", "name", "baseUrl", "apiKey", "protocolType", "usages", "isEnabled", "contextWindowTokens", "maxOutputTokens", "temperature"]),
            cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreatedLanguageModelDto(result.Id, result.Provider, result.Name));
    }
}
