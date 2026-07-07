using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.AiGatewayService.Skills;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class PlanAgentTaskCoordinator(
    IRepository<AgentTask> repository,
    IRepository<ApprovalRequest> approvalRepository,
    IReadRepository<Session> sessionRepository,
    IReadRepository<UploadRecord> uploadRepository,
    AgentAuditRecorder auditRecorder,
    IEnumerable<IKnowledgeBaseAccessChecker> knowledgeBaseAccessCheckers,
    ICurrentUser currentUser,
    IBusinessDatabaseReadService? businessDatabaseReadService = null,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null,
    SkillDefinitionGuard? skillDefinitionGuard = null,
    IAgentSkillAutoSelector? skillAutoSelector = null,
    AgentWorkflowPipeline? workflowPipeline = null)
{
    private const int PlanDraftValidationVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<Result<AgentTaskDto>> PlanAsync(
        PlanAgentTaskCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return MissingUser();
        }

        var preparationService = new AgentTaskPlanPreparationService(
            sessionRepository,
            uploadRepository,
            knowledgeBaseAccessCheckers,
            businessDatabaseReadService);
        var preparationResult = await preparationService.PrepareAsync(
            request,
            userId,
            IsAdmin(),
            cancellationToken);
        if (!preparationResult.IsSuccess)
        {
            return Result.From(preparationResult);
        }

        var preparation = preparationResult.Value!;
        var capabilityGaps = new List<string>();
        AgentPlanDraftWorkflowResult? workflowDraft = null;
        if (workflowPipeline is not null)
        {
            workflowDraft = await workflowPipeline.RunPlanDraftWorkflowAsync(
                new ChatStreamRequest(request.SessionId, request.Goal),
                cancellationToken);
        }

        var skillSelection = await ResolveEffectiveSkillAsync(request, cancellationToken);
        skillSelection ??= SelectSkillFromWorkflow(workflowDraft);
        if (AutoSkillSelectionRequired(request) && string.IsNullOrWhiteSpace(skillSelection?.SkillCode))
        {
            capabilityGaps.Add(skillSelection?.Reason?.Trim() is { Length: > 0 } reason
                ? $"Skill 自动识别未命中：{reason}"
                : "Skill 自动识别未命中；当前仅生成通用计划草案。");
        }

        var effectiveSkillCode = skillSelection?.SkillCode;
        SkillDefinition? selectedSkill = null;
        if (skillDefinitionGuard is not null)
        {
            var skillResult = await skillDefinitionGuard.ResolveAsync(effectiveSkillCode, cancellationToken);
            if (!skillResult.IsSuccess)
            {
                if (!string.IsNullOrWhiteSpace(request.SkillCode))
                {
                    return Result.From(skillResult);
                }

                capabilityGaps.Add(DescribeProblem("Skill 自动识别结果不可用", skillResult));
                effectiveSkillCode = null;
            }
            else
            {
                selectedSkill = skillResult.Value;
            }
        }

        var effectiveTaskType = ResolveEffectiveTaskType(request.TaskType, selectedSkill);
        var effectiveQueryMode = request.QueryMode ??
            (effectiveTaskType == AgentTaskType.CloudDataReport ? "CloudReadonly" : "TextToSql");
        var effectivePlanSource = selectedSkill is null ? "FreeGoal" : $"Skill.{selectedSkill.SkillCode}";

        var riskLevel = AgentTaskPlanMetadataBuilder.DetermineRiskLevel(effectiveTaskType);
        var requestedArtifactTypes = AgentTaskPlanStepBuilder.NormalizeArtifactTypes(request.ArtifactTypes)?.ToArray();
        var skillDefaultArtifactTypes = AgentTaskPlanStepBuilder.NormalizeArtifactTypes(selectedSkill?.OutputComponentTypes)?.ToArray();
        var effectiveArtifactTypes = requestedArtifactTypes ?? skillDefaultArtifactTypes;
        var effectivePlannerMode = "PlanDraft";
        string? plannerFallbackReason = null;
        var plannerToolCatalog = BuildPlannerToolCatalog(workflowDraft);
        var plannerToolCatalogVersion = plannerToolCatalog.Version;
        var plannerToolCount = plannerToolCatalog.AvailableToolCount;
        var toolRiskSummary = AgentTaskPlanMetadataBuilder.BuildToolRiskSummary(plannerToolCatalog);
        var mockMcpOnly = PlannerToolCatalogMetadata.IsMockMcpOnly(plannerToolCatalog.Tools);
        var steps = AgentTaskPlanStepBuilder.BuildPlanSteps(
            preparation.UploadIds.Length > 0,
            preparation.KnowledgeBaseIds.Length > 0,
            preparation.HasBusinessDataSourcesForPlan,
            effectiveTaskType,
            riskLevel,
            effectiveArtifactTypes);

        var originalToolCodes = steps
            .Select(step => step.ToolCode)
            .Where(toolCode => !string.IsNullOrWhiteSpace(toolCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        steps = AgentTaskPlanStepBuilder.EnsureMandatorySteps(
            steps,
            preparation.UploadIds.Length > 0,
            preparation.KnowledgeBaseIds.Length > 0,
            preparation.HasBusinessDataSourcesForPlan,
            effectiveTaskType,
            request.RequiresDataApproval,
            effectiveArtifactTypes);
        var forcedStepCodes = steps
            .Select(step => step.ToolCode)
            .Where(toolCode => !string.IsNullOrWhiteSpace(toolCode) && !originalToolCodes.Contains(toolCode!))
            .Select(toolCode => toolCode!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        steps = riskLevel >= AgentTaskRiskLevel.High
            ? steps.Select(step => step with { RequiresApproval = true }).ToArray()
            : steps;
        var approvalCheckpoints = steps
            .Where(step => step.RequiresApproval)
            .Select(step => string.IsNullOrWhiteSpace(step.ToolCode) ? step.Title : step.ToolCode!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var toolApprovalCheckpoints = steps
            .Where(step => step.RequiresApproval && !string.IsNullOrWhiteSpace(step.ToolCode))
            .Select(step => step.ToolCode!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AgentTaskPlanCloudReadonlyIntentDocument? cloudReadonlyIntent = null;

        var plan = new AgentTaskPlanDocument(
            1,
            "agent_planner",
            request.Goal,
            effectiveTaskType.ToString(),
            riskLevel.ToString(),
            preparation.UploadIds,
            preparation.KnowledgeBaseIds,
            cloudReadonlyIntent,
            steps.Select(step => new AgentTaskPlanStepDocument(
                    step.Title,
                    step.Description,
                    step.StepType,
                    step.ToolCode,
                    step.RequiresApproval,
                    step.InputJson))
                .ToArray(),
            new AgentTaskPlanRuntimeSettingsDocument(0, 0),
            effectivePlannerMode,
            plannerFallbackReason,
            request.ModelId,
            PlanDraftValidationVersion,
            plannerToolCatalogVersion,
            plannerToolCount,
            preparation.DataSourceIds,
            preparation.BusinessDomains,
            effectiveQueryMode,
            request.RequiresDataApproval,
            effectiveArtifactTypes,
            new AgentTaskPlanSafetySummaryDocument(
                effectivePlanSource,
                effectivePlannerMode,
                PlannerModelSummary: null,
                plannerToolCatalogVersion,
                plannerToolCount,
                preparation.IsSimulationOnlyPlan,
                request.RequiresDataApproval,
                toolRiskSummary,
                mockMcpOnly),
            forcedStepCodes,
            approvalCheckpoints,
            AgentTaskPlanMetadataBuilder.BuildPlanDataSourceSummaries(preparation.SelectedDataSources),
            plannerToolCatalogVersion,
            plannerToolCount,
            toolRiskSummary,
            mockMcpOnly,
            toolApprovalCheckpoints,
            SkillCode: selectedSkill?.SkillCode,
            SkillName: selectedSkill?.DisplayName,
            SkillRoutingReason: skillSelection?.Reason,
            PlanKind: AgentTaskPlanKinds.PlanDraft,
            IsExecutable: false,
            CapabilityGaps: capabilityGaps
                .Where(gap => !string.IsNullOrWhiteSpace(gap))
                .Distinct(StringComparer.Ordinal)
                .ToArray());

        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            new SessionId(request.SessionId),
            userId,
            AgentTaskPlanMetadataBuilder.BuildTitle(request.Goal),
            request.Goal,
            effectiveTaskType,
            riskLevel,
            request.ModelId.HasValue ? new(request.ModelId.Value) : null,
            JsonSerializer.Serialize(plan, JsonOptions),
            now);

        foreach (var step in steps)
        {
            task.AddStep(step.Title, step.Description, step.StepType, step.ToolCode, step.RequiresApproval, now, step.InputJson);
        }

        var approval = new ApprovalRequest(
            task.Id,
            AgentApprovalType.Plan,
            task.Id.Value.ToString(),
            userId,
            now);
        repository.Add(task);
        approvalRepository.Add(approval);
        if (timelineProjectionWriter is not null)
        {
            await timelineProjectionWriter.StageAgentTaskPlanCreatedAsync(task, approval, cancellationToken);
        }

        await auditRecorder.RecordPlanAsync(
            task,
            AuditResults.Succeeded,
            "Agent task PlanDraft generated and is awaiting user confirmation.",
            pendingApprovalCount: 1,
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(AgentTaskDtoMapper.Map(task, pendingApprovalCount: 1));
    }

    private static Result MissingUser()
    {
        return Result.Unauthorized(new ApiProblemDescriptor(
            AuthProblemCodes.Unauthorized,
            "Current user id is missing or invalid."));
    }

    private bool IsAdmin()
    {
        return string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<AgentSkillSelection?> ResolveEffectiveSkillAsync(
        PlanAgentTaskCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SkillCode))
        {
            return new AgentSkillSelection(request.SkillCode.Trim(), "用户手动选择 Skill。");
        }

        return skillAutoSelector is null
            ? null
            : await skillAutoSelector.SelectSkillAsync(request.SessionId, request.Goal, cancellationToken);
    }

    private bool AutoSkillSelectionRequired(PlanAgentTaskCommand request)
    {
        return skillAutoSelector is not null &&
               string.IsNullOrWhiteSpace(request.SkillCode);
    }

    private static AgentTaskType ResolveEffectiveTaskType(AgentTaskType requestedTaskType, SkillDefinition? skill)
    {
        return skill?.SkillCode switch
        {
            "cloud_readonly" => AgentTaskType.CloudDataReport,
            "data_analysis" => AgentTaskType.DataAnalysis,
            "knowledge_research" => AgentTaskType.RagAnswer,
            _ => requestedTaskType
        };
    }

    private static PlannerToolCatalog BuildPlannerToolCatalog(AgentPlanDraftWorkflowResult? workflowDraft)
    {
        if (workflowDraft is null || workflowDraft.Tools.Count == 0)
        {
            return new PlannerToolCatalog(PlannerToolCatalog.CurrentVersion, 0, []);
        }

        var tools = workflowDraft.Tools
            .Select(tool =>
            {
                var toolCode = string.IsNullOrWhiteSpace(tool.ToolName) ? tool.Name : tool.ToolName!;
                var targetType = tool.TargetType?.ToString() ?? "AgentRuntime";
                var providerKind = tool.TargetType == AiToolTargetType.McpServer ? "Mcp" : "Plugin";
                return new AgentPlannerToolSummary(
                    toolCode,
                    toolCode,
                    tool.Description ?? string.Empty,
                    providerKind,
                    targetType,
                    tool.TargetName ?? string.Empty,
                    tool.JsonSchema?.GetRawText() ?? "{}",
                    tool.RequiresApproval,
                    tool.RiskLevel.ToString(),
                    ProviderKind: providerKind,
                    IsMock: IsMockTool(tool));
            })
            .ToArray();

        return new PlannerToolCatalog(
            PlannerToolCatalog.CurrentVersion,
            tools.Length,
            tools);
    }

    private static bool IsMockTool(AiToolDefinition tool)
    {
        return tool.AdditionalProperties.TryGetValue("isMock", out var isMockValue) &&
               bool.TryParse(Convert.ToString(isMockValue, System.Globalization.CultureInfo.InvariantCulture), out var isMock) &&
               isMock;
    }

    private static AgentSkillSelection? SelectSkillFromWorkflow(AgentPlanDraftWorkflowResult? workflowDraft)
    {
        var skillIntent = workflowDraft?.Intents
            .Where(intent => intent.Intent.StartsWith("Skill.", StringComparison.OrdinalIgnoreCase) ||
                             !string.IsNullOrWhiteSpace(intent.SkillCode))
            .OrderByDescending(intent => intent.Confidence)
            .FirstOrDefault();
        if (skillIntent is null)
        {
            return null;
        }

        var skillCode = !string.IsNullOrWhiteSpace(skillIntent.SkillCode)
            ? skillIntent.SkillCode
            : skillIntent.Intent["Skill.".Length..];
        if (string.IsNullOrWhiteSpace(skillCode))
        {
            return null;
        }

        var reason = skillIntent.Reason ??
                     skillIntent.Reasoning ??
                     $"Unified workflow selected Skill.{skillCode}.";
        return new AgentSkillSelection(skillCode.Trim(), reason);
    }

    private static string DescribeProblem<T>(string prefix, Result<T> result)
    {
        var problem = result.Errors?
            .OfType<ApiProblemDescriptor>()
            .FirstOrDefault();
        if (problem is not null)
        {
            return $"{prefix}: {problem.Code} - {problem.Detail}";
        }

        var detail = result.Errors?
            .Select(error => error?.ToString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(detail)
            ? prefix
            : $"{prefix}: {detail}";
    }
}
