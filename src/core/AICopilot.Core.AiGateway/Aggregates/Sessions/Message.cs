using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Sessions;

public class Message : IEntity<int>
{
    public const int MaxRenderPayloadJsonLength = 4 * 1024 * 1024;

    protected Message()
    {
    }

    public Message(
        Session session,
        string? content,
        MessageType type,
        int sequence,
        MessageModelSnapshot? modelSnapshot = null,
        string? renderPayloadJson = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var normalizedContent = NormalizeOptionalText(content);
        var normalizedRenderPayload = NormalizeOptionalText(renderPayloadJson);

        if (string.IsNullOrWhiteSpace(normalizedContent) && string.IsNullOrWhiteSpace(normalizedRenderPayload))
        {
            throw new ArgumentException("Message content is required.", nameof(content));
        }

        if (normalizedRenderPayload?.Length > MaxRenderPayloadJsonLength)
        {
            throw new ArgumentOutOfRangeException(nameof(renderPayloadJson), $"Message render payload must not exceed {MaxRenderPayloadJsonLength} characters.");
        }

        if (!Enum.IsDefined(typeof(MessageType), type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Message type is invalid.");
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Message sequence must be positive.");
        }

        Session = session;
        SessionId = session.Id;
        Sequence = sequence;
        Content = normalizedContent ?? BuildStructuredFallback(type);
        Type = type;
        CreatedAt = DateTime.UtcNow;
        RenderPayloadJson = normalizedRenderPayload;
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
    public int Sequence { get; private set; }
    public Guid? FinalModelId { get; private set; }
    public string? FinalModelName { get; private set; }
    public Guid? RoutingModelId { get; private set; }
    public string? RoutingModelName { get; private set; }
    public int? ContextWindowTokens { get; private set; }
    public int? MaxOutputTokens { get; private set; }
    public string? RenderPayloadJson { get; private set; }

    public Session Session { get; private set; } = null!;
    public int Id { get; private set; }

    private static string BuildStructuredFallback(MessageType type)
    {
        return type == MessageType.User ? "用户结构化消息" : "A助理结构化执行事件";
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
