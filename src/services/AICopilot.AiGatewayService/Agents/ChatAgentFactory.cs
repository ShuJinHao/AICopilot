using AICopilot.AiGatewayService.Safety;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
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
        bool isSaveChatMessage = true,
        string? instructionsOverride = null)
    {
        _ = isSaveChatMessage;

        if (!runtimeFactory.CanCreate(model.Provider))
        {
            throw new ChatWorkflowException(
                AppProblemCodes.ChatConfigurationMissing,
                $"No chat client provider is registered for '{model.Provider}'.",
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
        Guid templateId,
        Action<AiChatOptions>? configureOptions = null,
        bool isSaveChatMessage = true)
    {
        var (model, template) = await GetModelAndTemplateAsync(new ConversationTemplateByIdSpec(templateId));
        return CreateAgent(model, template, configureOptions, isSaveChatMessage);
    }

    public async Task<ScopedRuntimeAgent> CreateAgentAsync(
        string templateName,
        Func<string, string>? configureInstructions = null,
        Action<AiChatOptions>? configureOptions = null,
        bool isSaveChatMessage = true)
    {
        var (model, template) = await GetModelAndTemplateAsync(new ConversationTemplateByNameSpec(templateName));
        var instructions = configureInstructions?.Invoke(template.SystemPrompt);
        return CreateAgent(model, template, configureOptions, isSaveChatMessage, instructions);
    }

    private static ChatWorkflowException CreateConfigurationMissingException()
    {
        return new ChatWorkflowException(
            AppProblemCodes.ChatConfigurationMissing,
            "The conversation template or model configuration could not be found.",
            "This session is missing an available template or model configuration. Please ask an administrator to review the AI settings.");
    }
}
