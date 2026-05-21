using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Tools;

public enum ToolProviderType
{
    BuiltIn = 0,
    Mcp = 1,
    CloudReadonly = 2,
    Artifact = 3,
    MockMcp = 4
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

public enum ToolDataBoundary
{
    NoData = 0,
    SimulationBusinessOnly = 1,
    RagContextOnly = 2,
    ArtifactDraftOnly = 3,
    CloudReadonlySandboxOnly = 4,
    CloudReadonlyPilotReadinessOnly = 5,
    CloudReadonlyProductionPilotOnly = 6,
    CloudReadonlyProductionControlledOnly = 7
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
        bool isVisibleToPlanner = true,
        bool isExecutableByAgent = true,
        int schemaVersion = 1,
        int catalogVersion = 1,
        string? approvalPolicy = null)
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
            isVisibleToPlanner,
            isExecutableByAgent,
            schemaVersion,
            catalogVersion,
            approvalPolicy);
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

    public string Category { get; private set; } = "General";

    public string[] BusinessDomains { get; private set; } = [];

    public ToolDataBoundary DataBoundary { get; private set; } = ToolDataBoundary.NoData;

    public bool IsVisibleToPlanner { get; private set; } = true;

    public bool IsExecutableByAgent { get; private set; } = true;

    public int SchemaVersion { get; private set; } = 1;

    public int CatalogVersion { get; private set; } = 1;

    public string ApprovalPolicy { get; private set; } = "None";

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
        bool? isVisibleToPlanner = null,
        bool? isExecutableByAgent = null,
        int? schemaVersion = null,
        int? catalogVersion = null,
        string? approvalPolicy = null)
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
            isVisibleToPlanner ?? IsVisibleToPlanner,
            isExecutableByAgent ?? IsExecutableByAgent,
            schemaVersion ?? SchemaVersion,
            catalogVersion ?? CatalogVersion,
            approvalPolicy ?? ApprovalPolicy);
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
        bool isVisibleToPlanner,
        bool isExecutableByAgent,
        int schemaVersion,
        int catalogVersion,
        string? approvalPolicy)
    {
        Validate(toolCode, displayName, description, providerType, targetType, targetName, riskLevel, timeoutSeconds, auditLevel, dataBoundary, schemaVersion, catalogVersion);

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
        RequiresApproval = requiresApproval || RiskRequiresApproval(riskLevel);
        IsEnabled = isEnabled && riskLevel is not AiToolRiskLevel.Blocked and not AiToolRiskLevel.Critical;
        TimeoutSeconds = timeoutSeconds;
        AuditLevel = auditLevel;
        Category = NormalizeRequired(category, nameof(category), 120);
        BusinessDomains = NormalizeBusinessDomains(businessDomains);
        DataBoundary = dataBoundary;
        IsVisibleToPlanner = isVisibleToPlanner && riskLevel != AiToolRiskLevel.Critical;
        IsExecutableByAgent = isExecutableByAgent && riskLevel != AiToolRiskLevel.Critical;
        SchemaVersion = schemaVersion;
        CatalogVersion = catalogVersion;
        ApprovalPolicy = NormalizeRequired(approvalPolicy ?? BuildDefaultApprovalPolicy(riskLevel, RequiresApproval), nameof(approvalPolicy), 120);
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

        if (!Enum.IsDefined(typeof(ToolDataBoundary), dataBoundary))
        {
            throw new ArgumentOutOfRangeException(nameof(dataBoundary), dataBoundary, "Tool data boundary is invalid.");
        }

        if (timeoutSeconds is < 1 or > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), timeoutSeconds, "Tool timeout must be between 1 and 600 seconds.");
        }

        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "Tool schema version must be positive.");
        }

        if (catalogVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(catalogVersion), catalogVersion, "Tool catalog version must be positive.");
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

    private static string[] NormalizeBusinessDomains(IReadOnlyCollection<string>? values)
    {
        return (values ?? [])
            .Select(value => NormalizeOptional(value, 120))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool RiskRequiresApproval(AiToolRiskLevel riskLevel)
    {
        return riskLevel is AiToolRiskLevel.RequiresApproval or AiToolRiskLevel.High or AiToolRiskLevel.Critical;
    }

    private static string BuildDefaultApprovalPolicy(AiToolRiskLevel riskLevel, bool requiresApproval)
    {
        return riskLevel switch
        {
            AiToolRiskLevel.Critical => "CriticalDisabled",
            AiToolRiskLevel.High => "ToolApproval",
            AiToolRiskLevel.RequiresApproval => "ToolApproval",
            _ when requiresApproval => "ToolApproval",
            _ => "None"
        };
    }
}
