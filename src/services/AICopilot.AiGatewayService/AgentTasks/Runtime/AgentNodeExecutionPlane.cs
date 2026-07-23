using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentNodeExecutionContract(
    string Lifecycle,
    string NodeId,
    string NodeKind,
    bool Required,
    string OutputSchemaRef,
    int TimeoutSeconds,
    int MaxAttempts,
    string BackoffClass,
    string SideEffectClass)
{
    public static AgentNodeExecutionContract ForChat(
        string nodeId,
        string nodeKind,
        bool required,
        string branch) =>
        new(
            "Chat",
            nodeId,
            nodeKind,
            required,
            $"evidence:chat-{branch.ToLowerInvariant()}:v1",
            TimeoutSeconds: 3_600,
            MaxAttempts: 1,
            BackoffClass: "None",
            SideEffectClass: "ReadOnly");

    public static AgentNodeExecutionContract ForDurable(AgentPlanNodeDocument node) =>
        new(
            "Durable",
            node.NodeId,
            node.NodeKind,
            node.Required,
            node.OutputSchemaRef,
            node.TimeoutPolicy.TimeoutSeconds,
            node.RetryPolicy.MaxAttempts,
            node.RetryPolicy.BackoffClass,
            node.SideEffectClass);
}

/// <summary>
/// Shared Node execution boundary for request-scoped Chat branches and durable
/// AgentTask nodes. Queueing, checkpointing, streaming and retries remain owned by
/// their orchestrators; contract validation and executor invocation do not fork.
/// </summary>
internal static class AgentNodeExecutionPlane
{
    public static async Task<T> ExecuteAsync<T>(
        AgentNodeExecutionContract contract,
        Func<CancellationToken, Task<T>> executor,
        CancellationToken cancellationToken,
        Action<int>? reportAttemptCount = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(executor);
        ValidateContract(contract);
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reportAttemptCount?.Invoke(attempt);
            try
            {
                return await executor(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                attempt < contract.MaxAttempts &&
                ShouldRetry(exception, contract.SideEffectClass))
            {
                AgentRuntimeTelemetry.RecordRetry(contract.NodeKind);
                var delay = ResolveRetryDelay(contract.BackoffClass, attempt);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
    }

    private static void ValidateContract(AgentNodeExecutionContract contract)
    {
        if (contract.Lifecycle is not ("Chat" or "Durable") ||
            string.IsNullOrWhiteSpace(contract.NodeId) ||
            contract.NodeId.Length > 160 ||
            !AgentPlanContractSchemaAuthority.AllowedNodeKinds.Contains(contract.NodeKind) ||
            string.IsNullOrWhiteSpace(contract.OutputSchemaRef) ||
            !contract.OutputSchemaRef.StartsWith("evidence:", StringComparison.Ordinal) ||
            !contract.OutputSchemaRef.EndsWith(":v1", StringComparison.Ordinal) ||
            contract.TimeoutSeconds is < 1 or > 3_600 ||
            contract.MaxAttempts is < 1 or > 5 ||
            contract.BackoffClass is not ("None" or "Fixed" or "Exponential") ||
            contract.SideEffectClass is not ("ReadOnly" or "DeterministicInternal" or "ArtifactDraftOnly") ||
            contract.MaxAttempts > 1 && !IsReplaySafe(contract.SideEffectClass))
        {
            throw new InvalidOperationException(
                $"Node '{contract.NodeId}' is outside the shared Node execution contract.");
        }
    }

    private static bool IsReplaySafe(string sideEffectClass) =>
        sideEffectClass is "ReadOnly" or "DeterministicInternal";

    private static bool ShouldRetry(Exception exception, string sideEffectClass)
    {
        if (!IsReplaySafe(sideEffectClass))
        {
            return false;
        }

        if (exception is AgentToolExecutionException toolException)
        {
            return toolException.Code is
                AppProblemCodes.ToolExecutionTimeout or
                AppProblemCodes.ModelRequestTimeout or
                AppProblemCodes.ModelProviderUnavailable or
                AppProblemCodes.RateLimitExceeded;
        }

        if (exception is CloudAiReadException cloudException)
        {
            return cloudException.Code is
                       CloudAiReadProblemCodes.RateLimited or
                       CloudAiReadProblemCodes.Unavailable ||
                   cloudException.StatusCode is { } statusCode &&
                   ((int)statusCode == 408 || (int)statusCode == 429 || (int)statusCode >= 500);
        }

        return exception is TimeoutException or HttpRequestException or IOException;
    }

    private static TimeSpan ResolveRetryDelay(string backoffClass, int failedAttempt) =>
        backoffClass switch
        {
            "None" => TimeSpan.Zero,
            "Fixed" => TimeSpan.FromMilliseconds(100),
            "Exponential" => TimeSpan.FromMilliseconds(Math.Min(800, 100 * (1 << Math.Min(3, failedAttempt - 1)))),
            _ => throw new InvalidOperationException("Node retry backoff class is invalid.")
        };
}
