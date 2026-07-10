using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.BusinessSemantics;

internal sealed class ProcessSummaryProfile : SemanticSummaryProfileBase
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["processId"] = "工序标识",
        ["processCode"] = "工序编码",
        ["processName"] = "工序名称"
    };

    public override SemanticQueryTarget Target => SemanticQueryTarget.Process;

    public override IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "列出工序主数据",
        "查看 CUT 工序详情"
    ];

    protected override IReadOnlyDictionary<string, string> FieldLabels => Labels;

    public override SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        var metrics = new[] { Metric("totalCount", "工序总数", $"{rows.Count} 个") };
        var highlights = rows.Take(3)
            .Select(row => $"工序 {SemanticSummaryFormatting.GetString(row, "processCode")}，名称 {SemanticSummaryFormatting.GetString(row, "processName")}，标识 {SemanticSummaryFormatting.GetString(row, "processId")}")
            .ToArray();
        return new SemanticSummaryDto(
            plan.Target.ToString(),
            rows.Count == 0 ? "当前范围内未命中 Cloud 工序主数据。" : $"当前命中 {rows.Count} 条 Cloud 工序主数据。",
            metrics,
            highlights,
            scope);
    }
}
