using AICopilot.AiGatewayService.BusinessPolicies;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.BusinessSemantics;

public interface IBusinessSemanticsCatalog
{
    IReadOnlyCollection<BusinessPolicySemanticDescriptor> GetPolicyIntents();

    bool TryGetPolicyIntent(string intent, out BusinessPolicySemanticDescriptor descriptor);

    RoutingGuidance PolicyRoutingGuidance { get; }

    IReadOnlyCollection<StructuredSemanticDescriptor> GetStructuredIntents();

    bool TryGetStructuredIntent(string intent, out StructuredSemanticDescriptor descriptor);

    RoutingGuidance StructuredRoutingGuidance { get; }

    IReadOnlyCollection<ISemanticSummaryProfile> GetSummaryProfiles();

    bool TryGetSummaryProfile(SemanticQueryTarget target, out ISemanticSummaryProfile profile);
}

public sealed record RoutingGuidance(
    IReadOnlyList<string> Rules,
    IReadOnlyList<string> PriorityRules,
    IReadOnlyList<string> Notes);

public sealed record BusinessPolicyResponseSection(
    string Key,
    string Label);

public sealed record BusinessPolicyResponseTemplate(
    IReadOnlyList<BusinessPolicyResponseSection> Sections);

public sealed record BusinessPolicySemanticDescriptor(
    BusinessPolicyDescriptor Policy,
    BusinessPolicyResponseTemplate ResponseTemplate);

public sealed record StructuredSemanticDescriptor(
    SemanticIntentDescriptor Intent,
    IReadOnlyList<string> ExampleQuestions,
    string QueryJsonExample);

public sealed class BusinessSemanticsCatalog : IBusinessSemanticsCatalog
{
    private static readonly BusinessPolicyResponseTemplate DefaultPolicyResponseTemplate = new(
    [
        new("userQuestion", "用户问题"),
        new("conclusion", "业务结论"),
        new("applicableConditions", "适用条件"),
        new("restrictedBoundaries", "禁止放宽的边界")
    ]);

    private readonly IReadOnlyDictionary<string, BusinessPolicySemanticDescriptor> _policyDescriptors;
    private readonly IReadOnlyDictionary<string, StructuredSemanticDescriptor> _structuredDescriptors;
    private readonly ISemanticSummaryProfileCatalog _summaryProfileCatalog;

    public BusinessSemanticsCatalog(
        IBusinessPolicyCatalog businessPolicyCatalog,
        ISemanticIntentCatalog semanticIntentCatalog,
        ISemanticSummaryProfileCatalog summaryProfileCatalog)
    {
        ArgumentNullException.ThrowIfNull(businessPolicyCatalog);
        ArgumentNullException.ThrowIfNull(semanticIntentCatalog);
        ArgumentNullException.ThrowIfNull(summaryProfileCatalog);

        _policyDescriptors = businessPolicyCatalog
            .GetAll()
            .Select(policy => new BusinessPolicySemanticDescriptor(policy, DefaultPolicyResponseTemplate))
            .ToDictionary(item => item.Policy.Intent, StringComparer.OrdinalIgnoreCase);

        _structuredDescriptors = semanticIntentCatalog
            .GetAll()
            .Select(CreateStructuredDescriptor)
            .ToDictionary(item => item.Intent.Intent, StringComparer.OrdinalIgnoreCase);

        _summaryProfileCatalog = summaryProfileCatalog;
    }

    public RoutingGuidance PolicyRoutingGuidance { get; } = new(
    [
        "Choose Policy.EmployeeAuthorization for questions about personnel authorization, device assignment, and function permission double checks.",
        "Choose Policy.DeviceRegistration for questions about who can register a device and whether registration is admin-only.",
        "Choose Policy.DeviceLifecycle for questions about renaming devices, changing client code, deleting devices, and historical dependency constraints.",
        "Choose Policy.BootstrapIdentity for questions about ClientCode, bootstrap, DeviceId, and upload identity rules.",
        "Choose Policy.RecipeVersioning for questions about recipe version lifecycle, active version replacement, and traceability."
    ],
    [
        "Policy.BootstrapIdentity overrides Policy.DeviceLifecycle whenever the question explicitly mentions ClientCode, bootstrap, DeviceId, or upload identity.",
        "Policy.EmployeeAuthorization overrides General.Chat when the question combines device assignment, function permission, and modify/parameter/recipe semantics."
    ],
    [
        "Policy intents should keep the user's original question text in query; they do not need structured JSON filters."
    ]);

    public RoutingGuidance StructuredRoutingGuidance { get; } = new(
    [
        "Choose Analysis.Device.* only for device master data or device status questions.",
        "Choose Analysis.DeviceLog.* only for device log questions.",
        "Choose Analysis.Recipe.* only for recipe, recipe detail, or recipe version history questions.",
        "Choose Analysis.Capacity.* only for capacity, output, yield, or qualified quantity questions.",
        "Choose Analysis.ProductionData.* only for production record, station record, or barcode trace questions."
    ],
    [
        "Recipe version/history questions should choose Analysis.Recipe.VersionHistory before Analysis.Recipe.Detail.",
        "Capacity questions with an explicit time range should choose Analysis.Capacity.Range; without a time range, process-focused questions should choose Analysis.Capacity.ByProcess before Analysis.Capacity.ByDevice.",
        "Production questions with explicit latest/最新 should choose Analysis.ProductionData.Latest; with an explicit time range choose Analysis.ProductionData.Range; otherwise choose Analysis.ProductionData.ByDevice."
    ],
    [
        "If the user question does not clearly match the structured semantic intents, fall back to General.Chat.",
        "Semantic query JSON should prefer structured filters, sort, timeRange, limit, and queryText.",
        "Typical structured questions include: 列出 LINE-A 产线设备、查看设备 DEV-001 详情、设备 DEV-001 现在是什么状态、查看设备 DEV-001 最新日志、查看配方 Recipe-Cut-01、查看 Recipe-Cut-01 的版本历史、查看 DEV-001 某时间范围的产能、查看 DEV-001 的最新生产记录。"
    ]);

    public IReadOnlyCollection<BusinessPolicySemanticDescriptor> GetPolicyIntents()
    {
        return _policyDescriptors.Values.OrderBy(item => item.Policy.Intent).ToArray();
    }

    public bool TryGetPolicyIntent(string intent, out BusinessPolicySemanticDescriptor descriptor)
    {
        return _policyDescriptors.TryGetValue(intent, out descriptor!);
    }

    public IReadOnlyCollection<StructuredSemanticDescriptor> GetStructuredIntents()
    {
        return _structuredDescriptors.Values.OrderBy(item => item.Intent.Intent).ToArray();
    }

    public bool TryGetStructuredIntent(string intent, out StructuredSemanticDescriptor descriptor)
    {
        return _structuredDescriptors.TryGetValue(intent, out descriptor!);
    }

    public IReadOnlyCollection<ISemanticSummaryProfile> GetSummaryProfiles()
    {
        return _summaryProfileCatalog.GetAll();
    }

    public bool TryGetSummaryProfile(SemanticQueryTarget target, out ISemanticSummaryProfile profile)
    {
        return _summaryProfileCatalog.TryGet(target, out profile!);
    }

    private static StructuredSemanticDescriptor CreateStructuredDescriptor(SemanticIntentDescriptor intent)
    {
        return intent.Intent switch
        {
            "Analysis.Device.List" => new(
                intent,
                ["列出 LINE-A 产线的设备"],
                """{"queryText":"列出 LINE-A 产线的设备","filters":[{"field":"lineName","operator":"eq","value":"LINE-A"}],"sort":{"field":"deviceCode","direction":"asc"},"limit":20}"""),
            "Analysis.Device.Detail" => new(
                intent,
                ["查看设备 DEV-001 的详情"],
                """{"queryText":"查看设备 DEV-001 的详情","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"limit":1}"""),
            "Analysis.Device.Status" => new(
                intent,
                ["设备 DEV-001 现在是什么状态？"],
                """{"queryText":"设备 DEV-001 现在是什么状态？","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"sort":{"field":"updatedAt","direction":"desc"},"limit":10}"""),
            "Analysis.DeviceLog.Latest" => new(
                intent,
                ["查看设备 DEV-001 最新日志"],
                """{"queryText":"查看设备 DEV-001 最新日志","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"sort":{"field":"occurredAt","direction":"desc"},"limit":10}"""),
            "Analysis.DeviceLog.Range" => new(
                intent,
                ["查看设备 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-20T23:59:59Z 的日志"],
                """{"queryText":"查看设备 DEV-001 在 2026-04-20 到 2026-04-21 的日志","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"timeRange":{"field":"occurredAt","start":"2026-04-20T00:00:00Z","end":"2026-04-21T00:00:00Z"},"sort":{"field":"occurredAt","direction":"desc"},"limit":50}"""),
            "Analysis.DeviceLog.ByLevel" => new(
                intent,
                ["查看设备 DEV-001 的错误日志"],
                """{"queryText":"查看设备 DEV-001 的错误日志","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"},{"field":"level","operator":"eq","value":"Error"}],"sort":{"field":"occurredAt","direction":"desc"},"limit":20}"""),
            "Analysis.Recipe.List" => new(
                intent,
                ["列出设备 DEV-001 的配方"],
                """{"queryText":"列出设备 DEV-001 的配方","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"sort":{"field":"updatedAt","direction":"desc"},"limit":20}"""),
            "Analysis.Recipe.Detail" => new(
                intent,
                ["查看配方 Recipe-Cut-01 详情"],
                """{"queryText":"查看配方 Recipe-Cut-01 详情","filters":[{"field":"recipeName","operator":"eq","value":"Recipe-Cut-01"}],"sort":{"field":"updatedAt","direction":"desc"},"limit":1}"""),
            "Analysis.Recipe.VersionHistory" => new(
                intent,
                ["查看配方 Recipe-Cut-01 的版本历史"],
                """{"queryText":"查看 Recipe-Cut-01 的版本历史","filters":[{"field":"recipeName","operator":"eq","value":"Recipe-Cut-01"}],"sort":{"field":"version","direction":"desc"},"limit":20}"""),
            "Analysis.Capacity.Range" => new(
                intent,
                ["查看设备 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-21T23:59:59Z 的产能"],
                """{"queryText":"查看 DEV-001 在 2026-04-20 到 2026-04-21 的产能","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"timeRange":{"field":"occurredAt","start":"2026-04-20T00:00:00Z","end":"2026-04-21T23:59:59Z"},"sort":{"field":"occurredAt","direction":"desc"},"limit":50}"""),
            "Analysis.Capacity.ByDevice" => new(
                intent,
                ["查看设备 DEV-001 的产能"],
                """{"queryText":"查看设备 DEV-001 的产能","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"sort":{"field":"occurredAt","direction":"desc"},"limit":50}"""),
            "Analysis.Capacity.ByProcess" => new(
                intent,
                ["查看 Cutting 工序的产能"],
                """{"queryText":"查看 Cutting 工序的产能","filters":[{"field":"processName","operator":"eq","value":"Cutting"}],"sort":{"field":"occurredAt","direction":"desc"},"limit":50}"""),
            "Analysis.ProductionData.Latest" => new(
                intent,
                ["查看设备 DEV-001 最新生产记录"],
                """{"queryText":"查看设备 DEV-001 最新生产记录","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"sort":{"field":"occurredAt","direction":"desc"},"limit":20}"""),
            "Analysis.ProductionData.Range" => new(
                intent,
                ["查看 DEV-001 在 2026-04-21T00:00:00Z 到 2026-04-21T23:59:59Z 的生产记录"],
                """{"queryText":"查看 DEV-001 在 2026-04-20 到 2026-04-21 的生产记录","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"timeRange":{"field":"occurredAt","start":"2026-04-20T00:00:00Z","end":"2026-04-21T23:59:59Z"},"sort":{"field":"occurredAt","direction":"desc"},"limit":50}"""),
            "Analysis.ProductionData.ByDevice" => new(
                intent,
                ["查看设备 DEV-001 的生产记录"],
                """{"queryText":"查看设备 DEV-001 的生产记录","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}],"sort":{"field":"occurredAt","direction":"desc"},"limit":50}"""),
            _ => new(
                intent,
                [],
                """{"limit":20}""")
        };
    }
}
