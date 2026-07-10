using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.BusinessSemantics;

internal sealed class ClientReleaseSummaryProfile : SemanticSummaryProfileBase
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["releaseId"] = "发布版本标识",
        ["componentKind"] = "组件类型",
        ["componentKey"] = "组件编码",
        ["displayName"] = "显示名称",
        ["channel"] = "发布通道",
        ["targetRuntime"] = "目标运行时",
        ["version"] = "版本号",
        ["status"] = "发布状态",
        ["releaseNotes"] = "Cloud 原始发布说明",
        ["createdAtUtc"] = "创建时间",
        ["publishedAtUtc"] = "发布时间",
        ["deletedAtUtc"] = "归档时间"
    };

    public override SemanticQueryTarget Target => SemanticQueryTarget.ClientRelease;

    public override IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "列出 stable 通道、win-x64 运行时的已发布客户端版本"
    ];

    protected override IReadOnlyDictionary<string, string> FieldLabels => Labels;

    public override SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        var statusBreakdown = SemanticSummaryFormatting.BuildBreakdown(rows, "status", "条");
        var metrics = new List<SemanticMetricItemDto>
        {
            Metric("totalCount", "发布版本记录数", $"{rows.Count} 条")
        };
        if (!string.IsNullOrWhiteSpace(statusBreakdown))
        {
            metrics.Add(Metric("statusBreakdown", "发布状态分布", statusBreakdown));
        }

        var highlights = rows.Take(3)
            .Select(row => $"组件 {SemanticSummaryFormatting.GetString(row, "componentKey")}，版本 {SemanticSummaryFormatting.GetString(row, "version")}，通道 {SemanticSummaryFormatting.GetString(row, "channel")}，运行时 {SemanticSummaryFormatting.GetString(row, "targetRuntime")}，状态 {SemanticSummaryFormatting.GetString(row, "status")}")
            .ToArray();
        var conclusion = rows.Count == 0
            ? "当前正式 Cloud 条件下未命中客户端发布版本。"
            : $"当前命中 {rows.Count} 条正式 Cloud 客户端发布版本；版本与发布说明如有值均为 Cloud 原样字段，未生成哈希或下载地址。";
        return new SemanticSummaryDto(plan.Target.ToString(), conclusion, metrics, highlights, scope);
    }
}
