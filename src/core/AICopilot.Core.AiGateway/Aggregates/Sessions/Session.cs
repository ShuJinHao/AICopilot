using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Sessions;

public class Session : BaseEntity<SessionId>, IAggregateRoot<SessionId>
{
    public const string DefaultTitle = "新会话";
    public const string UntitledTitle = "未命名会话";
    public const int MaxTitleLength = 48;
    public const int MaxMessageSummaryLength = 200;
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
        Title = DefaultTitle;
        UserId = userId;
        TemplateId = templateId;
        MessageCount = 0;
    }

    public string Title { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public ConversationTemplateId TemplateId { get; private set; }
    public string? LastMessageSummary { get; private set; }
    public DateTime? LastMessageAt { get; private set; }
    public int MessageCount { get; private set; }
    public DateTimeOffset? OnsiteConfirmedAt { get; private set; }
    public string? OnsiteConfirmedBy { get; private set; }
    public DateTimeOffset? OnsiteConfirmationExpiresAt { get; private set; }

    public IReadOnlyCollection<Message> Messages => _messages.AsReadOnly();

    public Message AddMessage(
        string? content,
        MessageType type,
        MessageModelSnapshot? modelSnapshot = null,
        string? renderPayloadJson = null)
    {
        if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(renderPayloadJson))
        {
            throw new ArgumentException("Message content is required.", nameof(content));
        }

        if (!Enum.IsDefined(typeof(MessageType), type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Message type is invalid.");
        }

        var message = new Message(this, content, type, MessageCount + 1, modelSnapshot, renderPayloadJson);
        _messages.Add(message);
        MessageCount++;
        LastMessageAt = message.CreatedAt;
        LastMessageSummary = BuildSummary(message.Content);
        if (type == MessageType.User && IsAutoTitleCandidate())
        {
            Title = BuildTitle(message.Content);
        }

        AddDomainEvent(new MessageAddedToSessionEvent(Id.Value, message.Content, message.Type, message.CreatedAt));
        return message;
    }

    public void Rename(string title)
    {
        Title = BuildTitle(title);
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

    public void EnsureMessageCountAtLeast(int messageCount)
    {
        if (messageCount > MessageCount)
        {
            MessageCount = messageCount;
        }
    }

    public bool HasValidOnsiteAttestation(DateTimeOffset nowUtc)
    {
        return OnsiteConfirmedAt.HasValue
               && !string.IsNullOrWhiteSpace(OnsiteConfirmedBy)
               && OnsiteConfirmationExpiresAt.HasValue
               && OnsiteConfirmationExpiresAt.Value > nowUtc;
    }

    private bool IsAutoTitleCandidate()
    {
        return string.Equals(Title, DefaultTitle, StringComparison.Ordinal)
               || string.Equals(Title, UntitledTitle, StringComparison.Ordinal)
               || string.IsNullOrWhiteSpace(Title);
    }

    private static string BuildTitle(string value)
    {
        var normalized = NormalizeInlineText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return UntitledTitle;
        }

        return normalized.Length <= MaxTitleLength
            ? normalized
            : normalized[..MaxTitleLength];
    }

    private static string BuildSummary(string value)
    {
        var normalized = NormalizeInlineText(value);
        return normalized.Length <= MaxMessageSummaryLength
            ? normalized
            : normalized[..MaxMessageSummaryLength];
    }

    private static string NormalizeInlineText(string value)
    {
        return string.Join(
            ' ',
            (value ?? string.Empty)
                .Trim()
                .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }
}
