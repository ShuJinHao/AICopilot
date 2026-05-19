using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.ConversationTemplates;

public sealed record ResetBuiltInConversationTemplatesDto(
    int CreatedCount,
    int UpdatedCount,
    IReadOnlyCollection<string> TemplateCodes);

[AuthorizeRequirement("AiGateway.UpdateConversationTemplate")]
public sealed record ResetBuiltInConversationTemplatesCommand(Guid ModelId)
    : ICommand<Result<ResetBuiltInConversationTemplatesDto>>;

public sealed class ResetBuiltInConversationTemplatesCommandHandler(
    IRepository<ConversationTemplate> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<ResetBuiltInConversationTemplatesCommand, Result<ResetBuiltInConversationTemplatesDto>>
{
    public async Task<Result<ResetBuiltInConversationTemplatesDto>> Handle(
        ResetBuiltInConversationTemplatesCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ModelId == Guid.Empty)
        {
            return Result.Invalid("ModelId is required when resetting built-in conversation templates.");
        }

        var modelId = new LanguageModelId(request.ModelId);
        var existingTemplates = await repository.GetListAsync(
            template => template.Code != null,
            cancellationToken);
        var existingByCode = existingTemplates
            .Where(template => !string.IsNullOrWhiteSpace(template.Code))
            .ToDictionary(template => template.Code!, StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var updated = 0;
        foreach (var definition in BuiltInConversationTemplates.All)
        {
            if (existingByCode.TryGetValue(definition.Code, out var template))
            {
                template.UpdateInfo(
                    definition.Name,
                    definition.Description,
                    definition.SystemPrompt,
                    modelId,
                    isEnabled: true);
                template.UpdateSpecification(new TemplateSpecification());
                template.MarkBuiltIn(definition.Code, definition.Scope, definition.Version);
                repository.Update(template);
                updated++;
                continue;
            }

            repository.Add(BuiltInConversationTemplates.CreateTemplate(definition, modelId));
            created++;
        }

        var codes = BuiltInConversationTemplates.All
            .Select(definition => definition.Code)
            .ToArray();

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.ResetBuiltInConversationTemplates",
                "ConversationTemplate",
                null,
                "A助理内置模板",
                AuditResults.Succeeded,
                $"重置 A助理内置模板：新增 {created} 个，更新 {updated} 个。",
                ["code", "scope", "builtInVersion", "systemPrompt"],
                new Dictionary<string, string>
                {
                    ["codes"] = string.Join(',', codes)
                }),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(new ResetBuiltInConversationTemplatesDto(created, updated, codes));
    }
}
