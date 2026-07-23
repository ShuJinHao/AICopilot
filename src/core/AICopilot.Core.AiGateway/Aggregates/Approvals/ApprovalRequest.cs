using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Approvals;

public enum AgentApprovalType
{
    Plan = 0,
    ToolCall = 1,
    Artifact = 2,
    FinalOutput = 3
}

public sealed class ApprovalRequest : BaseEntity<ApprovalRequestId>, IAggregateRoot<ApprovalRequestId>
{
    private ApprovalRequest()
    {
    }

    public ApprovalRequest(
        AgentTaskId taskId,
        AgentApprovalType approvalType,
        string targetId,
        Guid requestedBy,
        DateTimeOffset nowUtc)
    {
        if (requestedBy == Guid.Empty)
        {
            throw new ArgumentException("Approval requester id is required.", nameof(requestedBy));
        }

        Id = ApprovalRequestId.New();
        TaskId = taskId;
        ApprovalType = approvalType;
        TargetId = NormalizeRequired(targetId, nameof(targetId), 200);
        Status = AgentApprovalStatus.Pending;
        RequestedBy = requestedBy;
        CreatedAt = nowUtc;
    }

    public AgentTaskId TaskId { get; private set; }

    public AgentApprovalType ApprovalType { get; private set; }

    public string TargetId { get; private set; } = string.Empty;

    public AgentApprovalStatus Status { get; private set; }

    public Guid RequestedBy { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public string? ApprovalComment { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public void Approve(Guid approvedBy, string? comment, DateTimeOffset nowUtc)
    {
        Complete(AgentApprovalStatus.Approved, approvedBy, comment, nowUtc);
    }

    public void Reject(Guid approvedBy, string? comment, DateTimeOffset nowUtc)
    {
        Complete(AgentApprovalStatus.Rejected, approvedBy, comment, nowUtc);
    }

    public void Cancel(DateTimeOffset nowUtc)
    {
        if (Status != AgentApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Only pending approval requests can be cancelled.");
        }

        Status = AgentApprovalStatus.Cancelled;
        ApprovedAt = nowUtc;
    }

    public void Expire(DateTimeOffset nowUtc)
    {
        if (Status != AgentApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Only pending approval requests can be expired.");
        }

        Status = AgentApprovalStatus.Expired;
        ApprovedAt = nowUtc;
    }

    private void Complete(AgentApprovalStatus status, Guid approvedBy, string? comment, DateTimeOffset nowUtc)
    {
        if (Status != AgentApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Only pending approval requests can be completed.");
        }

        if (approvedBy == Guid.Empty)
        {
            throw new ArgumentException("Approval operator id is required.", nameof(approvedBy));
        }

        Status = status;
        ApprovedBy = approvedBy;
        ApprovalComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        ApprovedAt = nowUtc;
    }

    private static string NormalizeRequired(string value, string paramName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
