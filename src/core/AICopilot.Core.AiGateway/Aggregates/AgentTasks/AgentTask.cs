using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.AgentTasks;

public sealed class AgentTask : BaseEntity<AgentTaskId>, IAggregateRoot<AgentTaskId>
{
    private readonly List<AgentStep> _steps = [];

    private AgentTask()
    {
    }

    public AgentTask(
        SessionId sessionId,
        Guid userId,
        string title,
        string goal,
        AgentTaskType taskType,
        AgentTaskRiskLevel riskLevel,
        LanguageModelId? modelId,
        string planJson,
        DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Agent task user id is required.", nameof(userId));
        }

        Id = AgentTaskId.New();
        TaskCode = $"task_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..38];
        SessionId = sessionId;
        UserId = userId;
        Title = NormalizeRequired(title, nameof(title), 200);
        Goal = NormalizeRequired(goal, nameof(goal), 2000);
        TaskType = taskType;
        RiskLevel = riskLevel;
        ModelId = modelId;
        PlanJson = NormalizeRequired(planJson, nameof(planJson), 32000);
        Status = AgentTaskStatus.Draft;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public string TaskCode { get; private set; } = string.Empty;

    public SessionId SessionId { get; private set; }

    public Guid UserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Goal { get; private set; } = string.Empty;

    public AgentTaskType TaskType { get; private set; }

    public AgentTaskStatus Status { get; private set; }

    public AgentTaskRiskLevel RiskLevel { get; private set; }

    public LanguageModelId? ModelId { get; private set; }

    public ArtifactWorkspaceId? WorkspaceId { get; private set; }

    public AgentTaskRunAttemptId? ActiveRunAttemptId { get; private set; }

    public int RunAttemptCount { get; private set; }

    public Guid? RunLeaseId { get; private set; }

    public string? RunLeaseOwner { get; private set; }

    public DateTimeOffset? RunLeaseExpiresAt { get; private set; }

    public string PlanJson { get; private set; } = string.Empty;

    public string? FinalSummary { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyCollection<AgentStep> Steps => _steps.AsReadOnly();

    public bool IsRunInProgress(DateTimeOffset nowUtc)
    {
        return RunLeaseExpiresAt.HasValue && RunLeaseExpiresAt.Value > nowUtc;
    }

    public void BeginRunAttempt(
        AgentTaskRunAttemptId attemptId,
        int attemptNo,
        Guid leaseId,
        string leaseOwner,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset nowUtc)
    {
        if (attemptNo <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNo), "Run attempt number must be greater than zero.");
        }

        ActiveRunAttemptId = attemptId;
        RunAttemptCount = Math.Max(RunAttemptCount, attemptNo);
        AcquireRunLease(leaseId, leaseOwner, leaseExpiresAt, nowUtc);
    }

    public void AcquireRunLease(
        Guid leaseId,
        string leaseOwner,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset nowUtc)
    {
        if (leaseId == Guid.Empty)
        {
            throw new ArgumentException("Run lease id is required.", nameof(leaseId));
        }

        RunLeaseId = leaseId;
        RunLeaseOwner = string.IsNullOrWhiteSpace(leaseOwner) ? "agent-runtime" : leaseOwner.Trim()[..Math.Min(leaseOwner.Trim().Length, 120)];
        RunLeaseExpiresAt = leaseExpiresAt;
        UpdatedAt = nowUtc;
    }

    public void RefreshRunLease(DateTimeOffset leaseExpiresAt, DateTimeOffset nowUtc)
    {
        if (!RunLeaseId.HasValue)
        {
            throw new InvalidOperationException("Cannot refresh an agent run lease that has not been acquired.");
        }

        RunLeaseExpiresAt = leaseExpiresAt;
        UpdatedAt = nowUtc;
    }

    public void ReleaseRunLease(DateTimeOffset nowUtc, bool clearActiveAttempt = false)
    {
        RunLeaseId = null;
        RunLeaseOwner = null;
        RunLeaseExpiresAt = null;
        if (clearActiveAttempt)
        {
            ActiveRunAttemptId = null;
        }

        UpdatedAt = nowUtc;
    }

    public AgentStep AddStep(
        string title,
        string description,
        AgentStepType stepType,
        string? toolCode,
        bool requiresApproval,
        DateTimeOffset nowUtc,
        string? inputJson = null)
    {
        EnsureMutablePlan();
        var step = new AgentStep(Id, _steps.Count + 1, title, description, stepType, toolCode, requiresApproval, inputJson);
        _steps.Add(step);
        UpdatedAt = nowUtc;
        return step;
    }

    public void ApprovePlan(DateTimeOffset nowUtc)
    {
        if (Status != AgentTaskStatus.WaitingPlanApproval)
        {
            throw new InvalidOperationException("Only tasks waiting for plan approval can be approved.");
        }

        Status = AgentTaskStatus.PlanApproved;
        UpdatedAt = nowUtc;
    }

    public void ConfirmExecutablePlan(
        string planJson,
        IReadOnlyCollection<int> approvalRequiredStepIndexes,
        DateTimeOffset nowUtc)
    {
        if (Status is not AgentTaskStatus.Draft and not AgentTaskStatus.WaitingPlanApproval)
        {
            throw new InvalidOperationException("Only draft or plan-approval-waiting tasks can be confirmed.");
        }

        PlanJson = NormalizeRequired(planJson, nameof(planJson), 32000);
        Status = AgentTaskStatus.WaitingPlanApproval;
        var approvalIndexes = approvalRequiredStepIndexes
            .ToHashSet();
        foreach (var step in _steps)
        {
            if (approvalIndexes.Contains(step.StepIndex) && !step.RequiresApproval)
            {
                step.WaitForApproval();
            }
        }

        UpdatedAt = nowUtc;
    }

    public void Start(DateTimeOffset nowUtc)
    {
        if (Status is not AgentTaskStatus.PlanApproved and not AgentTaskStatus.WaitingToolApproval)
        {
            throw new InvalidOperationException("Only approved agent tasks can start.");
        }

        Status = AgentTaskStatus.Running;
        UpdatedAt = nowUtc;
    }

    public void BeginArtifactGeneration(DateTimeOffset nowUtc)
    {
        if (Status != AgentTaskStatus.Running)
        {
            throw new InvalidOperationException("Only running agent tasks can generate artifacts.");
        }

        Status = AgentTaskStatus.GeneratingArtifacts;
        UpdatedAt = nowUtc;
    }

    public void WaitForToolApproval(DateTimeOffset nowUtc)
    {
        if (Status is not AgentTaskStatus.Running and not AgentTaskStatus.GeneratingArtifacts)
        {
            throw new InvalidOperationException("Only running agent tasks can wait for tool approval.");
        }

        Status = AgentTaskStatus.WaitingToolApproval;
        UpdatedAt = nowUtc;
    }

    public void AttachWorkspace(ArtifactWorkspaceId workspaceId, DateTimeOffset nowUtc)
    {
        if (WorkspaceId is not null)
        {
            throw new InvalidOperationException("Agent task workspace is already attached.");
        }

        WorkspaceId = workspaceId;
        UpdatedAt = nowUtc;
    }

    public void MarkWorkspaceReady(DateTimeOffset nowUtc)
    {
        if (WorkspaceId is null)
        {
            throw new InvalidOperationException("Workspace must be attached before marking a task workspace-ready.");
        }

        Status = AgentTaskStatus.WorkspaceReady;
        UpdatedAt = nowUtc;
    }

    public void WaitForFinalApproval(DateTimeOffset nowUtc)
    {
        if (Status is not AgentTaskStatus.WorkspaceReady and not AgentTaskStatus.WaitingFinalApproval)
        {
            throw new InvalidOperationException("Only workspace-ready tasks can wait for final approval.");
        }

        Status = AgentTaskStatus.WaitingFinalApproval;
        UpdatedAt = nowUtc;
    }

    public void MarkFinalized(DateTimeOffset nowUtc)
    {
        if (Status != AgentTaskStatus.WaitingFinalApproval)
        {
            throw new InvalidOperationException("Only final-approval-waiting tasks can be finalized.");
        }

        Status = AgentTaskStatus.Finalized;
        UpdatedAt = nowUtc;
    }

    public void Complete(string finalSummary, DateTimeOffset nowUtc)
    {
        if (Status is not AgentTaskStatus.WaitingFinalApproval and not AgentTaskStatus.Finalized)
        {
            throw new InvalidOperationException("Only final-approval-waiting or finalized tasks can complete.");
        }

        FinalSummary = NormalizeRequired(finalSummary, nameof(finalSummary), 4000);
        Status = AgentTaskStatus.Completed;
        UpdatedAt = nowUtc;
        CompletedAt = nowUtc;
    }

    public void Fail(string failureSummary, DateTimeOffset nowUtc)
    {
        FinalSummary = NormalizeRequired(failureSummary, nameof(failureSummary), 4000);
        Status = AgentTaskStatus.Failed;
        UpdatedAt = nowUtc;
        CompletedAt = nowUtc;
    }

    public void Cancel(DateTimeOffset nowUtc)
    {
        Status = AgentTaskStatus.Cancelled;
        ReleaseRunLease(nowUtc, clearActiveAttempt: true);
        UpdatedAt = nowUtc;
        CompletedAt = nowUtc;
    }

    public void Reject(string reason, DateTimeOffset nowUtc)
    {
        FinalSummary = NormalizeRequired(reason, nameof(reason), 4000);
        Status = AgentTaskStatus.Rejected;
        ReleaseRunLease(nowUtc, clearActiveAttempt: true);
        UpdatedAt = nowUtc;
        CompletedAt = nowUtc;
    }

    public void PrepareRetry(DateTimeOffset nowUtc)
    {
        if (Status != AgentTaskStatus.Failed)
        {
            throw new InvalidOperationException("Only failed agent tasks can be retried.");
        }

        var firstFailedIndex = _steps
            .Where(step => step.Status == AgentStepStatus.Failed)
            .OrderBy(step => step.StepIndex)
            .Select(step => (int?)step.StepIndex)
            .FirstOrDefault();
        foreach (var step in _steps.OrderBy(step => step.StepIndex))
        {
            if (step.Status == AgentStepStatus.Completed)
            {
                continue;
            }

            if (!firstFailedIndex.HasValue || step.StepIndex >= firstFailedIndex.Value)
            {
                step.ResetForRetry();
            }
        }

        Status = AgentTaskStatus.PlanApproved;
        FinalSummary = null;
        CompletedAt = null;
        ReleaseRunLease(nowUtc, clearActiveAttempt: true);
        UpdatedAt = nowUtc;
    }

    private void EnsureMutablePlan()
    {
        if (Status is not AgentTaskStatus.Draft and not AgentTaskStatus.WaitingPlanApproval)
        {
            throw new InvalidOperationException("Agent task steps can only be added before plan confirmation.");
        }
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
