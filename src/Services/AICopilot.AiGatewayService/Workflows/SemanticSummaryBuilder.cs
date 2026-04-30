using AICopilot.AiGatewayService.BusinessSemantics;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.Workflows;

public static class SemanticSummaryBuilder
{
    private static readonly ISemanticSummaryProfileCatalog DefaultProfileCatalog = new SemanticSummaryProfileCatalog();

    public static SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        return Build(plan, rows, DefaultProfileCatalog);
    }

    public static SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        ISemanticSummaryProfileCatalog profileCatalog)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profileCatalog);

        if (!profileCatalog.TryGet(plan.Target, out var profile))
        {
            var fallbackScope = BuildScope(plan, null);
            return rows.Count == 0
                ? new SemanticSummaryDto(plan.Target.ToString(), "当前范围内未命中记录。", [], [], fallbackScope)
                : new SemanticSummaryDto(plan.Target.ToString(), $"当前命中 {rows.Count} 条记录。", [], [], fallbackScope);
        }

        var scope = BuildScope(plan, profile);
        if (rows.Count == 0)
        {
            return new SemanticSummaryDto(
                plan.Target.ToString(),
                "当前范围内未命中记录。",
                [],
                [],
                scope);
        }

        return profile.Build(plan, rows, scope);
    }

    private static string BuildScope(
        SemanticQueryPlan plan,
        ISemanticSummaryProfile? profile)
    {
        var scopeSegments = new List<string>();
        foreach (var filter in plan.Filters)
        {
            scopeSegments.Add($"{GetFieldLabel(profile, filter.Field)}{GetFilterOperatorDescription(filter.Operator)}{filter.Value}");
        }

        if (plan.TimeRange != null)
        {
            var start = plan.TimeRange.Start?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "未限制开始时间";
            var end = plan.TimeRange.End?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "未限制结束时间";
            scopeSegments.Add($"时间范围 {start} 到 {end}");
        }

        if (plan.Sort != null)
        {
            scopeSegments.Add($"排序：按{GetFieldLabel(profile, plan.Sort.Field)}{(plan.Sort.Direction == SemanticSortDirection.Desc ? "倒序" : "正序")}");
        }

        scopeSegments.Add($"结果上限 {plan.Limit} 条");
        return string.Join("，", scopeSegments);
    }

    private static string GetFieldLabel(ISemanticSummaryProfile? profile, string field)
    {
        return profile?.GetFieldLabel(field) ?? field;
    }

    private static string GetFilterOperatorDescription(SemanticFilterOperator filterOperator)
    {
        return filterOperator switch
        {
            SemanticFilterOperator.Equal => "=",
            SemanticFilterOperator.Contains => "包含",
            SemanticFilterOperator.GreaterOrEqual => ">=",
            SemanticFilterOperator.LessOrEqual => "<=",
            SemanticFilterOperator.In => "属于",
            _ => "="
        };
    }
}
