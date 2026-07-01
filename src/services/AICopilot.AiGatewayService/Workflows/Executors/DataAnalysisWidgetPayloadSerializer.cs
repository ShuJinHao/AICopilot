using System.Text.Json;
using AICopilot.Visualization.Widgets;

namespace AICopilot.AiGatewayService.Workflows.Executors;

internal static class DataAnalysisWidgetPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(IWidget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        return JsonSerializer.Serialize(ToPayload(widget), JsonOptions);
    }

    private static object ToPayload(IWidget widget)
    {
        return widget switch
        {
            StatsCardWidget statsCard => new
            {
                id = BuildId("stats-card", statsCard.Title),
                type = "StatsCard",
                title = statsCard.Title,
                description = statsCard.Description,
                data = statsCard.Data
            },
            DataTableWidget dataTable => new
            {
                id = BuildId("data-table", dataTable.Title),
                type = "DataTable",
                title = dataTable.Title,
                description = dataTable.Description,
                data = dataTable.Data
            },
            ChartWidget chart => new
            {
                id = BuildId("chart", chart.Title),
                type = "Chart",
                title = chart.Title,
                description = chart.Description,
                data = chart.Data
            },
            _ => new
            {
                id = BuildId("widget", widget.Title),
                type = widget.GetType().Name,
                title = widget.Title,
                description = widget.Description,
                data = (object?)null
            }
        };
    }

    private static string BuildId(string prefix, string title)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? "untitled"
            : new string(title
                .Trim()
                .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
                .ToArray());

        return $"{prefix}-{normalizedTitle}".TrimEnd('-');
    }
}
