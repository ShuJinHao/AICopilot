using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;

namespace AICopilot.AgentWorkflowTestKit;

public sealed record AgentPlanV2TestStep(
    string Title,
    string Description,
    AgentStepType StepType,
    string ToolCode,
    bool RequiresApproval = false,
    string? InputJson = null);

public static class AgentPlanV2TestData
{
    public static string CreateSingleStep(
        string toolCode,
        bool executable = true,
        string? skillCode = null,
        string? inputJson = null,
        AgentTaskType taskType = AgentTaskType.ReportGeneration,
        IReadOnlyCollection<Guid>? knowledgeBaseIds = null)
    {
        return Create(
            [new AgentPlanV2TestStep(
                "生成图表数据",
                "生成图表数据。",
                AgentStepType.ChartGeneration,
                toolCode,
                InputJson: inputJson)],
            executable,
            taskType,
            skillCode,
            knowledgeBaseIds);
    }

    public static string CreateCloud(bool executable = true)
    {
        return Create(
            [
                new AgentPlanV2TestStep(
                    "Read Cloud",
                    "Read Cloud readonly data.",
                    AgentStepType.DataQuery,
                    "query_cloud_data_readonly",
                    true),
                new AgentPlanV2TestStep(
                    "Generate Markdown",
                    "Generate markdown report.",
                    AgentStepType.ArtifactGeneration,
                    "generate_markdown_report")
            ],
            executable,
            AgentTaskType.CloudDataReport,
            skillCode: null,
            knowledgeBaseIds: null);
    }

    public static string CreateRag(Guid knowledgeBaseId, bool executable = true)
    {
        return Create(
            [new AgentPlanV2TestStep(
                "Search RAG",
                "Search admin-visible knowledge base.",
                AgentStepType.RagSearch,
                "rag_search")],
            executable,
            AgentTaskType.DataAnalysis,
            skillCode: null,
            [knowledgeBaseId]);
    }

    public static string Create(
        IReadOnlyCollection<AgentPlanV2TestStep> steps,
        bool executable,
        AgentTaskType taskType,
        string? skillCode,
        IReadOnlyCollection<Guid>? knowledgeBaseIds)
    {
        var isCloud = taskType == AgentTaskType.CloudDataReport;
        var capabilityCode = isCloud ? "Analysis.Device.List" : "General.Chat";
        var candidate = new AgentIntentCandidateDocument(
            AgentPlanContractVersions.IntentV1,
            capabilityCode,
            isCloud ? AgentIntentClass.CloudOnly : AgentIntentClass.General,
            AgentIntentAvailability.Available,
            isCloud ? "CloudAiRead" : "BuiltIn",
            1,
            new AgentIntentRequiredDocument(true, AgentIntentRequiredSource.ExplicitUserGoal, null),
            new AgentIntentRequestedResourcesDocument([], [], knowledgeBaseIds ?? [], []),
            new AgentIntentFiltersDocument(null, []),
            [],
            new AgentIntentProvenanceDocument(
                "intent-router:test:v1",
                "intent-prompt:test:v1",
                AgentIntentCatalogV1.CatalogVersion,
                AgentIntentCatalogV1.CatalogDigest),
            null);
        var stepArray = steps.ToArray();
        var nodes = stepArray
            .Select((step, index) => CreateNode(step, index, capabilityCode, isCloud, knowledgeBaseIds ?? []))
            .ToArray();
        var cloudIntent = isCloud
            ? new AgentTaskPlanCloudReadonlyIntentDocument(
                "Analysis.Device.List",
                CanonicalJson.Canonicalize("""{"filters":[],"limit":20}"""),
                1,
                "Device",
                "List",
                "target=Device; kind=List; filters=0; limit=20")
            : null;
        var approvals = stepArray
            .Where(step => step.RequiresApproval)
            .Select(step => step.ToolCode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var plan = new AgentTaskPlanDocument(
            Version: 2,
            PlannerTemplateCode: "agent_planner",
            Goal: $"{taskType} test task",
            TaskType: taskType.ToString(),
            RiskLevel: AgentTaskRiskLevel.Low.ToString(),
            UploadIds: [],
            KnowledgeBaseIds: knowledgeBaseIds ?? [],
            CloudReadonlyIntent: cloudIntent,
            Steps: stepArray.Select(step => new AgentTaskPlanStepDocument(
                step.Title,
                step.Description,
                step.StepType,
                step.ToolCode,
                step.RequiresApproval,
                step.InputJson is null ? null : CanonicalJson.Canonicalize(step.InputJson))).ToArray(),
            RuntimeSettings: new AgentTaskPlanRuntimeSettingsDocument(30, 12000),
            PlannerMode: "PlanDraft",
            PlannerToolCatalogVersion: PlannerToolCatalog.CurrentVersion,
            PlannerAvailableToolCount: stepArray.Length,
            ToolCatalogVersion: PlannerToolCatalog.CurrentVersion,
            VisibleToolCount: stepArray.Length,
            SkillCode: skillCode,
            PlanKind: AgentTaskPlanKinds.PlanDraft,
            IsExecutable: false,
            CapabilityGaps: [],
            SchemaVersion: AgentPlanContractVersions.PlanV2,
            PlanId: Guid.NewGuid(),
            PlanVersion: 1,
            PlanDigest: null,
            TopologyProfile: "LinearV1",
            IntentCandidates: [candidate],
            CapabilitySelectionMode: AgentCapabilitySelectionMode.InferredFromGoal,
            RequestedCapabilityCodes: [capabilityCode],
            PluginSelectionMode: AgentPluginSelectionMode.BuiltInOnly,
            SelectedPluginIds: [],
            ArtifactTargets: [],
            Nodes: nodes,
            JoinPolicies: [],
            Budgets: new AgentPlanBudgetDocument(
                "budget-policy:v1",
                16,
                1800,
                AgentPlanContractVersions.MaxPlanCanonicalBytes),
            ApprovalSummary: new AgentPlanApprovalSummaryDocument(true, approvals),
            ExecutionSnapshot: new AgentExecutionSnapshotDocument(
                AgentPlanContractVersions.ExecutionSnapshotV1,
                AgentPlanContractVersions.PlanPolicyV1,
                PlannerToolCatalog.CurrentVersion,
                CanonicalJson.ComputeSha256("test-tools"),
                CanonicalJson.ComputeSha256("test-providers"),
                AgentIntentCatalogV1.CatalogVersion,
                AgentIntentCatalogV1.CatalogDigest,
                "agent_planner",
                "agent-planner:test:v2",
                CanonicalJson.ComputeSha256("test-prompt"),
                null,
                null,
                null,
                CanonicalJson.ComputeSha256("test-plugins"),
                CanonicalJson.ComputeSha256("test-mcp"),
                "data-contract:v1",
                "knowledge-contract:v1",
                "agent-policy:v1",
                "agent-guard:v1",
                "budget-policy:v1",
                AgentPlanContractVersions.MaxPlanCanonicalBytes),
            SecuritySummary: new AgentPlanSecuritySummaryDocument(
                true,
                false,
                false,
                false,
                false,
                false));
        var sealedPlan = new AgentPlanCanonicalizer().Seal(plan).Value!;
        return executable
            ? CanonicalJson.Serialize(sealedPlan.Document with
            {
                PlanKind = AgentTaskPlanKinds.ExecutablePlan,
                IsExecutable = true
            })
            : sealedPlan.CanonicalJson;
    }

    private static AgentPlanNodeDocument CreateNode(
        AgentPlanV2TestStep step,
        int index,
        string capabilityCode,
        bool isCloudPlan,
        IReadOnlyCollection<Guid> knowledgeBaseIds)
    {
        var nodeKind = step.ToolCode switch
        {
            "query_cloud_data_readonly" => "CloudReadNode",
            "rag_search" => "KnowledgeReadNode",
            "read_uploaded_file" => "FileReadNode",
            "generate_markdown_report" or "generate_chart_data" or "generate_pdf" or
                "generate_html_report" or "generate_pptx" or "generate_xlsx" => "ArtifactBuildNode",
            "finalize_artifacts" => "ApprovalCheckpointNode",
            _ => "DeterministicTransformNode"
        };
        var nodeInput = nodeKind == "CloudReadNode"
            ? new AgentPlanNodeInputDocument(
                "Analysis.Device.List",
                CanonicalJson.ComputeSha256(CanonicalJson.Canonicalize("""{"filters":[],"limit":20}""")),
                "CloudAiRead",
                ["Device", "List"],
                20,
                null,
                null,
                [],
                null,
                "CloudReadOnly",
                null)
            : new AgentPlanNodeInputDocument(
                null,
                null,
                null,
                [],
                null,
                null,
                null,
                [],
                null,
                null,
                step.InputJson is null ? null : CanonicalJson.Canonicalize(step.InputJson));
        return new AgentPlanNodeDocument(
            AgentPlanContractVersions.NodeV1,
            $"node-{index + 1:000}",
            nodeKind,
            index == 0 ? [] : [$"node-{index:000}"],
            true,
            "node-input:v1",
            "evidence:test:v1",
            [step.ToolCode],
            isCloudPlan && nodeKind != "CloudReadNode" ? [] : [capabilityCode],
            [],
            knowledgeBaseIds,
            [],
            nodeInput,
            null,
            new AgentPlanTimeoutPolicyDocument("timeout-policy:v1", 120),
            new AgentPlanRetryPolicyDocument("retry-policy:v1", 1, "None"),
            new AgentPlanNodeBudgetDocument(0, 0, 0),
            new AgentPlanApprovalPolicyDocument(step.RequiresApproval, step.RequiresApproval ? "UserApprovalRequired" : "None"),
            new AgentPlanIdempotencyPolicyDocument("idempotency-policy:v1", "Deterministic"),
            nodeKind == "ArtifactBuildNode" ? "ArtifactDraftOnly" : "ReadOnly",
            null);
    }
}
