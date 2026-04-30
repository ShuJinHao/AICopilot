using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Sessions;

public class Message : IEntity<int>
{
    protected Message()
    {
    }

    public Message(Session session, string content, MessageType type)
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
    }

    public SessionId SessionId { get; private set; }
    public string Content { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public MessageType Type { get; private set; }

    public Session Session { get; private set; } = null!;
    public int Id { get; private set; }
}
