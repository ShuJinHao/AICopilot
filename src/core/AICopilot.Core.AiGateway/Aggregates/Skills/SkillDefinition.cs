using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Skills;

public sealed class SkillDefinition : BaseEntity<SkillDefinitionId>, IAggregateRoot<SkillDefinitionId>
{
    private SkillDefinition()
    {
    }

    public SkillDefinition(
        string skillCode,
        string displayName,
        string description,
        IReadOnlyCollection<string> allowedToolCodes,
        AiToolRiskLevel riskLevel,
        string approvalPolicy,
        IReadOnlyCollection<string>? allowedDataSourceModes,
        IReadOnlyCollection<string>? allowedKnowledgeScopes,
        IReadOnlyCollection<string>? outputComponentTypes,
        bool isEnabled,
        bool isBuiltIn,
        int version,
        DateTimeOffset nowUtc)
    {
        Id = SkillDefinitionId.New();
        CreatedAt = nowUtc;
        Update(
            displayName,
            description,
            allowedToolCodes,
            riskLevel,
            approvalPolicy,
            allowedDataSourceModes,
            allowedKnowledgeScopes,
            outputComponentTypes,
            isEnabled,
            isBuiltIn,
            version,
            nowUtc,
            skillCode);
    }

    public uint RowVersion { get; private set; }

    public string SkillCode { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string[] AllowedToolCodes { get; private set; } = [];

    public AiToolRiskLevel RiskLevel { get; private set; }

    public string ApprovalPolicy { get; private set; } = "None";

    public string[] AllowedDataSourceModes { get; private set; } = [];

    public string[] AllowedKnowledgeScopes { get; private set; } = [];

    public string[] OutputComponentTypes { get; private set; } = [];

    public bool IsEnabled { get; private set; }

    public bool IsBuiltIn { get; private set; }

    public int Version { get; private set; } = 1;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool AllowsTool(string? toolCode)
    {
        return !string.IsNullOrWhiteSpace(toolCode) &&
               AllowedToolCodes.Contains(toolCode.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public void Update(
        string displayName,
        string description,
        IReadOnlyCollection<string> allowedToolCodes,
        AiToolRiskLevel riskLevel,
        string approvalPolicy,
        IReadOnlyCollection<string>? allowedDataSourceModes,
        IReadOnlyCollection<string>? allowedKnowledgeScopes,
        IReadOnlyCollection<string>? outputComponentTypes,
        bool isEnabled,
        bool isBuiltIn,
        int version,
        DateTimeOffset nowUtc)
    {
        Update(
            displayName,
            description,
            allowedToolCodes,
            riskLevel,
            approvalPolicy,
            allowedDataSourceModes,
            allowedKnowledgeScopes,
            outputComponentTypes,
            isEnabled,
            isBuiltIn,
            version,
            nowUtc,
            SkillCode);
    }

    private void Update(
        string displayName,
        string description,
        IReadOnlyCollection<string> allowedToolCodes,
        AiToolRiskLevel riskLevel,
        string approvalPolicy,
        IReadOnlyCollection<string>? allowedDataSourceModes,
        IReadOnlyCollection<string>? allowedKnowledgeScopes,
        IReadOnlyCollection<string>? outputComponentTypes,
        bool isEnabled,
        bool isBuiltIn,
        int version,
        DateTimeOffset nowUtc,
        string skillCode)
    {
        if (!Enum.IsDefined(typeof(AiToolRiskLevel), riskLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(riskLevel), riskLevel, "Skill risk level is invalid.");
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Skill version must be positive.");
        }

        var normalizedTools = NormalizeList(allowedToolCodes, 160);
        if (normalizedTools.Length == 0)
        {
            throw new ArgumentException("Skill must allow at least one governed tool.", nameof(allowedToolCodes));
        }

        SkillCode = NormalizeRequired(skillCode, nameof(skillCode), 120);
        DisplayName = NormalizeRequired(displayName, nameof(displayName), 160);
        Description = NormalizeRequired(description, nameof(description), 1000);
        AllowedToolCodes = normalizedTools;
        RiskLevel = riskLevel;
        ApprovalPolicy = NormalizeRequired(approvalPolicy, nameof(approvalPolicy), 120);
        AllowedDataSourceModes = NormalizeList(allowedDataSourceModes, 120);
        AllowedKnowledgeScopes = NormalizeList(allowedKnowledgeScopes, 120);
        OutputComponentTypes = NormalizeList(outputComponentTypes, 80);
        IsEnabled = isEnabled && riskLevel != AiToolRiskLevel.Critical;
        IsBuiltIn = isBuiltIn;
        Version = version;
        UpdatedAt = nowUtc;
    }

    private static string NormalizeRequired(string value, string paramName, int maxLength)
    {
        var normalized = NormalizeOptional(value, maxLength);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }

    private static string[] NormalizeList(IReadOnlyCollection<string>? values, int maxLength)
    {
        return (values ?? [])
            .Select(value => NormalizeOptional(value, maxLength))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
