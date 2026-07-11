using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.Uploads;

public sealed class UploadRecordByIdForUserSpec : Specification<UploadRecord>
{
    public UploadRecordByIdForUserSpec(UploadRecordId id, Guid userId)
    {
        FilterCondition = record =>
            record.Id == id &&
            record.UserId == userId &&
            record.Status == UploadRecordStatus.Uploaded &&
            (record.Scope == UploadRecordScope.SessionTemp ||
             record.Scope == UploadRecordScope.AgentInput);
    }
}

public sealed class UploadRecordsByIdsForUserSpec : Specification<UploadRecord>
{
    public UploadRecordsByIdsForUserSpec(IReadOnlyCollection<UploadRecordId> ids, Guid userId)
    {
        FilterCondition = record =>
            ids.Contains(record.Id) &&
            record.UserId == userId &&
            record.Status == UploadRecordStatus.Uploaded &&
            (record.Scope == UploadRecordScope.SessionTemp ||
             record.Scope == UploadRecordScope.AgentInput);
    }
}

public sealed class UploadRecordsBySessionForUserSpec : Specification<UploadRecord>
{
    public UploadRecordsBySessionForUserSpec(SessionId sessionId, Guid userId)
    {
        FilterCondition = record =>
            record.SessionId == sessionId &&
            record.UserId == userId &&
            record.Status == UploadRecordStatus.Uploaded &&
            record.Scope == UploadRecordScope.SessionTemp;
        SetOrderByDescending(record => record.CreatedAt);
    }
}

public sealed class UploadRecordsByAgentTaskForUserSpec : Specification<UploadRecord>
{
    public UploadRecordsByAgentTaskForUserSpec(AgentTaskId taskId, Guid userId)
    {
        FilterCondition = record =>
            record.AgentTaskId == taskId &&
            record.UserId == userId &&
            record.Status == UploadRecordStatus.Uploaded &&
            record.Scope == UploadRecordScope.AgentInput;
        SetOrderBy(record => record.CreatedAt);
    }
}
