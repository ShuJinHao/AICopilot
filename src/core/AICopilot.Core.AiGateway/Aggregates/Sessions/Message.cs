using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Sessions;

public class Message : IEntity<int>
{
    protected Message()
    {
    }

    public Message(Session session, string content, MessageType type, MessageModelSnapshot? modelSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Message content is required.", nameof(content));
        }

        if (!Enum.IsDefined(typeof(MessageType), type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Message type is invalid.");
        }

        Session = session;
        SessionId = session.Id;
        Content = content.Trim();
        Type = type;
        CreatedAt = DateTime.UtcNow;
        FinalModelId = modelSnapshot?.FinalModelId;
        FinalModelName = NormalizeOptionalText(modelSnapshot?.FinalModelName);
        RoutingModelId = modelSnapshot?.RoutingModelId;
        RoutingModelName = NormalizeOptionalText(modelSnapshot?.RoutingModelName);
        ContextWindowTokens = modelSnapshot?.ContextWindowTokens;
        MaxOutputTokens = modelSnapshot?.MaxOutputTokens;
    }

    public SessionId SessionId { get; private set; }
    public string Content { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public MessageType Type { get; private set; }
    public Guid? FinalModelId { get; private set; }
    public string? FinalModelName { get; private set; }
    public Guid? RoutingModelId { get; private set; }
    public string? RoutingModelName { get; private set; }
    public int? ContextWindowTokens { get; private set; }
    public int? MaxOutputTokens { get; private set; }

    public Session Session { get; private set; } = null!;
    public int Id { get; private set; }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
