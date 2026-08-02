using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Tools;

public enum ToolProviderType
{
    BuiltIn = 0,
    Mcp = 1,
    CloudReadonly = 2
}

public enum ToolRegistrationTargetType
{
    Plugin = 1,
    McpServer = 2
}

public enum ToolAuditLevel
{
    Minimal = 0,
    Standard = 1,
    Verbose = 2
}

public enum ToolDataBoundary
{
    NoData = 0,
    RagContextOnly = 2,
    GovernedBusinessReadOnly = 5
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
        DateTimeOffset nowUtc,
        string category = "General",
        IReadOnlyCollection<string>? businessDomains = null,
        ToolDataBoundary dataBoundary = ToolDataBoundary.NoData,
        bool isExecutableByAgent = true,
        int schemaVersion = 1,
        int catalogVersion = 1)
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
            toolCode,
            category,
            businessDomains,
            dataBoundary,
            isExecutableByAgent,
            schemaVersion,
            catalogVersion);
    }

    public uint RowVersion { get; private set; }
    public string ToolCode { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public ToolProviderType ProviderType { get; private set; }
    public ToolRegistrationTargetType TargetType { get; private set; }
    public string TargetName { get; private set; } = string.Empty;
    public string InputSchemaJson { get; private set; } = "{\"additionalProperties\":false,\"properties\":{},\"type\":\"object\"}";
    public string OutputSchemaJson { get; private set; } = "{\"additionalProperties\":false,\"properties\":{},\"type\":\"object\"}";
    public AiToolRiskLevel RiskLevel { get; private set; }
    public string? RequiredPermission { get; private set; }
    public bool RequiresApproval { get; private set; }
    public bool IsEnabled { get; private set; }
    public int TimeoutSeconds { get; private set; }
    public ToolAuditLevel AuditLevel { get; private set; }
    public string Category { get; private set; } = "General";
    public string[] BusinessDomains { get; private set; } = [];
    public ToolDataBoundary DataBoundary { get; private set; }
    public bool IsExecutableByAgent { get; private set; } = true;
    public int SchemaVersion { get; private set; } = 1;
    public int CatalogVersion { get; private set; } = 1;
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
        DateTimeOffset nowUtc,
        string? category = null,
        IReadOnlyCollection<string>? businessDomains = null,
        ToolDataBoundary? dataBoundary = null,
        bool? isExecutableByAgent = null,
        int? schemaVersion = null,
        int? catalogVersion = null)
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
            ToolCode,
            category ?? Category,
            businessDomains ?? BusinessDomains,
            dataBoundary ?? DataBoundary,
            isExecutableByAgent ?? IsExecutableByAgent,
            schemaVersion ?? SchemaVersion,
            catalogVersion ?? CatalogVersion);
    }

    public bool DisableForUnavailableContract(
        AiToolRiskLevel conservativeRiskLevel,
        DateTimeOffset nowUtc)
    {
        if (!Enum.IsDefined(conservativeRiskLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(conservativeRiskLevel));
        }

        if (!IsEnabled && !IsExecutableByAgent && RequiresApproval && RiskLevel == conservativeRiskLevel)
        {
            return false;
        }

        RiskLevel = conservativeRiskLevel;
        RequiresApproval = true;
        IsEnabled = false;
        IsExecutableByAgent = false;
        SchemaVersion = checked(SchemaVersion + 1);
        CatalogVersion = checked(CatalogVersion + 1);
        UpdatedAt = nowUtc;
        return true;
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
        string toolCode,
        string category,
        IReadOnlyCollection<string>? businessDomains,
        ToolDataBoundary dataBoundary,
        bool isExecutableByAgent,
        int schemaVersion,
        int catalogVersion)
    {
        Validate(toolCode, displayName, description, providerType, targetType, targetName, riskLevel, timeoutSeconds, auditLevel, dataBoundary, schemaVersion, catalogVersion);
        ToolCode = NormalizeRequired(toolCode, nameof(toolCode), 160);
        DisplayName = NormalizeRequired(displayName, nameof(displayName), 160);
        Description = NormalizeRequired(description, nameof(description), 1000);
        ProviderType = providerType;
        TargetType = targetType;
        TargetName = NormalizeRequired(targetName, nameof(targetName), 200);
        InputSchemaJson = NormalizeInputSchema(inputSchemaJson);
        OutputSchemaJson = NormalizeOutputSchema(outputSchemaJson, providerType);
        RiskLevel = riskLevel;
        RequiredPermission = NormalizeOptional(requiredPermission, 160);
        RequiresApproval = requiresApproval || RiskRequiresApproval(riskLevel);
        IsEnabled = isEnabled && riskLevel is not AiToolRiskLevel.Blocked and not AiToolRiskLevel.Critical;
        TimeoutSeconds = timeoutSeconds;
        AuditLevel = auditLevel;
        Category = NormalizeRequired(category, nameof(category), 120);
        BusinessDomains = NormalizeBusinessDomains(businessDomains);
        DataBoundary = dataBoundary;
        IsExecutableByAgent = isExecutableByAgent && riskLevel != AiToolRiskLevel.Critical;
        SchemaVersion = schemaVersion;
        CatalogVersion = catalogVersion;
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
        ToolAuditLevel auditLevel,
        ToolDataBoundary dataBoundary,
        int schemaVersion,
        int catalogVersion)
    {
        _ = NormalizeRequired(toolCode, nameof(toolCode), 160);
        _ = NormalizeRequired(displayName, nameof(displayName), 160);
        _ = NormalizeRequired(description, nameof(description), 1000);
        _ = NormalizeRequired(targetName, nameof(targetName), 200);
        if (!Enum.IsDefined(providerType) || !Enum.IsDefined(targetType) ||
            !Enum.IsDefined(riskLevel) || !Enum.IsDefined(auditLevel) || !Enum.IsDefined(dataBoundary))
        {
            throw new ArgumentOutOfRangeException(nameof(providerType));
        }

        if (timeoutSeconds is < 1 or > 600 || schemaVersion < 1 || catalogVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }
    }

    private static string NormalizeInputSchema(string? value)
    {
        var contract = ToolInputSchemaContractV1.Validate(value);
        return contract.IsValid
            ? contract.CanonicalJson!
            : throw new ArgumentException(contract.Error ?? "Tool input schema is outside the supported strict subset.", nameof(value));
    }

    private static string NormalizeOutputSchema(string? value, ToolProviderType providerType)
    {
        var contract = ToolOutputSchemaContractAuthority.Validate(providerType, value);
        return contract.IsValid
            ? contract.CanonicalJson!
            : throw new ArgumentException(contract.Error ?? "Tool output schema is outside the supported strict subset.", nameof(value));
    }

    private static string NormalizeRequired(string value, string paramName, int maxLength)
    {
        var normalized = NormalizeOptional(value, maxLength);
        return !string.IsNullOrWhiteSpace(normalized)
            ? normalized
            : throw new ArgumentException($"{paramName} is required.", paramName);
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }

    private static string[] NormalizeBusinessDomains(IReadOnlyCollection<string>? values) =>
        (values ?? [])
            .Select(value => NormalizeOptional(value, 120))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool RiskRequiresApproval(AiToolRiskLevel riskLevel) =>
        riskLevel is AiToolRiskLevel.RequiresApproval or AiToolRiskLevel.High or AiToolRiskLevel.Critical;
}
