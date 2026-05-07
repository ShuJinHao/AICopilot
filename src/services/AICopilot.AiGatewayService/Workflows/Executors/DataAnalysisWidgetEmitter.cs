using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.Visualization;
using AICopilot.Visualization.Widgets;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed class DataAnalysisWidgetEmitter(ILogger<DataAnalysisWidgetEmitter> logger)
{
    public async Task TryEmitAsync(
        DataAnalysisOutputDto output,
        IEnumerable<dynamic>? rawData,
        IEnumerable<SchemaColumn>? schema,
        ChatWorkflowSink? sink,
        string databaseName,
        CancellationToken cancellationToken)
    {
        if (output.Decision is null || rawData is null || schema is null)
        {
            return;
        }

        try
        {
            var widget = BuildWidget(output.Decision, rawData, schema);
            if (sink is not null)
            {
                await sink.WriteAsync(new ChatChunk(DataAnalysisExecutor.ExecutorId, ChunkType.Widget, widget.ToJson()), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "构建可视化 Widget 失败。Database: {DbName}", databaseName);
        }
    }

    private static IWidget BuildWidget(
        VisualDecisionDto decision,
        IEnumerable<dynamic> data,
        IEnumerable<SchemaColumn> schema)
    {
        switch (decision.Type)
        {
            case WidgetType.StatsCard:
                var firstRow = data.First() as IDictionary<string, object>;
                if (firstRow == null || firstRow.Count == 0)
                {
                    throw new InvalidOperationException("StatsCard widget requires at least one row of data.");
                }

                var value = firstRow.Values.First();
                return new StatsCardWidget
                {
                    Title = decision.Title,
                    Description = decision.Description,
                    Data = new StatsCardData
                    {
                        Label = decision.Title,
                        Value = value,
                        Unit = decision.Unit
                    }
                };

            case WidgetType.DataTable:
                return new DataTableWidget
                {
                    Title = decision.Title,
                    Data = data.ToDataTableData(schema)
                };

            case WidgetType.Chart:
                var dataset = data.ToChartDataset(schema);
                return new ChartWidget
                {
                    Title = decision.Title,
                    Data = new ChartData
                    {
                        Category = decision.ChartConfig!.Category,
                        Dataset = dataset,
                        Encoding = new ChartEncoding
                        {
                            X = decision.ChartConfig.X,
                            Y = string.IsNullOrWhiteSpace(decision.ChartConfig.Y)
                                ? []
                                : [decision.ChartConfig.Y],
                            SeriesName = decision.ChartConfig.Series
                        }
                    }
                };

            default:
                throw new NotSupportedException($"不支持的 Widget 类型: {decision.Type}");
        }
    }
}
