using System.Threading.Channels;
using AICopilot.AiGatewayService.Models;

namespace AICopilot.AiGatewayService.Workflows;

public sealed class ChatWorkflowSink
{
    private readonly Channel<ChatChunk> channel = Channel.CreateUnbounded<ChatChunk>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask WriteAsync(ChatChunk chunk, CancellationToken cancellationToken)
    {
        return channel.Writer.WriteAsync(chunk, cancellationToken);
    }

    public void Complete(Exception? exception = null)
    {
        channel.Writer.TryComplete(exception);
    }

    public IAsyncEnumerable<ChatChunk> ReadAllAsync(CancellationToken cancellationToken)
    {
        return channel.Reader.ReadAllAsync(cancellationToken);
    }
}
