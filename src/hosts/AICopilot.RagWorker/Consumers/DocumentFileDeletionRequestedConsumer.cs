using AICopilot.RagService.Documents;
using AICopilot.Services.Contracts.Events;
using MassTransit;

namespace AICopilot.RagWorker.Consumers;

public sealed class DocumentFileDeletionRequestedConsumer(
    DocumentFileDeletionWorkflow workflow)
    : IConsumer<DocumentFileDeletionRequestedEvent>
{
    public Task Consume(ConsumeContext<DocumentFileDeletionRequestedEvent> context)
    {
        return workflow.ExecuteAsync(context.Message, context.CancellationToken);
    }
}
