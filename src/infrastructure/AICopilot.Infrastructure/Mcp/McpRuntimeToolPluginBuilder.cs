using AICopilot.AgentPlugin;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace AICopilot.Infrastructure.Mcp;

internal sealed class McpRuntimeToolPluginBuilder(ILogger logger)
{
    public McpRuntimeToolCandidate[] SelectExposedTools(
        McpServerInfo mcpServerInfo,
        IEnumerable<McpClientTool> tools)
    {
        var allowlist = mcpServerInfo.AllowedTools.ToDictionary(
            tool => tool.ToolName,
            StringComparer.OrdinalIgnoreCase);

        return tools
            .GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => HasUniqueCanonicalIdentity(mcpServerInfo.Name, group))
            .Select(group => group.Single())
            .Where(tool => allowlist.ContainsKey(tool.Name))
            .Select(tool => TryCreateCandidate(mcpServerInfo, allowlist[tool.Name], tool))
            .Where(candidate => candidate is not null)
            .Cast<McpRuntimeToolCandidate>()
            .ToArray();
    }

    public GenericBridgePlugin BuildMcpPlugin(
        McpServerInfo mcpServerInfo,
        IReadOnlyCollection<McpRuntimeToolBinding> mcpTools,
        McpRuntimeClientHandle clientHandle)
    {
        var tools = mcpTools
            .Select(binding => ToToolDefinition(
                mcpServerInfo.Name,
                binding,
                clientHandle))
            .ToArray();

        return new GenericBridgePlugin
        {
            Name = mcpServerInfo.Name,
            Description = mcpServerInfo.Description,
            Tools = tools,
            ChatExposureMode = mcpServerInfo.ChatExposureMode
        };
    }

    public McpRuntimeToolBinding[] BindGovernance(
        string serverName,
        IReadOnlyCollection<McpRuntimeToolCandidate> candidates,
        IReadOnlyDictionary<string, McpRuntimeToolGovernance> governanceByCode)
    {
        return candidates
            .Select(candidate =>
            {
                var toolCode = AiToolIdentity.CreateRuntimeName(
                    AiToolTargetType.McpServer,
                    serverName,
                    candidate.Tool.Name);
                var governance = governanceByCode.TryGetValue(toolCode, out var registered)
                    ? registered
                    : CreateDefaultGovernance(toolCode, candidate.RiskLevel);
                return new McpRuntimeToolBinding(candidate, governance);
            })
            .ToArray();
    }

    private AiToolDefinition ToToolDefinition(
        string serverName,
        McpRuntimeToolBinding binding,
        McpRuntimeClientHandle clientHandle)
    {
        var candidate = binding.Candidate;
        var governance = binding.Governance;
        var tool = candidate.Tool;

        return new AiToolDefinition
        {
            Name = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, serverName, tool.Name),
            ToolName = tool.Name,
            Description = tool.Description,
            Kind = AiToolCallKind.Mcp,
            TargetType = AiToolTargetType.McpServer,
            TargetName = serverName,
            ServerName = serverName,
            RequiresApproval = governance.RequiresApproval,
            ExternalSystemType = candidate.ExternalSystemType,
            CapabilityKind = candidate.CapabilityKind,
            RiskLevel = governance.RiskLevel,
            RequiredPermission = governance.RequiredPermission,
            AuditLevel = governance.AuditLevel,
            DataBoundary = governance.DataBoundary,
            SchemaVersion = governance.SchemaVersion,
            TimeoutSeconds = governance.TimeoutSeconds,
            ReadOnlyDeclared = candidate.Exposure.ReadOnlyDeclared,
            McpReadOnlyHint = candidate.McpReadOnlyHint,
            McpDestructiveHint = candidate.McpDestructiveHint,
            McpIdempotentHint = candidate.McpIdempotentHint,
            JsonSchema = candidate.InputSchema.Clone(),
            ReturnJsonSchema = candidate.OutputSchema.Clone(),
            InvokeAsync = async (context, cancellationToken) =>
            {
                var safety = AiToolSafetyPolicy.EvaluateConfiguredMcp(
                    new AiToolConfiguredMcpMetadata(
                        candidate.Exposure.ReadOnlyDeclared,
                        candidate.McpReadOnlyHint,
                        candidate.McpDestructiveHint,
                        candidate.McpIdempotentHint,
                        candidate.CapabilityKind,
                        candidate.ExternalSystemType,
                        governance.RiskLevel),
                    tool.Name,
                    tool.Description,
                    candidate.InputSchema,
                    candidate.OutputSchema);
                if (!safety.IsAllowed)
                {
                    throw new InvalidOperationException(
                        "MCP tool execution was blocked by the shared safety policy.");
                }

                var validatedArguments = McpRuntimeToolContract.ValidateArguments(
                    candidate,
                    context.Arguments);
                if (!validatedArguments.IsValid || validatedArguments.Value is not { } argumentsElement)
                {
                    throw new InvalidOperationException(
                        validatedArguments.Error ?? "MCP tool arguments failed governed validation.");
                }

                var arguments = argumentsElement
                    .EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => (object?)property.Value.Clone(),
                        StringComparer.Ordinal);
                var result = await InvokeWithTimeoutAsync(
                    tool,
                    arguments,
                    governance.TimeoutSeconds,
                    clientHandle,
                    cancellationToken);
                var validatedResult = McpRuntimeToolContract.ValidateStructuredResult(candidate, result);
                if (!validatedResult.IsValid || validatedResult.Value is not { } output)
                {
                    throw new InvalidOperationException(
                        validatedResult.Error ?? "MCP tool output failed governed validation.");
                }

                return output;
            }
        };
    }

    private async Task<ModelContextProtocol.Protocol.CallToolResult> InvokeWithTimeoutAsync(
        McpClientTool tool,
        IReadOnlyDictionary<string, object?> arguments,
        int timeoutSeconds,
        McpRuntimeClientHandle clientHandle,
        CancellationToken cancellationToken)
    {
        IDisposable? invocation = clientHandle.AcquireInvocation();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        timeoutCts.CancelAfter(timeout);
        Task<ModelContextProtocol.Protocol.CallToolResult>? callTask = null;

        try
        {
            callTask = tool.CallAsync(
                    arguments,
                    progress: null,
                    options: null,
                    timeoutCts.Token)
                .AsTask();
            return await callTask.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            timeoutCts.Cancel();
            ObserveLateToolCall(callTask!, invocation);
            invocation = null;
            throw new AiToolExecutionTimeoutException();
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutCts.IsCancellationRequested)
        {
            throw new AiToolExecutionTimeoutException();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (callTask is { IsCompleted: false })
            {
                ObserveLateToolCall(callTask, invocation);
                invocation = null;
            }

            throw;
        }
        finally
        {
            invocation?.Dispose();
        }
    }

    private void ObserveLateToolCall(
        Task<ModelContextProtocol.Protocol.CallToolResult> callTask,
        IDisposable invocation)
    {
        _ = ObserveLateToolCallCoreAsync(callTask, invocation);
    }

    private async Task ObserveLateToolCallCoreAsync(
        Task<ModelContextProtocol.Protocol.CallToolResult> callTask,
        IDisposable invocation)
    {
        try
        {
            _ = await callTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The deadline cancellation was expected and has now been observed.
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "A timed-out MCP tool call completed after its deadline. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                ex.GetType().Name);
        }
        finally
        {
            invocation.Dispose();
        }
    }

    private static McpRuntimeToolGovernance CreateDefaultGovernance(
        string toolCode,
        AiToolRiskLevel riskLevel)
    {
        return new McpRuntimeToolGovernance(
            toolCode,
            riskLevel,
            riskLevel is AiToolRiskLevel.RequiresApproval or AiToolRiskLevel.High or AiToolRiskLevel.Critical,
            RequiredPermission: null,
            AuditLevel: "Standard",
            DataBoundary: "NoData",
            SchemaVersion: 1,
            TimeoutSeconds: 120);
    }

    private McpRuntimeToolCandidate? TryCreateCandidate(
        McpServerInfo server,
        McpAllowedTool exposure,
        McpClientTool tool)
    {
        var targetDecision = AiToolSafetyPolicy.EvaluateConfiguredMcpTarget(
            server.ExternalSystemType,
            server.CapabilityKind);
        if (!targetDecision.IsAllowed)
        {
            logger.LogWarning(
                "MCP server {ServerName} tool {ToolName} was blocked because its configured target metadata is unverifiable. Reasons={Reasons}",
                server.Name,
                tool.Name,
                string.Join("; ", targetDecision.BlockReasons));
            return null;
        }

        if (McpRuntimeToolContract.TryCreateCandidate(
                server,
                exposure,
                tool,
                out var candidate,
                out var error))
        {
            logger.LogInformation(
                "MCP server {ServerName} tool {ToolName} passed safety policy. RuntimeName={RuntimeName}; ReadOnlyDeclared={ReadOnlyDeclared}; McpReadOnlyHint={McpReadOnlyHint}; McpDestructiveHint={McpDestructiveHint}",
                server.Name,
                tool.Name,
                AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, server.Name, tool.Name),
                candidate!.Exposure.ReadOnlyDeclared,
                candidate.McpReadOnlyHint,
                candidate.McpDestructiveHint);
            return candidate;
        }

        logger.LogWarning(
            "MCP server {ServerName} tool {ToolName} was blocked by safety policy. RuntimeName={RuntimeName}; Reasons={Reasons}",
            server.Name,
            tool.Name,
            AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, server.Name, tool.Name),
            error ?? "governed_contract_invalid");
        return null;
    }

    private bool HasUniqueCanonicalIdentity(
        string serverName,
        IGrouping<string, McpClientTool> tools)
    {
        if (tools.Count() == 1)
        {
            return true;
        }

        logger.LogWarning(
            "MCP server {ServerName} returned duplicate tool identity {ToolName}; every duplicate was blocked.",
            serverName,
            tools.Key);
        return false;
    }
}
