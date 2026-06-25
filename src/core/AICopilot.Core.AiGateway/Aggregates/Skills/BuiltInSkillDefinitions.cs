using AICopilot.SharedKernel.Ai;

namespace AICopilot.Core.AiGateway.Aggregates.Skills;

public sealed record SkillDefinitionSeed(
    string SkillCode,
    string DisplayName,
    string Description,
    IReadOnlyCollection<string> AllowedToolCodes,
    AiToolRiskLevel RiskLevel,
    string ApprovalPolicy,
    IReadOnlyCollection<string>? AllowedDataSourceModes = null,
    IReadOnlyCollection<string>? AllowedKnowledgeScopes = null,
    IReadOnlyCollection<string>? OutputComponentTypes = null,
    bool IsEnabled = true,
    int Version = BuiltInSkillDefinitions.CurrentVersion);

public static class BuiltInSkillDefinitions
{
    public const int CurrentVersion = 1;

    public const string DefaultSkillCode = "general_report";

    public static IReadOnlyCollection<SkillDefinitionSeed> All { get; } =
    [
        new(
            DefaultSkillCode,
            "通用报告",
            "面向日常分析、文件输入、知识检索和受控产物输出的默认 Skill。",
            [
                "read_uploaded_file",
                "parse_csv_json",
                "parse_table_file",
                "rag_search",
                "query_business_database_readonly",
                "summarize_business_query_result",
                "generate_business_chart",
                "generate_chart_data",
                "generate_markdown_report",
                "generate_html_report",
                "generate_pdf",
                "generate_pptx",
                "generate_xlsx",
                "finalize_artifacts"
            ],
            AiToolRiskLevel.High,
            "ToolApproval",
            ["SimulationBusiness"],
            ["SelectedKnowledgeBase"],
            ["chart", "markdown", "html", "pdf", "pptx", "xlsx"]),
        new(
            "data_analysis",
            "数据分析",
            "只读业务数据分析、汇总和图表输出；不允许直接写业务系统。",
            [
                "query_business_database_readonly",
                "summarize_business_query_result",
                "generate_business_chart",
                "generate_chart_data",
                "generate_markdown_report",
                "generate_html_report",
                "generate_xlsx",
                "finalize_artifacts"
            ],
            AiToolRiskLevel.High,
            "ToolApproval",
            ["SimulationBusiness"],
            [],
            ["chart", "markdown", "html", "xlsx"]),
        new(
            "knowledge_research",
            "知识检索",
            "围绕授权知识库做检索、解释和摘要，不暴露业务写入工具。",
            [
                "rag_search",
                "generate_markdown_report",
                "generate_html_report",
                "finalize_artifacts"
            ],
            AiToolRiskLevel.Low,
            "FinalOutputApproval",
            [],
            ["SelectedKnowledgeBase"],
            ["markdown", "html"]),
        new(
            "artifact_report",
            "产物报告",
            "基于上传文件和已获准上下文生成受控草稿产物，并在最终输出前审批。",
            [
                "read_uploaded_file",
                "parse_csv_json",
                "parse_table_file",
                "generate_chart_data",
                "generate_markdown_report",
                "generate_html_report",
                "generate_pdf",
                "generate_pptx",
                "generate_xlsx",
                "finalize_artifacts"
            ],
            AiToolRiskLevel.High,
            "ToolApproval",
            [],
            [],
            ["chart", "markdown", "html", "pdf", "pptx", "xlsx"]),
        new(
            "cloud_readonly",
            "Cloud 只读分析",
            "仅通过 AiRead 只读边界读取 Cloud 业务数据，并生成受控分析产物。",
            [
                "query_cloud_data_readonly",
                "generate_chart_data",
                "generate_markdown_report",
                "generate_html_report",
                "finalize_artifacts"
            ],
            AiToolRiskLevel.High,
            "ToolApproval",
            ["CloudReadOnly"],
            [],
            ["chart", "markdown", "html"])
    ];
}
