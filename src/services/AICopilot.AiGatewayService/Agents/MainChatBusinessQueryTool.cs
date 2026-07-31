using System.ComponentModel;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Agents;

internal sealed class MainChatBusinessQueryTool(
    SemanticAnalysisRunner semanticAnalysisRunner,
    SessionRuntimeSnapshot session,
    BusinessQueryContext? confirmedQuery,
    TrustedRenderChunkBuffer renderChunkBuffer)
{
    public static AiToolDefinition CreateDefinition(
        MainChatBusinessQueryTool target)
    {
        var method = typeof(MainChatBusinessQueryTool).GetMethod(
                         nameof(BusinessQuery))
                     ?? throw new InvalidOperationException(
                         "BusinessQuery tool method is missing.");
        var definition = AiToolDefinition.FromMethod(method, target);
        return new AiToolDefinition
        {
            Name = "BusinessQuery",
            ToolName = "BusinessQuery",
            Description = definition.Description,
            Method = definition.Method,
            Target = definition.Target,
            ExternalSystemType = AiToolExternalSystemType.CloudReadOnly,
            CapabilityKind = AiToolCapabilityKind.ReadOnlyQuery,
            RiskLevel = AiToolRiskLevel.Low,
            ReadOnlyDeclared = true,
            RequiredPermission = "AiGateway.Chat",
            AuditLevel = "Standard",
            DataBoundary = "CloudReadOnly",
            SchemaVersion = 1
        };
    }

    [Description(
        "Query authorized manufacturing business data. The server always tries the typed BusinessQuery provider first and permits only same-source governed Text-to-SQL fallback for Unsupported or Unavailable outcomes. Text-to-SQL is not a model-visible tool.")]
    public async Task<object> BusinessQuery(
        string semanticIntent,
        string question,
        CancellationToken cancellationToken)
    {
        var intent = new IntentResult
        {
            Intent = confirmedQuery?.SemanticPlan?.Intent ?? semanticIntent,
            Query = confirmedQuery?.Question ?? question,
            Confidence = 1,
            BusinessDataSourceExplicitlySelected = confirmedQuery?.SourceExplicitlySelected == true,
            ConfirmedBusinessQueryContext =
                confirmedQuery is null ? null : BusinessQueryConfirmation.Complete,
            ConfirmedBusinessQuery = confirmedQuery
        };
        var sink = new AgentWorkflowSink();
        AgentAnalysisNodeResult result;
        try
        {
            result = await semanticAnalysisRunner.RunAsync(
                intent,
                sink,
                session,
                cancellationToken);
        }
        catch
        {
            sink.Complete();
            throw;
        }

        sink.Complete();
        if (result.Status == BranchExecutionStatus.Succeeded)
        {
            await renderChunkBuffer.CaptureWidgetsAsync(sink, cancellationToken);
        }

        return result.Status switch
        {
            BranchExecutionStatus.Succeeded or BranchExecutionStatus.Empty => new
            {
                status = result.Status.ToString().ToLowerInvariant(),
                context = result.Evidence?.SafeContext,
                provider = result.Evidence?.Provider,
                sourceMode = result.Evidence?.SourceMode,
                isSimulation = result.Evidence?.IsSimulation
            },
            _ => new
            {
                status = "failed",
                code = result.FailureCode,
                message = result.SafeMessage
            }
        };
    }
}
