using System.Security.Cryptography;
using System.Text;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal static class AgentEvidenceSetDigest
{
    public static async Task<string> ComputeAsync(
        AiGatewayDbContext context,
        AgentTaskRunAttemptId runAttemptId,
        AgentEvidenceRecord current,
        CancellationToken cancellationToken)
    {
        var components = await context.AgentEvidenceRecords
            .AsNoTracking()
            .Where(evidence => evidence.RunAttemptId == runAttemptId && !evidence.IsRevoked)
            .Select(evidence => new
            {
                evidence.Id,
                evidence.NodeId,
                evidence.EnvelopeDigest,
                evidence.OutputDigest
            })
            .ToListAsync(cancellationToken);
        components.Add(new
        {
            current.Id,
            current.NodeId,
            current.EnvelopeDigest,
            current.OutputDigest
        });
        var canonical = string.Join(
            "\n",
            components
                .OrderBy(component => component.NodeId, StringComparer.Ordinal)
                .ThenBy(component => component.Id.Value)
                .Select(component =>
                    $"{component.Id.Value:D}|{component.NodeId}|{component.EnvelopeDigest}|{component.OutputDigest}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
