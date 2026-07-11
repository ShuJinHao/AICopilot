using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Uploads;

public enum UploadRecordScope
{
    SessionTemp = 0,
    AgentInput = 1,
    KnowledgeBase = 2
}

public enum UploadRecordStatus
{
    Uploaded = 0,
    LinkedToKnowledgeBase = 1,
    Failed = 2,
    Deleted = 3
}

public sealed class UploadRecord : BaseEntity<UploadRecordId>, IAggregateRoot<UploadRecordId>
{
    private UploadRecord()
    {
    }

    public UploadRecord(
        UploadRecordScope scope,
        Guid userId,
        SessionId? sessionId,
        AgentTaskId? agentTaskId,
        string fileName,
        string contentType,
        long fileSize,
        string sha256,
        string storagePath,
        DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Upload user id is required.", nameof(userId));
        }

        if (scope is not UploadRecordScope.SessionTemp and not UploadRecordScope.AgentInput)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scope),
                scope,
                "Only session and agent-input uploads are active AiGateway upload scopes.");
        }

        if (scope == UploadRecordScope.SessionTemp &&
            (!sessionId.HasValue || agentTaskId.HasValue))
        {
            throw new ArgumentException(
                "Session uploads require only a session id.",
                nameof(sessionId));
        }

        if (scope == UploadRecordScope.AgentInput &&
            (!agentTaskId.HasValue || sessionId.HasValue))
        {
            throw new ArgumentException(
                "Agent-input uploads require only an agent task id.",
                nameof(agentTaskId));
        }

        Id = UploadRecordId.New();
        Scope = scope;
        UserId = userId;
        SessionId = sessionId;
        AgentTaskId = agentTaskId;
        FileName = NormalizeRequired(fileName, nameof(fileName), 255);
        ContentType = NormalizeOptional(contentType, 200) ?? "application/octet-stream";
        FileSize = fileSize >= 0 ? fileSize : throw new ArgumentOutOfRangeException(nameof(fileSize));
        Sha256 = NormalizeRequired(sha256, nameof(sha256), 128).ToLowerInvariant();
        StoragePath = NormalizeRequired(storagePath, nameof(storagePath), 1000);
        Status = UploadRecordStatus.Uploaded;
        CreatedAt = nowUtc;
    }

    public UploadRecordScope Scope { get; private set; }

    public Guid UserId { get; private set; }

    public SessionId? SessionId { get; private set; }

    public AgentTaskId? AgentTaskId { get; private set; }

    public Guid? KnowledgeBaseId { get; private set; }

    public int? RagDocumentId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long FileSize { get; private set; }

    public string Sha256 { get; private set; } = string.Empty;

    public string? StoragePath { get; private set; }

    public UploadRecordStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private static string NormalizeRequired(string value, string paramName, int maxLength)
    {
        var normalized = NormalizeOptional(value, maxLength);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }
}
