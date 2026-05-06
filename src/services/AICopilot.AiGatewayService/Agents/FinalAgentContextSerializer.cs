using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.Agents;

public interface IFinalAgentContextSerializer
{
    Task<StoredFinalAgentContext> CreateSnapshotAsync(
        FinalAgentContext agentContext,
        CancellationToken cancellationToken = default);

    Task<FinalAgentContext> RestoreAsync(
        StoredFinalAgentContext storedContext,
        CancellationToken cancellationToken = default);
}

public sealed class FinalAgentContextSerializer(
    ChatAgentFactory chatAgentFactory,
    ApprovalToolResolver approvalToolResolver,
    IReadRepository<Session> sessionRepository) : IFinalAgentContextSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public async Task<StoredFinalAgentContext> CreateSnapshotAsync(
        FinalAgentContext agentContext,
        CancellationToken cancellationToken = default)
    {
        var serializedThread = await agentContext.Agent.SerializeSessionAsync(
            agentContext.Thread,
            SerializerOptions,
            cancellationToken);

        var toolNames = agentContext.RunOptions.Options.Tools
            .Select(tool => tool.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var toolIdentities = agentContext.RunOptions.Options.Tools
            .Where(tool => tool.Identity is not null)
            .ToDictionary(tool => tool.Name, tool => tool.Identity!, StringComparer.OrdinalIgnoreCase);

        var pendingApprovals = agentContext.FunctionApprovalRequestContents
            .Select(request => ToStoredApprovalRequest(request, toolIdentities))
            .ToArray();

        return new StoredFinalAgentContext(
            agentContext.SessionId,
            agentContext.InputText,
            agentContext.EstimatedInputTokens,
            agentContext.SystemPromptTokenCount,
            agentContext.TokenTelemetryContext,
            agentContext.RunOptions.Options.MaxOutputTokens,
            agentContext.RunOptions.Options.Temperature,
            toolNames,
            serializedThread,
            pendingApprovals);
    }

    public async Task<FinalAgentContext> RestoreAsync(
        StoredFinalAgentContext storedContext,
        CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.FirstOrDefaultAsync(
            new SessionByIdSpec(new SessionId(storedContext.SessionId)),
            cancellationToken);

        if (session == null)
        {
            throw new InvalidOperationException("Unable to restore final agent context because the session no longer exists.");
        }

        var tools = await approvalToolResolver.GetToolsByNamesAsync(storedContext.ToolNames, cancellationToken);
        ScopedRuntimeAgent? scopedAgent = await chatAgentFactory.CreateAgentAsync(
            session.TemplateId,
            options =>
            {
                options.Tools = tools;
            });

        try
        {
            var thread = await scopedAgent.Agent.DeserializeSessionAsync(
                storedContext.SerializedThreadState,
                SerializerOptions,
                cancellationToken);

            var runOptions = new RuntimeAgentRunOptions(
                new AiChatOptions
                {
                    Tools = tools,
                    MaxOutputTokens = storedContext.MaxOutputTokens,
                    Temperature = storedContext.Temperature
                });

            var context = new FinalAgentContext
            {
                ScopedAgent = scopedAgent,
                Thread = thread,
                InputText = storedContext.InputText,
                RunOptions = runOptions,
                SessionId = storedContext.SessionId,
                EstimatedInputTokens = storedContext.EstimatedInputTokens,
                SystemPromptTokenCount = storedContext.SystemPromptTokenCount,
                TokenTelemetryContext = storedContext.TokenTelemetryContext
            };

            foreach (var approval in storedContext.PendingApprovals)
            {
                context.FunctionApprovalRequestContents.Add(ToRuntimeApprovalRequest(approval));
            }

            scopedAgent = null;
            return context;
        }
        finally
        {
            if (scopedAgent is not null)
            {
                await scopedAgent.DisposeAsync();
            }
        }
    }

    private static StoredToolApprovalRequest ToStoredApprovalRequest(
        AiToolApprovalRequest request,
        IReadOnlyDictionary<string, AiToolIdentity> toolIdentities)
    {
        var identity = request.ToolCall.Identity;
        if (identity is null && toolIdentities.TryGetValue(request.ToolCall.Name, out var mappedIdentity))
        {
            identity = mappedIdentity;
        }
        else if (identity is null)
        {
            identity = FindUniqueIdentityByRawToolName(
                toolIdentities.Values,
                request.ToolCall.ToolName ?? request.ToolCall.Name);
        }

        return new StoredToolApprovalRequest(
            request.RequestId,
            request.ToolCall.CallId,
            request.ToolCall.Kind.ToString(),
            request.ToolCall.ToolName ?? identity?.ToolName ?? request.ToolCall.Name,
            request.ToolCall.ServerName,
            request.ToolCall.Arguments.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase),
            identity?.TargetType.ToString(),
            identity?.TargetName,
            request.ToolCall.Name);
    }

    private static AiToolIdentity? FindUniqueIdentityByRawToolName(
        IEnumerable<AiToolIdentity> identities,
        string rawToolName)
    {
        var matches = identities
            .Where(identity => string.Equals(identity.ToolName, rawToolName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static AiToolApprovalRequest ToRuntimeApprovalRequest(StoredToolApprovalRequest storedApproval)
    {
        var kind = Enum.TryParse<AiToolCallKind>(storedApproval.ToolKind, ignoreCase: true, out var parsedKind)
            ? parsedKind
            : AiToolCallKind.Function;
        var targetType = Enum.TryParse<AiToolTargetType>(storedApproval.TargetType, ignoreCase: true, out var parsedTargetType)
            ? parsedTargetType
            : (AiToolTargetType?)null;

        return new AiToolApprovalRequest(
            storedApproval.RequestId,
            new AiToolCall(
                storedApproval.CallId,
                storedApproval.RuntimeName ?? storedApproval.ToolName ?? storedApproval.CallId,
                kind,
                storedApproval.ServerName,
                storedApproval.Arguments,
                targetType,
                storedApproval.TargetName,
                storedApproval.ToolName));
    }
}
