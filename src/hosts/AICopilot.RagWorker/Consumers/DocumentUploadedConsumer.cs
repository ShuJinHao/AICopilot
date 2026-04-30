using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Events;
using MassTransit;

namespace AICopilot.RagWorker.Consumers;

public sealed class DocumentUploadedConsumer(
    IDocumentIndexingService documentIndexingService,
    ILogger<DocumentUploadedConsumer> logger)
    : IConsumer<DocumentUploadedEvent>
{
    public async Task Consume(ConsumeContext<DocumentUploadedEvent> context)
    {
        var message = context.Message;
        logger.LogInformation(
            "接收到文档索引请求: {DocumentId}, 文件: {FileName}",
            message.DocumentId,
            message.FileName);

        await documentIndexingService.IndexAsync(message.DocumentId, context.CancellationToken);
    }
}
