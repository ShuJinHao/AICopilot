using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Events;
using MassTransit;

namespace AICopilot.RagWorker.Consumers;

public sealed class DocumentFileDeletionRequestedConsumer(
    IFileStorageService fileStorage,
    IPersistenceFileReconciliationJournal journal,
    IPersistenceFileReconciliationLeaseManager leaseManager,
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
            "Deleting RAG document file for document {DocumentId} in knowledge base {KnowledgeBaseId}.",
            message.DocumentId,
            message.KnowledgeBaseId);

        var pendingRecord = await journal.FindByStoragePathAsync(
            message.FilePath,
            cancellationToken);
        if (pendingRecord is null)
        {
            await fileStorage.DeleteAsync(message.FilePath, cancellationToken);
            return;
        }

        await using var lease = await leaseManager.TryAcquireAsync(
            pendingRecord.CommitId,
            cancellationToken);
        if (lease is null)
        {
            throw new InvalidOperationException(
                "The document file is still owned by an active persistence commit.");
        }

        var currentRecord = await journal.FindByStoragePathAsync(
            message.FilePath,
            cancellationToken);
        if (currentRecord is not null && currentRecord.CommitId != pendingRecord.CommitId)
        {
            throw new InvalidOperationException(
                "The document file persistence ownership changed while acquiring its cleanup lease.");
        }

        if (currentRecord is not null)
        {
            await journal.CompleteAsync(currentRecord.CommitId, cancellationToken);
        }

        await fileStorage.DeleteAsync(message.FilePath, cancellationToken);
    }
}
