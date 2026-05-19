using AICopilot.AiGatewayService.Safety;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.ConversationTemplate;
using AICopilot.Core.AiGateway.Specifications.LanguageModel;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.AiGatewayService.Agents;

public class ChatAgentFactory(
    IReadRepository<ConversationTemplate> templateRepository,
    IReadRepository<LanguageModel> modelRepository,
    IAgentRuntimeFactory runtimeFactory)
{
    private async Task<(LanguageModel, ConversationTemplate)> GetModelAndTemplateAsync(
        ISpecification<ConversationTemplate> specification,
        CancellationToken cancellationToken = default)
    {
        var template = await templateRepository.FirstOrDefaultAsync(specification, cancellationToken);
        if (template is null)
        {
            throw CreateConfigurationMissingException();
        }

        var model = await modelRepository.FirstOrDefaultAsync(
            new LanguageModelByIdSpec(template.ModelId),
            cancellationToken);

        if (model is null)
        {
            throw CreateConfigurationMissingException();
        }

        return (model, template);
    }

    public ScopedRuntimeAgent CreateAgent(
        LanguageModel model,
        ConversationTemplate template,
        Action<AiChatOptions>? configureOptions = null,
        string? instructionsOverride = null)
    {
        if (!model.IsEnabled)
        {
            throw new ChatWorkflowException(
                AppProblemCodes.ChatConfigurationMissing,
                $"Language model '{model.Name}' is disabled.",
                "当前模型已停用，请切换模型或联系管理员检查 AI 配置。");
        }

        if (string.IsNullOrWhiteSpace(model.ApiKey))
        {
            throw new ChatWorkflowException(
                AppProblemCodes.ChatConfigurationMissing,
                $"Language model '{model.Name}' is missing an API key.",
                "当前模型未配置 API Key，请切换模型或联系管理员补充密钥。");
        }

        if (!runtimeFactory.CanCreate(model.ProtocolType))
        {
            throw new ChatWorkflowException(
                AppProblemCodes.ChatConfigurationMissing,
                $"No chat client provider is registered for protocol '{model.ProtocolType}'.",
                "The configured model provider is unavailable. Please ask an administrator to review the AI settings.");
        }

        var chatOptions = new AiChatOptions
        {
            Instructions = instructionsOverride ?? template.SystemPrompt,
            Temperature = template.Specification.Temperature ?? model.Parameters.Temperature
        };

        configureOptions?.Invoke(chatOptions);

        return runtimeFactory.Create(new AgentRuntimeCreateRequest(model, template, chatOptions));
    }

    public async Task<ScopedRuntimeAgent> CreateAgentAsync(
        ConversationTemplateId templateId,
        LanguageModelId modelId,
        Action<AiChatOptions>? configureOptions = null)
    {
        var template = await templateRepository.FirstOrDefaultAsync(new ConversationTemplateByIdSpec(templateId));
        if (template is null)
        {
            throw CreateConfigurationMissingException();
        }

        var model = await modelRepository.FirstOrDefaultAsync(new LanguageModelByIdSpec(modelId));
        if (model is null)
        {
            throw CreateConfigurationMissingException();
        }

        return CreateAgent(model, template, configureOptions);
    }

    public async Task<ScopedRuntimeAgent> CreateAgentAsync(
        string templateName,
        LanguageModel model,
        Func<string, string>? configureInstructions = null,
        Action<AiChatOptions>? configureOptions = null)
    {
        var template = await templateRepository.FirstOrDefaultAsync(new ConversationTemplateByNameSpec(templateName));
        if (template is null)
        {
            throw CreateConfigurationMissingException();
        }

        var instructions = configureInstructions?.Invoke(template.SystemPrompt);
        return CreateAgent(model, template, configureOptions, instructions);
    }

    public async Task<ScopedRuntimeAgent> CreateAgentAsync(
        ConversationTemplateId templateId,
        Action<AiChatOptions>? configureOptions = null)
    {
        var (model, template) = await GetModelAndTemplateAsync(new ConversationTemplateByIdSpec(templateId));
        return CreateAgent(model, template, configureOptions);
    }

    public async Task<ScopedRuntimeAgent> CreateAgentAsync(
        string templateName,
        Func<string, string>? configureInstructions = null,
        Action<AiChatOptions>? configureOptions = null)
    {
        var (model, template) = await GetModelAndTemplateAsync(new ConversationTemplateByNameSpec(templateName));
        var instructions = configureInstructions?.Invoke(template.SystemPrompt);
        return CreateAgent(model, template, configureOptions, instructions);
    }

    private static ChatWorkflowException CreateConfigurationMissingException()
    {
        return new ChatWorkflowException(
            AppProblemCodes.ChatConfigurationMissing,
            "The conversation template or model configuration could not be found.",
            "This session is missing an available template or model configuration. Please ask an administrator to review the AI settings.");
    }
}
