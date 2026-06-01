using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentReportComposer
{
    public static AgentReportDocument BuildReportDocument(AgentTask task, AgentTaskRunState state)
    {
        return AgentReportDocumentBuilder.BuildReportDocument(task, state);
    }

    public static string BuildMarkdownReport(AgentTask task, AgentTaskRunState state)
    {
        return AgentReportMarkdownRenderer.BuildMarkdownReport(BuildReportDocument(task, state));
    }

    public static string BuildHtmlReport(AgentTask task, AgentTaskRunState state)
    {
        return AgentReportHtmlRenderer.BuildHtmlReport(BuildReportDocument(task, state));
    }

    public static object BuildChartPayload(AgentTaskRunState state)
    {
        return AgentReportChartPayloadBuilder.BuildChartPayload(state);
    }
}
