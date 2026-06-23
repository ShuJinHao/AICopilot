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
    public const int CurrentCatalogVersion = 10;

    private const string AgentRuntimeTarget = "AgentTaskRuntime";
    private const string MockMcpTarget = "MockMcpProvider";
    private const string ObjectSchema = """{"type":"object"}""";

    public static IReadOnlyCollection<ToolRegistrationSeed> AgentRuntimeTools { get; } =
    [
        Low("read_uploaded_file", "Read uploaded file", "Read current task uploads and create a safe summary.", "Workspace", ToolDataBoundary.ArtifactDraftOnly),
        Low("parse_csv_json", "Parse CSV/JSON", "Parse CSV or JSON input into structured table data.", "Workspace", ToolDataBoundary.ArtifactDraftOnly),
        Low("parse_table_file", "Parse table file", "Parse CSV, JSON, or XLSX input into structured table data.", "Workspace", ToolDataBoundary.ArtifactDraftOnly),
        Low("rag_search", "Search knowledge base", "Search authorized RAG context for the current task.", "RAG", ToolDataBoundary.RagContextOnly),
        CloudReadonly("query_cloud_data_readonly", "Query Cloud readonly data", "Cloud AiRead readonly query boundary. Real mode stays disabled by default."),
        BusinessReadonly("query_business_database_readonly", "Query business database readonly", "Query authorized SimulationBusiness data through Text-to-SQL readonly guardrails."),
        Low("summarize_business_query_result", "Summarize business query result", "Summarize approved BusinessDatabase readonly query results with source markers.", "DataAnalysis", ToolDataBoundary.SimulationBusinessOnly),
        Low("generate_business_chart", "Generate business chart", "Generate controlled chart data from approved BusinessDatabase readonly query results.", "Artifacts", ToolDataBoundary.SimulationBusinessOnly, ToolProviderType.Artifact),
        Low("generate_chart_data", "Generate chart data", "Generate chart preview data from controlled task inputs.", "Artifacts", ToolDataBoundary.ArtifactDraftOnly, ToolProviderType.Artifact),
        Low("generate_markdown_report", "Generate Markdown report", "Generate a Markdown draft inside the controlled artifact workspace.", "Artifacts", ToolDataBoundary.ArtifactDraftOnly, ToolProviderType.Artifact),
        Low("generate_html_report", "Generate HTML report", "Generate an HTML draft inside the controlled artifact workspace.", "Artifacts", ToolDataBoundary.ArtifactDraftOnly, ToolProviderType.Artifact),
        Approval("generate_pdf", "Generate PDF draft", "Generate a PDF draft inside the controlled artifact workspace."),
        Approval("generate_pptx", "Generate PPTX draft", "Generate a PPTX draft inside the controlled artifact workspace."),
        Approval("generate_xlsx", "Generate XLSX draft", "Generate an XLSX draft inside the controlled artifact workspace."),
        Approval("finalize_artifacts", "Finalize artifacts", "Create the final-output approval checkpoint before publication.", approvalPolicy: "FinalOutputApproval"),
        MockMcp("mock_mcp_health_check", "Mock MCP health check", "Validate the in-process Mock MCP provider is available.", ToolDataBoundary.NoData, AiToolRiskLevel.Low, requiresApproval: false, inputSchema: ObjectSchema),
        MockMcp("mock_mcp_kpi_formula_lookup", "Mock MCP KPI formula lookup", "Return deterministic KPI formula notes for capacity, quality, and inventory analysis.", ToolDataBoundary.RagContextOnly, AiToolRiskLevel.Low, requiresApproval: false, inputSchema: """{"type":"object","properties":{"domain":{"type":"string","enum":["Production","Quality","Inventory","Sales","Employee"]}}}"""),
        MockMcp("mock_mcp_artifact_quality_check", "Mock MCP artifact quality check", "Check that draft artifacts keep SimulationBusiness source markers.", ToolDataBoundary.ArtifactDraftOnly, AiToolRiskLevel.Medium, requiresApproval: false, inputSchema: """{"type":"object","properties":{"artifactType":{"type":"string"},"contentPreview":{"type":"string"}}}"""),
        MockMcp("mock_mcp_external_ticket_preview", "Mock MCP external ticket preview", "Create an external ticket preview without sending it to any real external system.", ToolDataBoundary.ArtifactDraftOnly, AiToolRiskLevel.High, requiresApproval: true, inputSchema: """{"type":"object","properties":{"title":{"type":"string"},"summary":{"type":"string"}},"required":["title"]}""")
    ];

    public static IReadOnlyCollection<string> ObsoleteAgentRuntimeToolCodes { get; } =
    [
        "query_cloud_sandbox_readonly",
        "query_cloud_pilot_readiness_readonly",
        "query_cloud_production_pilot_readonly",
        "query_cloud_production_controlled_readonly"
    ];

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
            ObjectSchema,
            ObjectSchema,
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
            SchemaVersion: 1,
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
            ObjectSchema,
            ObjectSchema,
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
            SchemaVersion: 1,
            CatalogVersion: CurrentCatalogVersion,
            approvalPolicy);
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
            ObjectSchema,
            ObjectSchema,
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
            SchemaVersion: 1,
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
            ObjectSchema,
            ObjectSchema,
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
            SchemaVersion: 1,
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
            ObjectSchema,
            riskLevel,
            null,
            requiresApproval,
            IsEnabled: true,
            TimeoutSeconds: 30,
            ToolAuditLevel.Standard,
            "MockMcp",
            BusinessDomains: ["Production", "Quality", "Inventory", "Sales", "Employee"],
            dataBoundary,
            IsVisibleToPlanner: true,
            IsExecutableByAgent: true,
            SchemaVersion: 1,
            CatalogVersion: CurrentCatalogVersion,
            ApprovalPolicy: requiresApproval ? "ToolApproval" : "None");
    }
}
