using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record AgentTrialScenarioDto(
    string Id,
    string Title,
    string Description,
    string BusinessDomain,
    string DefaultPrompt,
    IReadOnlyCollection<Guid> DefaultDataSourceIds,
    IReadOnlyCollection<string> DefaultArtifactTypes,
    bool RequiresApproval,
    bool IsSimulationOnly,
    bool IsCloudSandboxTrial = false,
    string? SourceMode = null,
    string? SourceLabel = null);

[AuthorizeRequirement("AiGateway.PlanAgentTask")]
public sealed record GetAgentTrialScenariosQuery
    : IQuery<Result<IReadOnlyCollection<AgentTrialScenarioDto>>>;

[AuthorizeRequirement("AiGateway.PlanAgentTask")]
public sealed record CreateAgentTaskFromTrialScenarioCommand(
    Guid SessionId,
    string ScenarioId,
    string? PromptOverride = null,
    IReadOnlyCollection<string>? ArtifactTypes = null,
    IReadOnlyCollection<Guid>? DataSourceIds = null,
    string? PlannerMode = null) : ICommand<Result<AgentTaskDto>>;

public sealed class GetAgentTrialScenariosQueryHandler(
    IBusinessDatabaseReadService businessDatabaseReadService)
    : IQueryHandler<GetAgentTrialScenariosQuery, Result<IReadOnlyCollection<AgentTrialScenarioDto>>>
{
    public async Task<Result<IReadOnlyCollection<AgentTrialScenarioDto>>> Handle(
        GetAgentTrialScenariosQuery request,
        CancellationToken cancellationToken)
    {
        var dataSources = await businessDatabaseReadService.ListEnabledAsync(cancellationToken);
        return Result.Success<IReadOnlyCollection<AgentTrialScenarioDto>>(
            AgentTrialScenarioCatalog.Build(dataSources));
    }
}

public sealed class CreateAgentTaskFromTrialScenarioCommandHandler(
    IBusinessDatabaseReadService businessDatabaseReadService,
    ISender sender)
    : ICommandHandler<CreateAgentTaskFromTrialScenarioCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(
        CreateAgentTaskFromTrialScenarioCommand request,
        CancellationToken cancellationToken)
    {
        if (request.SessionId == Guid.Empty)
        {
            return Result.Invalid("SessionId is required.");
        }

        var dataSources = await businessDatabaseReadService.ListEnabledAsync(cancellationToken);
        var scenarios = AgentTrialScenarioCatalog.Build(dataSources);
        var scenario = scenarios.FirstOrDefault(item =>
            string.Equals(item.Id, request.ScenarioId, StringComparison.OrdinalIgnoreCase));
        if (scenario is null)
        {
            return Result.NotFound("Trial scenario is not available.");
        }

        if (scenario.IsCloudSandboxTrial)
        {
            if (request.DataSourceIds is { Count: > 0 })
            {
                return Result.Invalid("P7 Cloud Sandbox Trial scenarios cannot bind BusinessDatabase data sources.");
            }

            var sandboxArtifactTypes = NormalizeArtifactTypes(
                request.ArtifactTypes is { Count: > 0 }
                    ? request.ArtifactTypes
                    : scenario.DefaultArtifactTypes);
            if (sandboxArtifactTypes.Length == 0)
            {
                return Result.Invalid("At least one artifact type is required.");
            }

            var sandboxGoal = string.IsNullOrWhiteSpace(request.PromptOverride)
                ? scenario.DefaultPrompt
                : request.PromptOverride!.Trim();
            var sandboxPlanCommand = new PlanAgentTaskCommand(
                request.SessionId,
                sandboxGoal,
                AgentTaskType.CloudDataReport,
                ModelId: null,
                UploadIds: [],
                KnowledgeBaseIds: [],
                DataSourceIds: [],
                BusinessDomains: [scenario.BusinessDomain],
                QueryMode: CloudReadonlySandboxAgentTrialMarkers.SourceMode,
                RequiresDataApproval: scenario.RequiresApproval,
                ArtifactTypes: sandboxArtifactTypes,
                TrialScenarioId: scenario.Id,
                TrialScenarioTitle: scenario.Title,
                IsSimulationTrial: false,
                PlannerMode: NormalizePlannerMode(request.PlannerMode),
                IsCloudSandboxTrial: true);

            return await sender.Send(sandboxPlanCommand, cancellationToken);
        }

        var simulationIds = dataSources
            .Where(item => item.IsEnabled &&
                           item.IsReadOnly &&
                           item.IsSelectableInAgent &&
                           item.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness)
            .Select(item => item.Id)
            .ToHashSet();
        var requestedDataSourceIds = (request.DataSourceIds is { Count: > 0 }
                ? request.DataSourceIds
                : scenario.DefaultDataSourceIds)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (requestedDataSourceIds.Length == 0 ||
            requestedDataSourceIds.Any(id => !simulationIds.Contains(id)))
        {
            return Result.Invalid("P2 trial scenarios can only use authorized SimulationBusiness data sources.");
        }

        var artifactTypes = NormalizeArtifactTypes(
            request.ArtifactTypes is { Count: > 0 }
                ? request.ArtifactTypes
                : scenario.DefaultArtifactTypes);
        if (artifactTypes.Length == 0)
        {
            return Result.Invalid("At least one artifact type is required.");
        }

        var goal = string.IsNullOrWhiteSpace(request.PromptOverride)
            ? scenario.DefaultPrompt
            : request.PromptOverride!.Trim();
        var planCommand = new PlanAgentTaskCommand(
            request.SessionId,
            goal,
            AgentTaskType.ReportGeneration,
            ModelId: null,
            UploadIds: [],
            KnowledgeBaseIds: [],
            DataSourceIds: requestedDataSourceIds,
            BusinessDomains: [scenario.BusinessDomain],
            QueryMode: "TextToSql",
            RequiresDataApproval: scenario.RequiresApproval,
            ArtifactTypes: artifactTypes,
            TrialScenarioId: scenario.Id,
            TrialScenarioTitle: scenario.Title,
            IsSimulationTrial: true,
            PlannerMode: NormalizePlannerMode(request.PlannerMode));

        return await sender.Send(planCommand, cancellationToken);
    }

    private static string[] NormalizeArtifactTypes(IReadOnlyCollection<string> artifactTypes)
    {
        var allowed = new HashSet<string>(
            ["Chart", "Markdown", "Html", "Pdf", "Pptx", "Xlsx"],
            StringComparer.OrdinalIgnoreCase);
        return artifactTypes
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Where(item => allowed.Contains(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePlannerMode(string? plannerMode)
    {
        if (string.IsNullOrWhiteSpace(plannerMode))
        {
            return nameof(AgentPlannerMode.Auto);
        }

        return Enum.TryParse<AgentPlannerMode>(plannerMode.Trim(), ignoreCase: true, out var mode)
            ? mode.ToString()
            : nameof(AgentPlannerMode.Auto);
    }
}

internal static class AgentTrialScenarioCatalog
{
    private const string SimulationSourceLabel = "AI 独立模拟业务库";

    public static IReadOnlyCollection<AgentTrialScenarioDto> Build(
        IReadOnlyCollection<BusinessDatabaseDescriptor> dataSources)
    {
        var simulationSourceIds = dataSources
            .Where(item => item.IsEnabled &&
                           item.IsReadOnly &&
                           item.IsSelectableInAgent &&
                           item.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Id)
            .ToArray();
        IEnumerable<AgentTrialScenarioDto> simulationDefinitions = simulationSourceIds.Length == 0
            ? []
            : Definitions.Select(item => item.ToDto(simulationSourceIds));

        return simulationDefinitions
            .Concat(CloudSandboxDefinitions.Select(item => item.ToDto([])))
            .ToArray();
    }

    private static readonly IReadOnlyCollection<AgentTrialScenarioDefinition> Definitions =
    [
        new(
            "capacity-analysis",
            "产能分析",
            "按产线、班次和计划达成率生成内部试用产能分析草稿。",
            "Production",
            $"使用 {SimulationSourceLabel} 分析最近 30 天产能、计划达成率、异常产线和改进建议。输出必须标记 SimulationBusiness、数据源名称、query hash、行数和截断状态。",
            ["Chart", "Markdown", "Html", "Pptx", "Xlsx"]),
        new(
            "quality-defects",
            "质量缺陷",
            "按产品、缺陷类型和严重度定位质量问题。",
            "Quality",
            $"使用 {SimulationSourceLabel} 分析质量检验缺陷分布、严重缺陷趋势、责任工序和整改建议。输出必须保留 SimulationBusiness 标识和 query hash。",
            ["Chart", "Markdown", "Html", "Pptx", "Xlsx"]),
        new(
            "device-downtime",
            "设备停机",
            "按设备、停机原因和持续时间汇总设备事件。",
            "Equipment",
            $"使用 {SimulationSourceLabel} 分析设备停机、故障原因、影响产线和恢复建议。输出必须展示 sourceMode=SimulationBusiness、isSimulation=true 和 query hash。",
            ["Chart", "Markdown", "Html", "Pptx"]),
        new(
            "inventory-turnover",
            "库存周转",
            "按物料、仓库和采购状态生成库存周转视图。",
            "Inventory",
            $"使用 {SimulationSourceLabel} 分析库存移动、呆滞风险、采购到货和周转建议。输出必须展示模拟库标识、行数和截断状态。",
            ["Chart", "Markdown", "Html", "Xlsx"]),
        new(
            "sales-delivery",
            "销售交付",
            "按客户、订单状态和交付延误汇总销售履约。",
            "Sales",
            $"使用 {SimulationSourceLabel} 分析销售订单、交付记录、客户投诉和延迟风险。输出必须展示 SimulationBusiness 和 query hash，不得伪装成真实客户数据。",
            ["Chart", "Markdown", "Html", "Pptx", "Xlsx"]),
        new(
            "employee-policy-rag",
            "员工制度/RAG 补充说明",
            "验证员工考勤制度、RAG 分类和 CriticalOverride 补充说明的试用口径。",
            "EmployeePolicy",
            $"使用 {SimulationSourceLabel} 结合员工制度 RAG 分类和 KnowledgeSupplement CriticalOverride 规则，回答婚假、考勤异常和制度补充说明场景。输出必须声明这是模拟制度试用内容，不得作为真实企业制度发布。",
            ["Markdown", "Html", "Pdf"])
    ];

    private static readonly IReadOnlyCollection<AgentTrialScenarioDefinition> CloudSandboxDefinitions =
    [
        new(
            "cloud-sandbox-devices",
            "Cloud Sandbox 设备清单",
            "仅用于 Cloud 只读 Sandbox（非生产）受控试用，查询设备清单并生成草稿产物。",
            "Device",
            "使用 Cloud 只读 Sandbox（非生产）查询设备清单，输出必须展示 sourceMode=CloudReadonlySandbox、isSandbox=true、endpoint code、hash、行数、截断状态和审批状态。",
            ["Markdown", "Html"],
            IsSimulationOnly: false,
            IsCloudSandboxTrial: true),
        new(
            "cloud-sandbox-capacity-summary",
            "Cloud Sandbox 产能汇总",
            "仅用于 Cloud 只读 Sandbox（非生产）受控试用，查询产能汇总并生成图表/报告草稿。",
            "Capacity",
            "使用 Cloud 只读 Sandbox（非生产）查询产能汇总，生成图表和报告草稿，必须保留 CloudReadonlySandbox 非生产标识、endpoint code、query/result hash、行数和截断状态。",
            ["Chart", "Markdown", "Html", "Pptx"],
            IsSimulationOnly: false,
            IsCloudSandboxTrial: true),
        new(
            "cloud-sandbox-device-logs",
            "Cloud Sandbox 设备日志",
            "仅用于 Cloud 只读 Sandbox（非生产）受控试用，查询设备日志并生成审计友好草稿。",
            "Equipment",
            "使用 Cloud 只读 Sandbox（非生产）查询设备日志，输出必须展示 endpoint code、hash、非生产标识和审批状态，不得伪装成生产数据。",
            ["Markdown", "Html", "Xlsx"],
            IsSimulationOnly: false,
            IsCloudSandboxTrial: true),
        new(
            "cloud-sandbox-pass-station-records",
            "Cloud Sandbox 过站记录",
            "仅用于 Cloud 只读 Sandbox（非生产）受控试用，查询过站记录并生成图表和表格草稿。",
            "Production",
            "使用 Cloud 只读 Sandbox（非生产）查询过站记录，输出必须包含 CloudReadonlySandbox、isSandbox=true、endpoint code、hash、行数和截断状态。",
            ["Chart", "Markdown", "Html", "Xlsx"],
            IsSimulationOnly: false,
            IsCloudSandboxTrial: true),
        new(
            "cloud-sandbox-device-exception-analysis",
            "Cloud Sandbox 设备异常分析",
            "仅用于 Cloud 只读 Sandbox（非生产）受控试用，基于设备日志做异常分析草稿。",
            "Equipment",
            "使用 Cloud 只读 Sandbox（非生产）分析设备异常趋势，必须显示 endpoint code、hash、非生产标识、行数、截断状态和审批状态。",
            ["Chart", "Markdown", "Html", "Pdf"],
            IsSimulationOnly: false,
            IsCloudSandboxTrial: true),
        new(
            "cloud-sandbox-capacity-delivery-analysis",
            "Cloud Sandbox 产能交付分析",
            "仅用于 Cloud 只读 Sandbox（非生产）受控试用，基于产能汇总做交付风险草稿。",
            "Delivery",
            "使用 Cloud 只读 Sandbox（非生产）分析产能交付风险，所有产物必须保留 CloudReadonlySandbox 非生产标识、endpoint code、hash、行数和截断状态。",
            ["Chart", "Markdown", "Html", "Pptx", "Xlsx"],
            IsSimulationOnly: false,
            IsCloudSandboxTrial: true)
    ];

    private sealed record AgentTrialScenarioDefinition(
        string Id,
        string Title,
        string Description,
        string BusinessDomain,
        string DefaultPrompt,
        IReadOnlyCollection<string> DefaultArtifactTypes,
        bool IsSimulationOnly = true,
        bool IsCloudSandboxTrial = false)
    {
        public AgentTrialScenarioDto ToDto(IReadOnlyCollection<Guid> dataSourceIds)
        {
            return new AgentTrialScenarioDto(
                Id,
                Title,
                Description,
                BusinessDomain,
                DefaultPrompt,
                dataSourceIds,
                DefaultArtifactTypes,
                RequiresApproval: true,
                IsSimulationOnly,
                IsCloudSandboxTrial,
                SourceMode: IsCloudSandboxTrial
                    ? CloudReadonlySandboxAgentTrialMarkers.SourceMode
                    : "SimulationBusiness",
                SourceLabel: IsCloudSandboxTrial
                    ? CloudReadonlySandboxAgentTrialMarkers.SourceLabel
                    : SimulationSourceLabel);
        }
    }
}
