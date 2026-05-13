using System.Security.Cryptography;
using System.Text;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed class ToolExecutionAuditRecorder(IAuditLogWriter auditLogWriter)
{
    public async Task RecordResultAsync(
        AiFunctionResultContent resultContent,
        string toolName,
        AiToolIdentity? identity,
        CancellationToken cancellationToken)
    {
        var serializedResult = SerializeResult(resultContent.Result);
        var resultBytes = Encoding.UTF8.GetByteCount(serializedResult);
        var resultHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serializedResult)));
        var resultType = resultContent.Result?.GetType().FullName ?? "null";
        var metadata = new Dictionary<string, string>
        {
            ["callId"] = resultContent.CallId,
            ["toolName"] = toolName,
            ["resultType"] = resultType,
            ["resultBytes"] = resultBytes.ToString(),
            ["resultSha256"] = resultHash
        };

        if (identity is not null)
        {
            metadata["toolKind"] = identity.Kind.ToString();
            metadata["targetType"] = identity.TargetType.ToString();
            metadata["targetName"] = identity.TargetName;
            metadata["rawToolName"] = identity.ToolName;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "Tool.ExecuteResult",
                "ToolExecution",
                resultContent.CallId,
                toolName,
                AuditResults.Succeeded,
                $"Tool execution result observed: {toolName}; ResultType={resultType}; ResultBytes={resultBytes}; ResultSha256={resultHash[..12]}.",
                Metadata: metadata),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
    }

    private static string SerializeResult(object? result)
    {
        if (result is null)
        {
            return "null";
        }

        if (result is string text)
        {
            return text;
        }

        try
        {
            return result.ToJson();
        }
        catch (Exception exception)
        {
            return $"<unserializable:{result.GetType().FullName}:{exception.GetType().Name}>";
        }
    }
}
