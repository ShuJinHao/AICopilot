using AICopilot.AiGatewayService.Workspaces;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentBuiltInToolDispatcher(
    IReadRepository<UploadRecord> uploadRepository,
    IAgentArtifactWorkspaceService workspaceService,
    IFileStorageService fileStorage,
    IAgentTableFileParser tableFileParser,
    IKnowledgeRetrievalService knowledgeRetrievalService,
    IEnumerable<IKnowledgeBaseAccessChecker> knowledgeBaseAccessCheckers,
    ICloudReadonlyAgentToolExecutor cloudReadonlyToolExecutor,
    IIdentityAccessService identityAccessService,
    AgentRuntimeArtifactBuilder artifactBuilder,
    IBusinessDatabaseReadService? businessDatabaseReadService = null,
    IBusinessTextToSqlRuntime? businessTextToSqlRuntime = null,
    CloudReadOnlyTextToSqlFallbackRunner? cloudTextToSqlFallbackRunner = null,
    AgentReasoningNodeExecutor? reasoningNodeExecutor = null)
{
    private readonly AgentRuntimeFileInputToolService fileInputTools = new(
        uploadRepository,
        workspaceService,
        fileStorage,
        tableFileParser);

    private readonly AgentRuntimeRagToolService ragTools = new(
        knowledgeRetrievalService,
        knowledgeBaseAccessCheckers,
        identityAccessService);

    private readonly AgentRuntimeCloudReadonlyToolService cloudReadonlyTools = new(cloudReadonlyToolExecutor);

    private readonly AgentRuntimeBusinessQueryToolService businessQueryTools = new(
        businessDatabaseReadService,
        businessTextToSqlRuntime,
        cloudTextToSqlFallbackRunner);

    public Task<object> ExecuteAsync(AgentToolExecutionContext context)
    {
        if (string.Equals(context.Step.ToolCode, "agent_reasoning", StringComparison.Ordinal))
        {
            return reasoningNodeExecutor is null
                ? Task.FromException<object>(new InvalidOperationException("Agent reasoning runtime is unavailable."))
                : reasoningNodeExecutor.ExecuteAsync(context);
        }

        return ExecuteBuiltInStepAsync(
            context.Task,
            context.Workspace,
            context.Plan,
            context.Step,
            context.State,
            context.InputEvidence,
            context.CancellationToken);
    }

    private async Task<object> ExecuteBuiltInStepAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentTaskPlanDocument plan,
        AgentStep step,
        AgentTaskRunState state,
        IReadOnlyCollection<AgentEvidenceRecord>? inputEvidence,
        CancellationToken cancellationToken)
    {
        if (step.ToolCode is
            "generate_business_chart" or
            "generate_chart_data" or
            "generate_markdown_report" or
            "generate_html_report" or
            "generate_pdf" or
            "generate_pptx" or
            "generate_xlsx")
        {
            artifactBuilder.BindEvidenceSet(state, inputEvidence ?? []);
        }

        return step.ToolCode switch
        {
            "read_uploaded_file" => await fileInputTools.ReadUploadedFilesAsync(task.UserId, workspace, step, plan, state, writeSourceArtifacts: true, cancellationToken: cancellationToken),
            "parse_csv_json" => await fileInputTools.ParseTableFileAsync(task.UserId, workspace, step, plan, state, cancellationToken),
            "parse_table_file" => await fileInputTools.ParseTableFileAsync(task.UserId, workspace, step, plan, state, cancellationToken),
            "rag_search" => await ragTools.SearchRagAsync(task, plan, state, cancellationToken),
            "query_cloud_data_readonly" => await cloudReadonlyTools.QueryCloudReadonlyAsync(plan, step, state, cancellationToken),
            "query_business_database_readonly" => await businessQueryTools.QueryBusinessDatabaseReadonlyP1Async(task, plan, state, cancellationToken),
            "summarize_business_query_result" => businessQueryTools.SummarizeBusinessQueryResult(state),
            "join_evidence" => AgentRuntimeEvidenceJoinTool.Join(plan, step, inputEvidence ?? []),
            "assess_cloud_health" => AgentCloudHealthAssessmentTool.Assess(plan, state, inputEvidence ?? []),
            "generate_business_chart" => await artifactBuilder.GenerateChartDataAsync(workspace, step, state, cancellationToken),
            "generate_chart_data" => await artifactBuilder.GenerateChartDataAsync(workspace, step, state, cancellationToken),
            "generate_markdown_report" => await artifactBuilder.GenerateMarkdownReportAsync(task, workspace, step, state, cancellationToken),
            "generate_html_report" => await artifactBuilder.GenerateHtmlReportAsync(task, workspace, step, state, cancellationToken),
            "generate_pdf" => await artifactBuilder.GeneratePdfReportAsync(task, workspace, step, state, cancellationToken),
            "generate_pptx" => await artifactBuilder.GeneratePptxReportAsync(task, workspace, step, state, cancellationToken),
            "generate_xlsx" => await artifactBuilder.GenerateXlsxReportAsync(task, workspace, step, state, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported agent tool code: {step.ToolCode}")
        };
    }
}
