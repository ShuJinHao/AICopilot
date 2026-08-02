using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Agents;

public sealed class TrustedRenderChunkBuffer
{
    private readonly object gate = new();
    private readonly Queue<ChatChunk> chunks = new();

    public async Task CaptureWidgetsAsync(
        AgentWorkflowSink sink,
        CancellationToken cancellationToken)
    {
        await foreach (var chunk in sink.ReadAllAsync(cancellationToken))
        {
            if (!IsTrustedWidget(chunk))
            {
                continue;
            }

            lock (gate)
            {
                chunks.Enqueue(chunk);
            }
        }
    }

    public IReadOnlyList<ChatChunk> Drain()
    {
        lock (gate)
        {
            if (chunks.Count == 0)
            {
                return Array.Empty<ChatChunk>();
            }

            var drained = chunks.ToArray();
            chunks.Clear();
            return drained;
        }
    }

    internal static bool IsTrustedWidget(ChatChunk chunk)
    {
        if (chunk.Type != ChunkType.Widget ||
            Encoding.UTF8.GetByteCount(chunk.Content) >
            AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(chunk.Content);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            return type.GetString() is "Chart" or "DataTable" or "StatsCard";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
