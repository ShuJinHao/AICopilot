using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;

public class ConversationTemplate : IAggregateRoot<ConversationTemplateId>
{
    public const int MaxNameLength = 200;
    public const int MaxCodeLength = 100;
    public const int MaxDescriptionLength = 1000;
    public const int MaxSystemPromptLength = 16000;
    private static readonly string[] ForbiddenIdentityFragments =
    [
        "朝小夕",
        "朝夕",
        "小夕",
        "旧助理名",
        "旧品牌名"
    ];

    private static readonly string[] DangerousPermissivePromptFragments =
    [
        "可以绕过审批",
        "允许绕过审批",
        "无需审批",
        "不需要审批",
        "可以执行 SQL",
        "允许执行 SQL",
        "直接执行 SQL",
        "可以写入 Cloud",
        "允许写入 Cloud",
        "直接写入 Cloud",
        "可以写入云端",
        "允许写入云端",
        "直接写入云端",
        "自动重启设备",
        "直接重启设备",
        "自动下发",
        "直接下发",
        "忽略系统规则",
        "忽略安全规则"
    ];

    protected ConversationTemplate()
    {
    }

    public ConversationTemplate(
        string name,
        string description,
        string systemPrompt,
        LanguageModelId modelId,
        TemplateSpecification specification)
    {
        ValidateInfo(name, description, systemPrompt, modelId);
        ValidateSpecification(specification);

        Id = ConversationTemplateId.New();
        Name = name.Trim();
        Description = (description ?? string.Empty).Trim();
        SystemPrompt = systemPrompt.Trim();
        Specification = specification;
        ModelId = modelId;
        IsEnabled = true;
    }

    public ConversationTemplateId Id { get; private set; }

    public uint RowVersion { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Code { get; private set; }

    public string Description { get; private set; } = null!;

    public string SystemPrompt { get; private set; } = null!;

    public LanguageModelId ModelId { get; private set; }

    public ConversationTemplateScope Scope { get; private set; } = ConversationTemplateScope.General;

    public int BuiltInVersion { get; private set; }

    public bool IsBuiltIn { get; private set; }

    public TemplateSpecification Specification { get; private set; } = new();

    public bool IsEnabled { get; private set; }

    public void UpdateInfo(
        string name,
        string description,
        string systemPrompt,
        LanguageModelId modelId,
        bool isEnabled)
    {
        ValidateInfo(name, description, systemPrompt, modelId);

        Name = name.Trim();
        Description = (description ?? string.Empty).Trim();
        SystemPrompt = systemPrompt.Trim();
        ModelId = modelId;
        IsEnabled = isEnabled;
    }

    public void UpdateSpecification(TemplateSpecification spec)
    {
        ValidateSpecification(spec);
        Specification = spec;
    }

    public void MarkBuiltIn(string code, ConversationTemplateScope scope, int version)
    {
        ValidateBuiltInMetadata(code, scope, version);

        Code = code.Trim();
        Scope = scope;
        BuiltInVersion = version;
        IsBuiltIn = true;
    }

    public void ClearBuiltInMetadata()
    {
        Code = null;
        Scope = ConversationTemplateScope.General;
        BuiltInVersion = 0;
        IsBuiltIn = false;
    }

    private static void ValidateInfo(
        string name,
        string description,
        string systemPrompt,
        LanguageModelId modelId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Conversation template name is required.", nameof(name));
        }

        if (name.Trim().Length > MaxNameLength)
        {
            throw new ArgumentOutOfRangeException(nameof(name), $"Conversation template name must not exceed {MaxNameLength} characters.");
        }

        if ((description?.Length ?? 0) > MaxDescriptionLength)
        {
            throw new ArgumentOutOfRangeException(nameof(description), $"Conversation template description must not exceed {MaxDescriptionLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new ArgumentException("Conversation template system prompt is required.", nameof(systemPrompt));
        }

        if (systemPrompt.Trim().Length > MaxSystemPromptLength)
        {
            throw new ArgumentOutOfRangeException(nameof(systemPrompt), $"Conversation template system prompt must not exceed {MaxSystemPromptLength} characters.");
        }

        ValidateSystemPromptSafety(systemPrompt);

    }

    private static void ValidateSystemPromptSafety(string systemPrompt)
    {
        foreach (var fragment in ForbiddenIdentityFragments)
        {
            if (systemPrompt.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Conversation template system prompt contains a forbidden legacy assistant identity.",
                    nameof(systemPrompt));
            }
        }

        foreach (var fragment in DangerousPermissivePromptFragments)
        {
            if (systemPrompt.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Conversation template system prompt contains unsafe execution or approval-bypass instruction.",
                    nameof(systemPrompt));
            }
        }
    }

    private static void ValidateBuiltInMetadata(string code, ConversationTemplateScope scope, int version)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Conversation template code is required.", nameof(code));
        }

        if (code.Trim().Length > MaxCodeLength)
        {
            throw new ArgumentOutOfRangeException(nameof(code), $"Conversation template code must not exceed {MaxCodeLength} characters.");
        }

        if (!Enum.IsDefined(typeof(ConversationTemplateScope), scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Conversation template scope is invalid.");
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Built-in template version must be greater than zero.");
        }
    }

    private static void ValidateSpecification(TemplateSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        if (specification.MaxTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(specification), "Max tokens must be greater than zero.");
        }

        if (specification.Temperature is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(specification), "Temperature must be between 0 and 2.");
        }
    }
}
