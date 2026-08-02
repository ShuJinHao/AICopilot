using System.ComponentModel;
using AICopilot.AiGatewayService.BusinessQueries;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Agents;

internal sealed class MainChatBusinessQueryTool(
    BusinessQueryExecutor businessQueryExecutor,
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
        var result = await businessQueryExecutor.ExecuteAsync(
            session.Id,
            confirmedQuery?.SemanticPlan?.Intent ?? semanticIntent,
            confirmedQuery?.Question ?? question,
            confirmedQuery,
            cancellationToken);
        renderChunkBuffer.CaptureTrustedWidgets(result.Widgets);

        return result.Status switch
        {
            BusinessQueryExecutionStatus.Succeeded or BusinessQueryExecutionStatus.Empty => new
            {
                status = result.Status.ToString().ToLowerInvariant(),
                context = result.SafeContext,
                provider = result.Provider,
                sourceKey = result.SourceKey,
                sourceMode = result.SourceMode,
                isSimulation = result.IsSimulation
            },
            BusinessQueryExecutionStatus.NeedsConfirmation => new
            {
                status = "needs_confirmation",
                code = result.FailureCode,
                message = result.SafeMessage
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
