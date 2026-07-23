using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public class ConfiguredAgentRuntimeFactory(
    IReadRepository<ConversationTemplate> templateRepository,
    IReadRepository<LanguageModel> modelRepository,
    IAgentRuntimeFactory runtimeFactory,
    ICurrentUser? currentUser = null)
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

        if (!template.IsEnabled)
        {
            throw CreateTemplateDisabledException();
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
        if (!template.IsEnabled)
        {
            throw CreateTemplateDisabledException();
        }

        if (!model.IsEnabled)
        {
            throw new AgentWorkflowException(
                AppProblemCodes.ChatConfigurationMissing,
                $"Language model '{model.Name}' is disabled.",
                "当前模型已停用，请切换模型或联系管理员检查 AI 配置。");
        }

        if (string.IsNullOrWhiteSpace(model.ApiKey))
        {
            throw new AgentWorkflowException(
                AppProblemCodes.ChatConfigurationMissing,
                $"Language model '{model.Name}' is missing an API key.",
                "当前模型未配置 API Key，请切换模型或联系管理员补充密钥。");
        }

        if (!runtimeFactory.CanCreate(model.ProtocolType))
        {
            throw new AgentWorkflowException(
                AppProblemCodes.ChatConfigurationMissing,
                $"No chat client provider is registered for protocol '{model.ProtocolType}'.",
                "The configured model provider is unavailable. Please ask an administrator to review the AI settings.");
        }

        var chatOptions = BuildChatOptions(model, template, configureOptions, instructionsOverride);
        var runtime = runtimeFactory.Create(
            new AgentRuntimeCreateRequest(model, template, chatOptions, CreateCallerContext()));
        return new ScopedRuntimeAgent(
            runtime.Agent,
            runtime,
            CreateConfigurationSnapshot(model, template, chatOptions));
    }

    private AgentRuntimeCallerContext? CreateCallerContext()
    {
        if (currentUser is null || !currentUser.IsAuthenticated)
        {
            return null;
        }

        return new AgentRuntimeCallerContext(
            currentUser.Id,
            currentUser.UserName,
            currentUser.Role,
            currentUser.CloudTenantId);
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

        if (!template.IsEnabled)
        {
            throw CreateTemplateDisabledException();
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

        if (!template.IsEnabled)
        {
            throw CreateTemplateDisabledException();
        }

        var instructions = configureInstructions?.Invoke(template.SystemPrompt);
        return CreateAgent(model, template, configureOptions, instructions);
    }

    public async Task<ScopedRuntimeAgent> CreateAgentAsync(
        string templateName,
        LanguageModelId modelId,
        Func<string, string>? configureInstructions = null,
        Action<AiChatOptions>? configureOptions = null,
        CancellationToken cancellationToken = default)
    {
        var template = await templateRepository.FirstOrDefaultAsync(
            new ConversationTemplateByNameSpec(templateName),
            cancellationToken);
        var model = await modelRepository.FirstOrDefaultAsync(
            new LanguageModelByIdSpec(modelId),
            cancellationToken);
        if (template is null || model is null)
        {
            throw CreateConfigurationMissingException();
        }

        if (!template.IsEnabled)
        {
            throw CreateTemplateDisabledException();
        }

        var instructions = configureInstructions?.Invoke(template.SystemPrompt);
        return CreateAgent(model, template, configureOptions, instructions);
    }

    public async Task<RuntimeAgentConfigurationSnapshot> ReadConfigurationSnapshotAsync(
        string templateName,
        LanguageModel? modelOverride,
        Func<string, string>? configureInstructions = null,
        Action<AiChatOptions>? configureOptions = null,
        CancellationToken cancellationToken = default)
    {
        var template = await templateRepository.FirstOrDefaultAsync(
            new ConversationTemplateByNameSpec(templateName),
            cancellationToken);
        if (template is null)
        {
            throw CreateConfigurationMissingException();
        }

        var model = modelOverride ?? await modelRepository.FirstOrDefaultAsync(
            new LanguageModelByIdSpec(template.ModelId),
            cancellationToken);
        if (model is null)
        {
            throw CreateConfigurationMissingException();
        }

        ValidateConfiguration(model, template);
        var instructions = configureInstructions?.Invoke(template.SystemPrompt);
        var options = BuildChatOptions(model, template, configureOptions, instructions);
        return CreateConfigurationSnapshot(model, template, options);
    }

    public async Task<RuntimeAgentConfigurationSnapshot> ReadConfigurationSnapshotAsync(
        string templateName,
        LanguageModelId modelId,
        Action<AiChatOptions>? configureOptions,
        CancellationToken cancellationToken = default)
    {
        var model = await modelRepository.FirstOrDefaultAsync(
            new LanguageModelByIdSpec(modelId),
            cancellationToken);
        if (model is null)
        {
            throw CreateConfigurationMissingException();
        }

        return await ReadConfigurationSnapshotAsync(
            templateName,
            model,
            configureOptions: configureOptions,
            cancellationToken: cancellationToken);
    }

    public Task<RuntimeAgentConfigurationSnapshot> ReadConfigurationSnapshotAsync(
        string templateName,
        LanguageModelId modelId,
        CancellationToken cancellationToken = default) =>
        ReadConfigurationSnapshotAsync(
            templateName,
            modelId,
            configureOptions: null,
            cancellationToken);

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

    private static AgentWorkflowException CreateConfigurationMissingException()
    {
        return new AgentWorkflowException(
            AppProblemCodes.ChatConfigurationMissing,
            "The conversation template or model configuration could not be found.",
            "This session is missing an available template or model configuration. Please ask an administrator to review the AI settings.");
    }

    private static AgentWorkflowException CreateTemplateDisabledException()
    {
        return new AgentWorkflowException(
            AppProblemCodes.ChatConfigurationMissing,
            "The conversation template is disabled.",
            "当前会话绑定的模板已停用，请切换模板或联系管理员检查 AI 配置。");
    }

    private static AiChatOptions BuildChatOptions(
        LanguageModel model,
        ConversationTemplate template,
        Action<AiChatOptions>? configureOptions,
        string? instructionsOverride)
    {
        var options = new AiChatOptions
        {
            Instructions = instructionsOverride ?? template.SystemPrompt,
            Temperature = template.Specification.Temperature ?? model.Parameters.Temperature,
            MaxOutputTokens = model.Parameters.MaxOutputTokens
        };
        configureOptions?.Invoke(options);
        return options;
    }

    private static RuntimeAgentConfigurationSnapshot CreateConfigurationSnapshot(
        LanguageModel model,
        ConversationTemplate template,
        AiChatOptions options)
    {
        var parameterJson = JsonSerializer.Serialize(new
        {
            contextWindowTokens = model.Parameters.MaxTokens,
            maxOutputTokens = options.MaxOutputTokens ?? model.Parameters.MaxOutputTokens,
            temperature = options.Temperature ?? model.Parameters.Temperature
        });
        var canonicalParameters = AgentCanonicalJsonV1.Canonicalize(parameterJson);
        return new RuntimeAgentConfigurationSnapshot(
            template.Id.Value,
            template.Code ?? template.Id.Value.ToString("D"),
            template.IsBuiltIn
                ? $"builtin:{template.BuiltInVersion}"
                : $"rowversion:{template.RowVersion}",
            ComputeSha256(options.Instructions ?? string.Empty),
            model.Id.Value,
            model.Name,
            model.Provider,
            model.ProtocolType,
            ComputeSha256(canonicalParameters),
            model.Parameters.MaxTokens,
            options.MaxOutputTokens ?? model.Parameters.MaxOutputTokens,
            options.Temperature ?? model.Parameters.Temperature);
    }

    private static void ValidateConfiguration(LanguageModel model, ConversationTemplate template)
    {
        if (!template.IsEnabled)
        {
            throw CreateTemplateDisabledException();
        }

        if (!model.IsEnabled || string.IsNullOrWhiteSpace(model.ApiKey))
        {
            throw CreateConfigurationMissingException();
        }
    }

    private static string ComputeSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }
}
