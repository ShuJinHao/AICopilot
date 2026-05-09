namespace AICopilot.Services.Contracts.Events;

public record DocumentFileDeletionRequestedEvent
{
    public int DocumentId { get; init; }

    public Guid KnowledgeBaseId { get; init; }

    public string FilePath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;
}
