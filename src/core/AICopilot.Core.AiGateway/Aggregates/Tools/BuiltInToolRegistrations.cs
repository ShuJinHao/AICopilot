using AICopilot.SharedKernel.Ai;

namespace AICopilot.Core.AiGateway.Aggregates.Tools;

public sealed record ToolRegistrationSeed(
    string ToolCode,
    string DisplayName,
    string Description,
    ToolProviderType ProviderType,
    ToolRegistrationTargetType TargetType,
    string TargetName,
    string InputSchemaJson,
    string OutputSchemaJson,
    AiToolRiskLevel RiskLevel,
    string? RequiredPermission,
    bool RequiresApproval,
    bool IsEnabled,
    int TimeoutSeconds,
    ToolAuditLevel AuditLevel,
    string Category = "General",
    IReadOnlyCollection<string>? BusinessDomains = null,
    ToolDataBoundary DataBoundary = ToolDataBoundary.NoData,
    bool IsVisibleToPlanner = true,
    bool IsExecutableByAgent = true,
    int SchemaVersion = 1,
    int CatalogVersion = BuiltInToolRegistrations.CurrentCatalogVersion,
    string ApprovalPolicy = "None");

public static class BuiltInToolRegistrations
{
    public const int CurrentCatalogVersion = 18;
    public const int CurrentSchemaVersion = 3;
    public const string FinalizationCheckpointToolCode = "finalize_artifacts";

    private const string AgentRuntimeTarget = "AgentTaskRuntime";
    private const string ArtifactWorkspaceLifecycleTarget = "ArtifactWorkspaceLifecycleCoordinator";
    private const string MockMcpTarget = "MockMcpProvider";
    // Built-in runtime arguments are frozen in the Plan and execution snapshot. The
    // dispatcher does not consume arbitrary per-step arguments, so their exact input
    // contract is the empty object instead of an open JSON object.
    private const string EmptyObjectInputSchema =
        """{"type":"object","properties":{},"additionalProperties":false}""";
    private const string UploadSummaryOutputSchema =
        """{"type":"object","properties":{"status":{"type":"string","enum":["completed"]},"resultType":{"type":"string","enum":["upload-summary"]},"itemCount":{"type":"integer"}},"required":["status","resultType","itemCount"],"additionalProperties":false}""";
    private const string TableSummaryOutputSchema =
        """{"type":"object","properties":{"status":{"type":"string","enum":["completed"]},"resultType":{"type":"string","enum":["table-summary"]},"itemCount":{"type":"integer"},"rowCount":{"type":"integer"}},"required":["status","resultType","itemCount","rowCount"],"additionalProperties":false}""";
    private const string RagSummaryOutputSchema =
        """{"type":"object","properties":{"status":{"type":"string","enum":["completed"]},"resultType":{"type":"string","enum":["rag-summary"]},"itemCount":{"type":"integer"},"lowConfidence":{"type":"boolean"}},"required":["status","resultType","itemCount","lowConfidence"],"additionalProperties":false}""";
    private const string CloudQuerySummaryOutputSchema =
        """{"type":"object","properties":{"status":{"type":"string","enum":["completed"]},"resultType":{"type":"string","enum":["cloud-query-summary"]},"sourceMode":{"type":"string"},"isSimulation":{"type":"boolean"},"rowCount":{"type":"integer"},"isTruncated":{"type":"boolean"},"resultHash":{"type":"string"}},"required":["status","resultType","sourceMode","isSimulation","rowCount","isTruncated","resultHash"],"additionalProperties":false}""";
    private const string BusinessQuerySummaryOutputSchema =
        """{"type":"object","properties":{"status":{"type":"string","enum":["completed"]},"resultType":{"type":"string","enum":["business-query-summary"]},"sourceMode":{"type":"string"},"isSimulation":{"type":"boolean"},"rowCount":{"type":"integer"},"isTruncated":{"type":"boolean"},"resultHash":{"type":"string"}},"required":["status","resultType","sourceMode","isSimulation","rowCount","isTruncated","resultHash"],"additionalProperties":false}""";
    private const string EvidenceJoinOutputSchema =
        """{"type":"object","properties":{"status":{"type":"string","enum":["completed"]},"resultType":{"type":"string","enum":["evidence-join"]},"joinPolicy":{"type":"string","enum":["AllRequired","OptionalBestEffort"]},"requiredEvidenceCount":{"type":"integer"},"optionalEvidenceCount":{"type":"integer"},"missingOptionalCount":{"type":"integer"}},"required":["status","resultType","joinPolicy","requiredEvidenceCount","optionalEvidenceCount","missingOptionalCount"],"additionalProperties":false}""";
    private const string AgentReasoningOutputSchema =
        """{"type":"object","properties":{"status":{"type":"string","enum":["completed"]},"resultType":{"type":"string","enum":["agent-reasoning"]},"childRunId":{"type":"string"},"completionStatus":{"type":"string","enum":["Completed"]},"truthClass":{"type":"string","enum":["LlmInference"]},"safeSummary":{"type":"string"},"findings":{"type":"array","items":{"type":"string"}},"citationRefs":{"type":"array","items":{"type":"string"}},"evidenceWarnings":{"type":"array","items":{"type":"string"}},"conflictStatus":{"type":"string","enum":["None","PotentialConflict"]},"confidence":{"type":"number"},"noFurtherToolCalls":{"type":"boolean","enum":[true]},"recoveryUsed":{"type":"boolean"},"modelCalls":{"type":"integer"}},"required":["status","resultType","childRunId","completionStatus","truthClass","safeSummary","findings","citationRefs","evidenceWarnings","conflictStatus","confidence","noFurtherToolCalls","recoveryUsed","modelCalls"],"additionalProperties":false}""";
    private const string CloudHealthAssessmentOutputSchema =
        """{"type":"object","properties":{"status":{"type":"string","enum":["completed"]},"resultType":{"type":"string","enum":["cloud-health-assessment"]},"algorithmVersion":{"type":"string","enum":["cloud-health-assessment:v1"]},"assessmentType":{"type":"string","enum":["CurrentDeviceRuntimeHealth"]},"truthClass":{"type":"string","enum":["DerivedFact"]},"healthScore":{"type":"integer"},"healthLevel":{"type":"string","enum":["Stable","Watch","Attention","DataInsufficient"]},"safeSummary":{"type":"string"},"findings":{"type":"array","items":{"type":"string"}},"confidence":{"type":"number"},"missingRate":{"type":"number"},"inputEvidenceCount":{"type":"integer"},"evidenceSetDigest":{"type":"string"},"sourceAsOfUtc":{"type":"string"},"sourceMode":{"type":"string"},"isSimulation":{"type":"boolean"},"rowCount":{"type":"integer"},"isTruncated":{"type":"boolean"},"typedMetrics":{"type":"object","properties":{"futureHeartbeatCount":{"type":"number"},"missingHeartbeatCount":{"type":"number"},"reportedIssueStatusCount":{"type":"number"},"staleHeartbeatCount":{"type":"number"},"totalDeviceCount":{"type":"number"},"unknownRuntimeStatusCount":{"type":"number"}},"required":["futureHeartbeatCount","missingHeartbeatCount","reportedIssueStatusCount","staleHeartbeatCount","totalDeviceCount","unknownRuntimeStatusCount"],"additionalProperties":false}},"required":["status","resultType","algorithmVersion","assessmentType","truthClass","healthScore","healthLevel","safeSummary","findings","confidence","missingRate","inputEvidenceCount","evidenceSetDigest","sourceAsOfUtc","sourceMode","isSimulation","rowCount","isTruncated","typedMetrics"],"additionalProperties":false}""";
    private const string ArtifactOutputSchema =
        """{"type":"object","properties":{"status":{"type":"string","enum":["completed"]},"resultType":{"type":"string","enum":["artifact"]},"artifactType":{"type":"string","enum":["chart","markdown","html","pdf","pptx","xlsx"]},"artifactId":{"type":"string"}},"required":["status","resultType","artifactType","artifactId"],"additionalProperties":false}""";
    private const string FinalizationCheckpointOutputSchema =
        """{"type":"object","properties":{"status":{"type":"string","enum":["finalized"]},"resultType":{"type":"string","enum":["finalization-checkpoint"]}},"required":["status","resultType"],"additionalProperties":false}""";
    private const string MockHealthOutputSchema =
        """{"type":"object","properties":{"isMock":{"type":"boolean"},"providerKind":{"type":"string","enum":["MockMcp"]},"toolCode":{"type":"string","enum":["mock_mcp_health_check"]},"toolRunId":{"type":"string"},"toolCatalogVersion":{"type":"integer"},"schemaVersion":{"type":"integer"},"status":{"type":"string","enum":["Succeeded"]},"durationMs":{"type":"integer"},"resultHash":{"type":"string"},"payload":{"type":"object","properties":{"health":{"type":"string","enum":["Healthy"]},"mockOnly":{"type":"boolean"},"externalEndpointEnabled":{"type":"boolean"},"checkedAt":{"type":"string"}},"required":["health","mockOnly","externalEndpointEnabled","checkedAt"],"additionalProperties":false}},"required":["isMock","providerKind","toolCode","toolRunId","toolCatalogVersion","schemaVersion","status","durationMs","resultHash","payload"],"additionalProperties":false}""";
    private const string MockKpiOutputSchema =
        """{"type":"object","properties":{"isMock":{"type":"boolean"},"providerKind":{"type":"string","enum":["MockMcp"]},"toolCode":{"type":"string","enum":["mock_mcp_kpi_formula_lookup"]},"toolRunId":{"type":"string"},"toolCatalogVersion":{"type":"integer"},"schemaVersion":{"type":"integer"},"status":{"type":"string","enum":["Succeeded"]},"durationMs":{"type":"integer"},"resultHash":{"type":"string"},"payload":{"type":"object","properties":{"domain":{"type":"string"},"formula":{"type":"string"},"source":{"type":"string","enum":["Mock MCP KPI formula catalog"]},"isSimulationSupport":{"type":"boolean"}},"required":["domain","formula","source","isSimulationSupport"],"additionalProperties":false}},"required":["isMock","providerKind","toolCode","toolRunId","toolCatalogVersion","schemaVersion","status","durationMs","resultHash","payload"],"additionalProperties":false}""";
    private const string MockArtifactOutputSchema =
        """{"type":"object","properties":{"isMock":{"type":"boolean"},"providerKind":{"type":"string","enum":["MockMcp"]},"toolCode":{"type":"string","enum":["mock_mcp_artifact_quality_check"]},"toolRunId":{"type":"string"},"toolCatalogVersion":{"type":"integer"},"schemaVersion":{"type":"integer"},"status":{"type":"string","enum":["Succeeded"]},"durationMs":{"type":"integer"},"resultHash":{"type":"string"},"payload":{"type":"object","properties":{"artifactType":{"type":"string"},"passed":{"type":"boolean"},"checks":{"type":"object","properties":{"simulationMarker":{"type":"boolean"},"queryHash":{"type":"boolean"},"noRealExternalSideEffect":{"type":"boolean"}},"required":["simulationMarker","queryHash","noRealExternalSideEffect"],"additionalProperties":false}},"required":["artifactType","passed","checks"],"additionalProperties":false}},"required":["isMock","providerKind","toolCode","toolRunId","toolCatalogVersion","schemaVersion","status","durationMs","resultHash","payload"],"additionalProperties":false}""";
    private const string MockTicketOutputSchema =
        """{"type":"object","properties":{"isMock":{"type":"boolean"},"providerKind":{"type":"string","enum":["MockMcp"]},"toolCode":{"type":"string","enum":["mock_mcp_external_ticket_preview"]},"toolRunId":{"type":"string"},"toolCatalogVersion":{"type":"integer"},"schemaVersion":{"type":"integer"},"status":{"type":"string","enum":["Succeeded"]},"durationMs":{"type":"integer"},"resultHash":{"type":"string"},"payload":{"type":"object","properties":{"title":{"type":"string"},"summary":{"type":"string"},"sideEffectExecuted":{"type":"boolean"},"previewOnly":{"type":"boolean"},"externalSystem":{"type":"string","enum":["mock-ticket-system"]}},"required":["title","summary","sideEffectExecuted","previewOnly","externalSystem"],"additionalProperties":false}},"required":["isMock","providerKind","toolCode","toolRunId","toolCatalogVersion","schemaVersion","status","durationMs","resultHash","payload"],"additionalProperties":false}""";
    private const string MockHealthInputSchema =
        """{"type":"object","properties":{"mockBehavior":{"type":"string","enum":["slow","timeout","500","schema-invalid"]}},"additionalProperties":false}""";
    private const string MockKpiInputSchema =
        """{"type":"object","properties":{"domain":{"type":"string","enum":["Production","Quality","Inventory","Sales","Employee"]},"mockBehavior":{"type":"string","enum":["slow","timeout","500","schema-invalid"]}},"additionalProperties":false}""";
    private const string MockArtifactInputSchema =
        """{"type":"object","properties":{"artifactType":{"type":"string"},"contentPreview":{"type":"string"},"mockBehavior":{"type":"string","enum":["slow","timeout","500","schema-invalid"]}},"additionalProperties":false}""";
    private const string MockTicketInputSchema =
        """{"type":"object","properties":{"title":{"type":"string"},"summary":{"type":"string"},"mockBehavior":{"type":"string","enum":["slow","timeout","500","schema-invalid"]}},"required":["title"],"additionalProperties":false}""";

    public static IReadOnlyCollection<ToolRegistrationSeed> AgentRuntimeTools { get; } =
    [
        Low("read_uploaded_file", "读取上传文件", "Read current task uploads and create a safe summary.", "Workspace", ToolDataBoundary.ArtifactDraftOnly),
        Low("parse_csv_json", "解析 CSV/JSON", "Parse CSV or JSON input into structured table data.", "Workspace", ToolDataBoundary.ArtifactDraftOnly),
        Low("parse_table_file", "解析表格文件", "Parse CSV, JSON, or XLSX input into structured table data.", "Workspace", ToolDataBoundary.ArtifactDraftOnly),
        Low("rag_search", "检索知识库", "Search authorized RAG context for the current task.", "RAG", ToolDataBoundary.RagContextOnly),
        CloudReadonly("query_cloud_data_readonly", "查询 Cloud 只读数据", "Cloud AiRead readonly query boundary. Real mode stays disabled by default."),
        BusinessReadonly("query_business_database_readonly", "查询只读业务库", "Query authorized SimulationBusiness data through Text-to-SQL readonly guardrails."),
        Low("summarize_business_query_result", "总结查询结果", "Summarize approved BusinessDatabase readonly query results with source markers.", "DataAnalysis", ToolDataBoundary.SimulationBusinessOnly),
        Low("join_evidence", "合并证据", "Deterministically join authorized parent Evidence under the sealed DAG policy.", "Workflow", ToolDataBoundary.AuthorizedEvidenceOnly),
        Low("assess_cloud_health", "评估当前运行健康", "Derive a replayable current device runtime health assessment from authorized Cloud status Evidence.", "DataAnalysis", ToolDataBoundary.AuthorizedEvidenceOnly),
        Low("agent_reasoning", "受控证据综合", "Run one evidence-only child reasoning turn plus at most one recovery turn; no model tools are exposed.", "Workflow", ToolDataBoundary.AuthorizedEvidenceOnly),
        Low("generate_business_chart", "生成业务图表", "Generate controlled chart data from approved BusinessDatabase readonly query results.", "Artifacts", ToolDataBoundary.SimulationBusinessOnly, ToolProviderType.Artifact),
        Low("generate_chart_data", "生成图表数据", "Generate chart preview data from controlled task inputs.", "Artifacts", ToolDataBoundary.ArtifactDraftOnly, ToolProviderType.Artifact),
        Low("generate_markdown_report", "生成 Markdown 报告", "Generate a Markdown draft inside the controlled artifact workspace.", "Artifacts", ToolDataBoundary.ArtifactDraftOnly, ToolProviderType.Artifact),
        Low("generate_html_report", "生成 HTML 报告", "Generate an HTML draft inside the controlled artifact workspace.", "Artifacts", ToolDataBoundary.ArtifactDraftOnly, ToolProviderType.Artifact),
        Approval("generate_pdf", "生成 PDF 草稿", "Generate a PDF draft inside the controlled artifact workspace."),
        Approval("generate_pptx", "生成 PPTX 草稿", "Generate a PPTX draft inside the controlled artifact workspace."),
        Approval("generate_xlsx", "生成 XLSX 草稿", "Generate an XLSX draft inside the controlled artifact workspace."),
        FinalizationCheckpoint(),
        MockMcp("mock_mcp_health_check", "Mock MCP 健康检查", "Validate the in-process Mock MCP provider is available.", ToolDataBoundary.NoData, AiToolRiskLevel.Low, requiresApproval: false, inputSchema: MockHealthInputSchema),
        MockMcp("mock_mcp_kpi_formula_lookup", "Mock MCP KPI 公式查询", "Return deterministic KPI formula notes for capacity, quality, and inventory analysis.", ToolDataBoundary.RagContextOnly, AiToolRiskLevel.Low, requiresApproval: false, inputSchema: MockKpiInputSchema),
        MockMcp("mock_mcp_artifact_quality_check", "Mock MCP 产物质量检查", "Check that draft artifacts keep SimulationBusiness source markers.", ToolDataBoundary.ArtifactDraftOnly, AiToolRiskLevel.Medium, requiresApproval: false, inputSchema: MockArtifactInputSchema),
        MockMcp("mock_mcp_external_ticket_preview", "Mock MCP 外部工单预览", "Create an external ticket preview without sending it to any real external system.", ToolDataBoundary.ArtifactDraftOnly, AiToolRiskLevel.High, requiresApproval: true, inputSchema: MockTicketInputSchema)
    ];

    public static IReadOnlyCollection<string> ObsoleteAgentRuntimeToolCodes { get; } =
    [
        "query_cloud_sandbox_readonly",
        "query_cloud_pilot_readiness_readonly",
        "query_cloud_production_pilot_readonly",
        "query_cloud_production_controlled_readonly"
    ];

    public static ToolRegistrationSeed? FindAgentRuntimeTool(string? toolCode)
    {
        return string.IsNullOrWhiteSpace(toolCode)
            ? null
            : AgentRuntimeTools.FirstOrDefault(tool =>
                string.Equals(tool.ToolCode, toolCode, StringComparison.Ordinal));
    }

    public static bool IsLifecycleCheckpoint(string? toolCode) =>
        string.Equals(toolCode, FinalizationCheckpointToolCode, StringComparison.OrdinalIgnoreCase);

    private static ToolRegistrationSeed Low(
        string code,
        string displayName,
        string description,
        string category,
        ToolDataBoundary dataBoundary,
        ToolProviderType providerType = ToolProviderType.BuiltIn)
    {
        return new ToolRegistrationSeed(
            code,
            displayName,
            description,
            providerType,
            ToolRegistrationTargetType.AgentRuntime,
            AgentRuntimeTarget,
            EmptyObjectInputSchema,
            ResolveOutputSchema(code),
            AiToolRiskLevel.Low,
            null,
            RequiresApproval: false,
            IsEnabled: true,
            TimeoutSeconds: 120,
            ToolAuditLevel.Standard,
            category,
            BusinessDomains: [],
            dataBoundary,
            IsVisibleToPlanner: true,
            IsExecutableByAgent: true,
            SchemaVersion: CurrentSchemaVersion,
            CatalogVersion: CurrentCatalogVersion,
            ApprovalPolicy: "None");
    }

    private static ToolRegistrationSeed Approval(
        string code,
        string displayName,
        string description,
        string approvalPolicy = "ToolApproval")
    {
        return new ToolRegistrationSeed(
            code,
            displayName,
            description,
            ToolProviderType.Artifact,
            ToolRegistrationTargetType.AgentRuntime,
            AgentRuntimeTarget,
            EmptyObjectInputSchema,
            ResolveOutputSchema(code),
            AiToolRiskLevel.High,
            null,
            RequiresApproval: true,
            IsEnabled: true,
            TimeoutSeconds: 180,
            ToolAuditLevel.Standard,
            "Artifacts",
            BusinessDomains: [],
            ToolDataBoundary.ArtifactDraftOnly,
            IsVisibleToPlanner: true,
            IsExecutableByAgent: true,
            SchemaVersion: CurrentSchemaVersion,
            CatalogVersion: CurrentCatalogVersion,
            approvalPolicy);
    }

    private static ToolRegistrationSeed FinalizationCheckpoint()
    {
        return new ToolRegistrationSeed(
            FinalizationCheckpointToolCode,
            "最终产物确认",
            "Plan/lifecycle checkpoint: requests explicit final-output approval; the approved durable NodeRun atomically commits final files, Evidence, Usage, and terminal task state without provider dispatch.",
            ToolProviderType.Artifact,
            ToolRegistrationTargetType.AgentRuntime,
            ArtifactWorkspaceLifecycleTarget,
            EmptyObjectInputSchema,
            ResolveOutputSchema(FinalizationCheckpointToolCode),
            AiToolRiskLevel.High,
            null,
            RequiresApproval: true,
            IsEnabled: true,
            TimeoutSeconds: 180,
            ToolAuditLevel.Standard,
            "Artifacts",
            BusinessDomains: [],
            ToolDataBoundary.ArtifactDraftOnly,
            IsVisibleToPlanner: true,
            // This legacy flag means the checkpoint may participate in an Agent
            // plan. It does not authorize provider/dispatcher invocation.
            IsExecutableByAgent: true,
            SchemaVersion: CurrentSchemaVersion,
            CatalogVersion: CurrentCatalogVersion,
            ApprovalPolicy: "FinalOutputApproval");
    }

    private static ToolRegistrationSeed CloudReadonly(string code, string displayName, string description)
    {
        return new ToolRegistrationSeed(
            code,
            displayName,
            description,
            ToolProviderType.CloudReadonly,
            ToolRegistrationTargetType.AgentRuntime,
            AgentRuntimeTarget,
            EmptyObjectInputSchema,
            ResolveOutputSchema(code),
            AiToolRiskLevel.High,
            null,
            RequiresApproval: true,
            IsEnabled: false,
            TimeoutSeconds: 60,
            ToolAuditLevel.Standard,
            "CloudReadonly",
            BusinessDomains: [],
            ToolDataBoundary.NoData,
            IsVisibleToPlanner: false,
            IsExecutableByAgent: false,
            SchemaVersion: CurrentSchemaVersion,
            CatalogVersion: CurrentCatalogVersion,
            ApprovalPolicy: "DisabledRealCloudReadonly");
    }

    private static ToolRegistrationSeed BusinessReadonly(string code, string displayName, string description)
    {
        return new ToolRegistrationSeed(
            code,
            displayName,
            description,
            ToolProviderType.BuiltIn,
            ToolRegistrationTargetType.AgentRuntime,
            AgentRuntimeTarget,
            EmptyObjectInputSchema,
            ResolveOutputSchema(code),
            AiToolRiskLevel.High,
            "DataSource.TextToSql",
            RequiresApproval: true,
            IsEnabled: true,
            TimeoutSeconds: 60,
            ToolAuditLevel.Standard,
            "DataAnalysis",
            BusinessDomains: ["Production", "Quality", "Inventory", "Sales", "Employee"],
            ToolDataBoundary.SimulationBusinessOnly,
            IsVisibleToPlanner: true,
            IsExecutableByAgent: true,
            SchemaVersion: CurrentSchemaVersion,
            CatalogVersion: CurrentCatalogVersion,
            ApprovalPolicy: "ToolApproval");
    }

    private static ToolRegistrationSeed MockMcp(
        string code,
        string displayName,
        string description,
        ToolDataBoundary dataBoundary,
        AiToolRiskLevel riskLevel,
        bool requiresApproval,
        string inputSchema)
    {
        return new ToolRegistrationSeed(
            code,
            displayName,
            description,
            ToolProviderType.MockMcp,
            ToolRegistrationTargetType.AgentRuntime,
            MockMcpTarget,
            inputSchema,
            ResolveOutputSchema(code),
            riskLevel,
            null,
            requiresApproval,
            IsEnabled: false,
            TimeoutSeconds: 30,
            ToolAuditLevel.Standard,
            "MockMcp",
            BusinessDomains: ["Production", "Quality", "Inventory", "Sales", "Employee"],
            dataBoundary,
            IsVisibleToPlanner: false,
            IsExecutableByAgent: false,
            SchemaVersion: CurrentSchemaVersion,
            CatalogVersion: CurrentCatalogVersion,
            ApprovalPolicy: requiresApproval ? "ToolApproval" : "None");
    }

    private static string ResolveOutputSchema(string code)
    {
        return code switch
        {
            "read_uploaded_file" => UploadSummaryOutputSchema,
            "parse_csv_json" or "parse_table_file" => TableSummaryOutputSchema,
            "rag_search" => RagSummaryOutputSchema,
            "query_cloud_data_readonly" => CloudQuerySummaryOutputSchema,
            "query_business_database_readonly" or "summarize_business_query_result" => BusinessQuerySummaryOutputSchema,
            "join_evidence" => EvidenceJoinOutputSchema,
            "assess_cloud_health" => CloudHealthAssessmentOutputSchema,
            "agent_reasoning" => AgentReasoningOutputSchema,
            "generate_business_chart" or "generate_chart_data" or
            "generate_markdown_report" or "generate_html_report" or
            "generate_pdf" or "generate_pptx" or "generate_xlsx" => ArtifactOutputSchema,
            "finalize_artifacts" => FinalizationCheckpointOutputSchema,
            "mock_mcp_health_check" => MockHealthOutputSchema,
            "mock_mcp_kpi_formula_lookup" => MockKpiOutputSchema,
            "mock_mcp_artifact_quality_check" => MockArtifactOutputSchema,
            "mock_mcp_external_ticket_preview" => MockTicketOutputSchema,
            _ => throw new InvalidOperationException($"Built-in tool '{code}' has no frozen output contract.")
        };
    }
}
