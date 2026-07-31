using System.ComponentModel;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Agents;

public sealed class MainChatToolCatalog(
    IAgentPluginCatalog pluginCatalog,
    ApprovalToolResolver approvalToolResolver,
    SemanticAnalysisRunner semanticAnalysisRunner)
{
    public async Task<AiToolDefinition[]> BuildAsync(
        SessionRuntimeSnapshot session,
        string userMessage,
        CancellationToken cancellationToken)
    {
        BusinessQueryContext? confirmedQuery = null;
        if (semanticAnalysisRunner.TryConfirmPending(
                session.Id,
                userMessage,
                out var restoredConfirmation))
        {
            confirmedQuery = restoredConfirmation;
        }

        var pluginNames = pluginCatalog.GetAllPlugin()
            .Where(plugin => plugin.ChatExposureMode.CanExposeInChat())
            .Select(plugin => plugin.Name)
            .ToArray();
        var pluginTools = await approvalToolResolver.GetToolsForPluginsAsync(
            pluginNames,
            cancellationToken);
        var permittedPluginTools = pluginTools
            .Where(IsPermittedMainChatTool)
            .ToList();
        permittedPluginTools.Add(CreateBusinessQueryTool(
            new MainChatBusinessQueryTool(
                semanticAnalysisRunner,
                session,
                confirmedQuery)));
        return permittedPluginTools.ToArray();
    }

    private static bool IsPermittedMainChatTool(AiToolDefinition tool)
    {
        if (tool.RiskLevel == AiToolRiskLevel.Blocked ||
            tool.ExternalSystemType == AiToolExternalSystemType.Unknown ||
            tool.CapabilityKind == AiToolCapabilityKind.SideEffecting ||
            string.Equals(
                tool.RequiredPermission,
                "DataSource.TextToSql",
                StringComparison.Ordinal))
        {
            return false;
        }

        if (tool.TargetType == AiToolTargetType.McpServer)
        {
            return AiToolSafetyPolicy.EvaluateConfiguredMcp(tool).IsAllowed;
        }

        return tool.CapabilityKind is
            AiToolCapabilityKind.ReadOnlyQuery or
            AiToolCapabilityKind.Diagnostics or
            AiToolCapabilityKind.LocalSuggestion;
    }

    private static AiToolDefinition CreateBusinessQueryTool(
        MainChatBusinessQueryTool target)
    {
        var method = typeof(MainChatBusinessQueryTool).GetMethod(
                         nameof(MainChatBusinessQueryTool.BusinessQuery))
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
}

internal sealed class MainChatBusinessQueryTool(
    SemanticAnalysisRunner semanticAnalysisRunner,
    SessionRuntimeSnapshot session,
    BusinessQueryContext? confirmedQuery)
{
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
        var result = await semanticAnalysisRunner.RunAsync(
            intent,
            sink: null,
            session,
            cancellationToken);
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

internal sealed class AgentSessionCheckpointSink(
    IAgentSessionStateStore store,
    Guid sessionId,
    Guid userId,
    string? tenantId,
    Guid turnId) : IAgentSessionCheckpointSink
{
    public async ValueTask PersistAsync(
        string serializedSessionState,
        CancellationToken cancellationToken = default)
    {
        _ = await store.PersistCheckpointAsync(
            sessionId,
            userId,
            tenantId,
            turnId,
            serializedSessionState,
            cancellationToken);
    }
}
