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
    ToolAuditLevel AuditLevel);

public static class BuiltInToolRegistrations
{
    private const string AgentRuntimeTarget = "AgentTaskRuntime";
    private const string ObjectSchema = """{"type":"object"}""";

    public static IReadOnlyCollection<ToolRegistrationSeed> AgentRuntimeTools { get; } =
    [
        Low("read_uploaded_file", "读取上传文件", "读取当前任务绑定的上传文件并生成安全摘要。"),
        Low("parse_csv_json", "解析 CSV/JSON", "将 CSV 或 JSON 输入解析为结构化数据。"),
        Low("parse_table_file", "解析表格文件", "将 CSV、JSON 或 XLSX 输入解析为结构化数据。"),
        Low("rag_search", "检索知识库", "基于任务目标检索已授权知识库。"),
        CloudReadonly("query_cloud_data_readonly", "读取 Cloud 只读数据", "Cloud AiRead readonly query boundary. Data source mode is controlled by CloudReadonly configuration."),
        Low("generate_chart_data", "生成图表数据", "基于可用输入生成图表预览数据。", ToolProviderType.Artifact),
        Low("generate_markdown_report", "生成 Markdown 报告", "在受控工作区 draft 目录生成 Markdown 草稿。", ToolProviderType.Artifact),
        Low("generate_html_report", "生成 HTML 报告", "在受控工作区 draft 目录生成 HTML 草稿。", ToolProviderType.Artifact),
        Approval("generate_pdf", "生成 PDF 草稿", "在受控工作区 draft 目录生成 PDF 草稿。"),
        Approval("generate_pptx", "生成 PPTX 草稿", "在受控工作区 draft 目录生成 PPTX 草稿。"),
        Approval("generate_xlsx", "生成 XLSX 草稿", "在受控工作区 draft 目录生成 XLSX 草稿。"),
        Approval("finalize_artifacts", "确认正式输出", "最终输出前创建 FinalOutput 审批并等待用户确认。")
    ];

    private static ToolRegistrationSeed Low(
        string code,
        string displayName,
        string description,
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
            ToolAuditLevel.Standard);
    }

    private static ToolRegistrationSeed Approval(string code, string displayName, string description)
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
            AiToolRiskLevel.RequiresApproval,
            null,
            RequiresApproval: true,
            IsEnabled: true,
            TimeoutSeconds: 180,
            ToolAuditLevel.Standard);
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
            AiToolRiskLevel.RequiresApproval,
            null,
            RequiresApproval: true,
            IsEnabled: false,
            TimeoutSeconds: 60,
            ToolAuditLevel.Standard);
    }
}
