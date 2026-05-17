using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Tools;

public enum ToolProviderType
{
    BuiltIn = 0,
    Mcp = 1,
    CloudReadonly = 2,
    Artifact = 3
}

public enum ToolRegistrationTargetType
{
    AgentRuntime = 0,
    Plugin = 1,
    McpServer = 2
}

public enum ToolAuditLevel
{
    Minimal = 0,
    Standard = 1,
    Verbose = 2
}

public sealed class ToolRegistration : BaseEntity<ToolRegistrationId>, IAggregateRoot<ToolRegistrationId>
{
    private ToolRegistration()
    {
    }

    public ToolRegistration(
        string toolCode,
        string displayName,
        string description,
        ToolProviderType providerType,
        ToolRegistrationTargetType targetType,
        string targetName,
        string inputSchemaJson,
        string outputSchemaJson,
        AiToolRiskLevel riskLevel,
        string? requiredPermission,
        bool requiresApproval,
        bool isEnabled,
        int timeoutSeconds,
        ToolAuditLevel auditLevel,
        DateTimeOffset nowUtc)
    {
        Id = ToolRegistrationId.New();
        CreatedAt = nowUtc;
        Update(
            displayName,
            description,
            providerType,
            targetType,
            targetName,
            inputSchemaJson,
            outputSchemaJson,
            riskLevel,
            requiredPermission,
            requiresApproval,
            isEnabled,
            timeoutSeconds,
            auditLevel,
            nowUtc,
            toolCode);
    }

    public uint RowVersion { get; private set; }

    public string ToolCode { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public ToolProviderType ProviderType { get; private set; }

    public ToolRegistrationTargetType TargetType { get; private set; }

    public string TargetName { get; private set; } = string.Empty;

    public string InputSchemaJson { get; private set; } = "{}";

    public string OutputSchemaJson { get; private set; } = "{}";

    public AiToolRiskLevel RiskLevel { get; private set; }

    public string? RequiredPermission { get; private set; }

    public bool RequiresApproval { get; private set; }

    public bool IsEnabled { get; private set; }

    public int TimeoutSeconds { get; private set; }

    public ToolAuditLevel AuditLevel { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        string displayName,
        string description,
        ToolProviderType providerType,
        ToolRegistrationTargetType targetType,
        string targetName,
        string inputSchemaJson,
        string outputSchemaJson,
        AiToolRiskLevel riskLevel,
        string? requiredPermission,
        bool requiresApproval,
        bool isEnabled,
        int timeoutSeconds,
        ToolAuditLevel auditLevel,
        DateTimeOffset nowUtc)
    {
        Update(
            displayName,
            description,
            providerType,
            targetType,
            targetName,
            inputSchemaJson,
            outputSchemaJson,
            riskLevel,
            requiredPermission,
            requiresApproval,
            isEnabled,
            timeoutSeconds,
            auditLevel,
            nowUtc,
            ToolCode);
    }

    private void Update(
        string displayName,
        string description,
        ToolProviderType providerType,
        ToolRegistrationTargetType targetType,
        string targetName,
        string inputSchemaJson,
        string outputSchemaJson,
        AiToolRiskLevel riskLevel,
        string? requiredPermission,
        bool requiresApproval,
        bool isEnabled,
        int timeoutSeconds,
        ToolAuditLevel auditLevel,
        DateTimeOffset nowUtc,
        string toolCode)
    {
        Validate(toolCode, displayName, description, providerType, targetType, targetName, riskLevel, timeoutSeconds, auditLevel);

        ToolCode = NormalizeRequired(toolCode, nameof(toolCode), 160);
        DisplayName = NormalizeRequired(displayName, nameof(displayName), 160);
        Description = NormalizeRequired(description, nameof(description), 1000);
        ProviderType = providerType;
        TargetType = targetType;
        TargetName = NormalizeRequired(targetName, nameof(targetName), 200);
        InputSchemaJson = NormalizeJson(inputSchemaJson);
        OutputSchemaJson = NormalizeJson(outputSchemaJson);
        RiskLevel = riskLevel;
        RequiredPermission = NormalizeOptional(requiredPermission, 160);
        RequiresApproval = requiresApproval || riskLevel == AiToolRiskLevel.RequiresApproval;
        IsEnabled = isEnabled && riskLevel != AiToolRiskLevel.Blocked;
        TimeoutSeconds = timeoutSeconds;
        AuditLevel = auditLevel;
        UpdatedAt = nowUtc;
    }

    private static void Validate(
        string toolCode,
        string displayName,
        string description,
        ToolProviderType providerType,
        ToolRegistrationTargetType targetType,
        string targetName,
        AiToolRiskLevel riskLevel,
        int timeoutSeconds,
        ToolAuditLevel auditLevel)
    {
        _ = NormalizeRequired(toolCode, nameof(toolCode), 160);
        _ = NormalizeRequired(displayName, nameof(displayName), 160);
        _ = NormalizeRequired(description, nameof(description), 1000);
        _ = NormalizeRequired(targetName, nameof(targetName), 200);

        if (!Enum.IsDefined(typeof(ToolProviderType), providerType))
        {
            throw new ArgumentOutOfRangeException(nameof(providerType), providerType, "Tool provider type is invalid.");
        }

        if (!Enum.IsDefined(typeof(ToolRegistrationTargetType), targetType))
        {
            throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "Tool target type is invalid.");
        }

        if (!Enum.IsDefined(typeof(AiToolRiskLevel), riskLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(riskLevel), riskLevel, "Tool risk level is invalid.");
        }

        if (!Enum.IsDefined(typeof(ToolAuditLevel), auditLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(auditLevel), auditLevel, "Tool audit level is invalid.");
        }

        if (timeoutSeconds is < 1 or > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), timeoutSeconds, "Tool timeout must be between 1 and 600 seconds.");
        }
    }

    private static string NormalizeJson(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
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
}
