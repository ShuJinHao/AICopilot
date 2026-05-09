using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Events;
using MassTransit;

namespace AICopilot.RagWorker.Consumers;

public sealed class DocumentFileDeletionRequestedConsumer(
    IFileStorageService fileStorage,
    ILogger<DocumentFileDeletionRequestedConsumer> logger)
    : IConsumer<DocumentFileDeletionRequestedEvent>
{
    public Task Consume(ConsumeContext<DocumentFileDeletionRequestedEvent> context)
    {
        return DeleteFileAsync(context.Message, context.CancellationToken);
    }

    public async Task DeleteFileAsync(
        DocumentFileDeletionRequestedEvent message,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Deleting RAG document file {FilePath} for document {DocumentId} in knowledge base {KnowledgeBaseId}.",
            message.FilePath,
            message.DocumentId,
            message.KnowledgeBaseId);

        await fileStorage.DeleteAsync(message.FilePath, cancellationToken);
    }
}
