using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Sessions;

public enum MessageEventType
{
    Message = 0,
    AgentTaskPlanCreated = 1,
    AgentTaskStepStarted = 2,
    AgentTaskStepCompleted = 3,
    ApprovalRequested = 4,
    ApprovalDecided = 5,
    ArtifactReady = 6,
    FinalOutputReady = 7
}

public sealed class MessageEvent : BaseEntity<MessageEventId>
{
    public const int MaxPayloadJsonLength = 256 * 1024;

    private MessageEvent()
    {
    }

    private MessageEvent(
        SessionId sessionId,
        int sequence,
        MessageEventType eventType,
        DateTimeOffset createdAt,
        Message? message = null,
        int? messageId = null,
        AgentTaskId? agentTaskId = null,
        AgentStepId? agentStepId = null,
        ApprovalRequestId? approvalRequestId = null,
        ArtifactWorkspaceId? artifactWorkspaceId = null,
        ArtifactId? artifactId = null,
        string? payloadJson = null)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Message event sequence must be positive.");
        }

        if (!Enum.IsDefined(typeof(MessageEventType), eventType))
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Message event type is invalid.");
        }

        var normalizedPayload = NormalizeOptionalPayload(payloadJson);
        if (normalizedPayload?.Length > MaxPayloadJsonLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadJson), $"Message event payload must not exceed {MaxPayloadJsonLength} characters.");
        }

        if (eventType == MessageEventType.Message && message is null && messageId is not > 0)
        {
            throw new ArgumentException("Message events must reference a persisted or tracked message.", nameof(message));
        }

        Id = MessageEventId.New();
        SessionId = sessionId;
        Sequence = sequence;
        EventType = eventType;
        CreatedAt = createdAt;
        Message = message;
        MessageId = message?.Id > 0 ? message.Id : messageId;
        AgentTaskId = agentTaskId;
        AgentStepId = agentStepId;
        ApprovalRequestId = approvalRequestId;
        ArtifactWorkspaceId = artifactWorkspaceId;
        ArtifactId = artifactId;
        PayloadJson = normalizedPayload;
    }

    public SessionId SessionId { get; private set; }

    public int Sequence { get; private set; }

    public MessageEventType EventType { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int? MessageId { get; private set; }

    public AgentTaskId? AgentTaskId { get; private set; }

    public AgentStepId? AgentStepId { get; private set; }

    public ApprovalRequestId? ApprovalRequestId { get; private set; }

    public ArtifactWorkspaceId? ArtifactWorkspaceId { get; private set; }

    public ArtifactId? ArtifactId { get; private set; }

    public string? PayloadJson { get; private set; }

    public Message? Message { get; private set; }

    public static MessageEvent ForMessage(SessionId sessionId, int sequence, Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new MessageEvent(sessionId, sequence, MessageEventType.Message, message.CreatedAt, message: message);
    }

    public static MessageEvent FromProjection(
        SessionId sessionId,
        int sequence,
        MessageEventType eventType,
        DateTimeOffset createdAt,
        AgentTaskId? agentTaskId = null,
        AgentStepId? agentStepId = null,
        ApprovalRequestId? approvalRequestId = null,
        ArtifactWorkspaceId? artifactWorkspaceId = null,
        ArtifactId? artifactId = null,
        string? payloadJson = null)
    {
        return new MessageEvent(
            sessionId,
            sequence,
            eventType,
            createdAt,
            agentTaskId: agentTaskId,
            agentStepId: agentStepId,
            approvalRequestId: approvalRequestId,
            artifactWorkspaceId: artifactWorkspaceId,
            artifactId: artifactId,
            payloadJson: payloadJson);
    }

    private static string? NormalizeOptionalPayload(string? payloadJson)
    {
        return string.IsNullOrWhiteSpace(payloadJson) ? null : payloadJson.Trim();
    }
}
