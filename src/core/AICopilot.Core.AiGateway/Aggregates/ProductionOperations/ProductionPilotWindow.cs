using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.ProductionOperations;

public sealed class ProductionPilotWindow
    : BaseEntity<ProductionPilotWindowId>, IAggregateRoot<ProductionPilotWindowId>
{
    private ProductionPilotWindow()
    {
    }

    public ProductionPilotWindow(
        string windowId,
        string name,
        string status,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        IReadOnlyCollection<string>? allowedEndpointCodes,
        int maxTimeRangeDays,
        int maxRows,
        int timeoutMs,
        string ownerDepartment,
        string approvalPolicy,
        string rollbackPolicy,
        DateTimeOffset nowUtc)
    {
        Id = ProductionPilotWindowId.New();
        CreatedAt = nowUtc;
        Update(
            windowId,
            name,
            status,
            startAt,
            endAt,
            allowedEndpointCodes,
            maxTimeRangeDays,
            maxRows,
            timeoutMs,
            ownerDepartment,
            approvalPolicy,
            rollbackPolicy,
            nowUtc);
    }

    public string WindowId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public DateTimeOffset StartAt { get; private set; }

    public DateTimeOffset EndAt { get; private set; }

    public string[] AllowedEndpointCodes { get; private set; } = [];

    public int MaxTimeRangeDays { get; private set; }

    public int MaxRows { get; private set; }

    public int TimeoutMs { get; private set; }

    public string OwnerDepartment { get; private set; } = string.Empty;

    public string ApprovalPolicy { get; private set; } = string.Empty;

    public string RollbackPolicy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        string windowId,
        string name,
        string status,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        IReadOnlyCollection<string>? allowedEndpointCodes,
        int maxTimeRangeDays,
        int maxRows,
        int timeoutMs,
        string ownerDepartment,
        string approvalPolicy,
        string rollbackPolicy,
        DateTimeOffset nowUtc)
    {
        WindowId = ProductionPilotEmergencyStopState.NormalizeRequired(windowId, nameof(windowId), 160);
        Name = ProductionPilotEmergencyStopState.NormalizeRequired(name, nameof(name), 200);
        Status = ProductionPilotEmergencyStopState.NormalizeRequired(status, nameof(status), 80);
        StartAt = startAt;
        EndAt = endAt;
        AllowedEndpointCodes = ProductionPilotEmergencyStopState.NormalizeStrings(allowedEndpointCodes, 120);
        MaxTimeRangeDays = Math.Max(1, maxTimeRangeDays);
        MaxRows = Math.Max(1, maxRows);
        TimeoutMs = Math.Max(500, timeoutMs);
        OwnerDepartment = ProductionPilotEmergencyStopState.NormalizeRequired(ownerDepartment, nameof(ownerDepartment), 120);
        ApprovalPolicy = ProductionPilotEmergencyStopState.NormalizeRequired(approvalPolicy, nameof(approvalPolicy), 120);
        RollbackPolicy = ProductionPilotEmergencyStopState.NormalizeRequired(rollbackPolicy, nameof(rollbackPolicy), 160);
        UpdatedAt = nowUtc;
    }
}
