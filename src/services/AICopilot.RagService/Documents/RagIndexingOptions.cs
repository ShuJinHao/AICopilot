namespace AICopilot.RagService.Documents;

public sealed class RagIndexingOptions
{
    public const string SectionName = "Rag:Indexing";

    public int ParsingTimeoutSeconds { get; set; } = 60;

    public int EmbeddingTimeoutSeconds { get; set; } = 180;

    public TimeSpan ParsingTimeout => ToTimeout(ParsingTimeoutSeconds, 60);

    public TimeSpan EmbeddingTimeout => ToTimeout(EmbeddingTimeoutSeconds, 180);

    private static TimeSpan ToTimeout(int seconds, int fallbackSeconds)
    {
        return TimeSpan.FromSeconds(seconds > 0 ? seconds : fallbackSeconds);
    }
}
