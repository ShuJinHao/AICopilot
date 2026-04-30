using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Sessions;

public class Session : BaseEntity<SessionId>, IAggregateRoot<SessionId>
{
    private static readonly TimeSpan MaxOnsiteAttestationLifetime = TimeSpan.FromMinutes(30);
    private readonly List<Message> _messages = [];

    protected Session()
    {
    }

    public Session(Guid userId, ConversationTemplateId templateId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Session user id is required.", nameof(userId));
        }


        Id = SessionId.New();
        Title = "新会话";
        UserId = userId;
        TemplateId = templateId;
    }

    public string Title { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public ConversationTemplateId TemplateId { get; private set; }
    public DateTimeOffset? OnsiteConfirmedAt { get; private set; }
    public string? OnsiteConfirmedBy { get; private set; }
    public DateTimeOffset? OnsiteConfirmationExpiresAt { get; private set; }

    public IReadOnlyCollection<Message> Messages => _messages.AsReadOnly();

    public void AddMessage(string content, MessageType type)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Message content is required.", nameof(content));
        }

        if (!Enum.IsDefined(typeof(MessageType), type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Message type is invalid.");
        }

        var message = new Message(this, content, type);
        _messages.Add(message);
        AddDomainEvent(new MessageAddedToSessionEvent(Id.Value, message.Content, message.Type, message.CreatedAt));
    }

    public void SetOnsiteAttestation(string confirmedBy, DateTimeOffset confirmedAtUtc, DateTimeOffset expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(confirmedBy))
        {
            throw new ArgumentException("Onsite attestation operator is required.", nameof(confirmedBy));
        }

        if (expiresAtUtc <= confirmedAtUtc)
        {
            throw new ArgumentException("Onsite attestation expiration must be later than confirmation time.", nameof(expiresAtUtc));
        }

        if (expiresAtUtc - confirmedAtUtc > MaxOnsiteAttestationLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Onsite attestation expiration cannot exceed 30 minutes.");
        }

        OnsiteConfirmedAt = confirmedAtUtc;
        OnsiteConfirmedBy = confirmedBy.Trim();
        OnsiteConfirmationExpiresAt = expiresAtUtc;
        AddDomainEvent(new OnsiteAttestationSetEvent(Id.Value, OnsiteConfirmedBy, confirmedAtUtc, expiresAtUtc));
    }

    public void ClearOnsiteAttestation()
    {
        OnsiteConfirmedAt = null;
        OnsiteConfirmedBy = null;
        OnsiteConfirmationExpiresAt = null;
    }

    public bool HasValidOnsiteAttestation(DateTimeOffset nowUtc)
    {
        return OnsiteConfirmedAt.HasValue
               && !string.IsNullOrWhiteSpace(OnsiteConfirmedBy)
               && OnsiteConfirmationExpiresAt.HasValue
               && OnsiteConfirmationExpiresAt.Value > nowUtc;
    }
}
