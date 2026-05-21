using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.TrialOperations;

public enum TrialCampaignStatus
{
    Draft = 0,
    Active = 1,
    Paused = 2,
    Completed = 3,
    Archived = 4
}

public enum TrialScenarioRunStatus
{
    Planned = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Blocked = 4
}

public enum TrialRiskSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum TrialRiskStatus
{
    Open = 0,
    Mitigating = 1,
    Resolved = 2,
    ClosedAsOutOfScope = 3
}

public enum PilotReadinessStatus
{
    NotEvaluated = 0,
    CollectingEvidence = 1,
    Blocked = 2,
    ReadyForP11Planning = 3
}

public sealed class TrialCampaign : BaseEntity<TrialCampaignId>, IAggregateRoot<TrialCampaignId>
{
    public static readonly string[] SupportedSourceModes =
    [
        "SimulationBusiness",
        "CloudReadonlySandbox"
    ];

    private readonly List<TrialScenarioRun> _scenarioRuns = [];
    private readonly List<TrialRiskIssue> _riskIssues = [];

    private TrialCampaign()
    {
    }

    public TrialCampaign(
        string name,
        IReadOnlyCollection<string>? allowedSourceModes,
        string? ownerDepartment,
        DateTimeOffset? startAt,
        DateTimeOffset? endAt,
        string? summary,
        DateTimeOffset nowUtc)
    {
        Id = TrialCampaignId.New();
        Name = NormalizeRequired(name, nameof(name), 200);
        AllowedSourceModes = NormalizeSourceModes(allowedSourceModes);
        OwnerDepartment = NormalizeOptional(ownerDepartment, 160);
        StartAt = startAt;
        EndAt = endAt;
        Summary = NormalizeOptional(summary, 2000);
        Status = TrialCampaignStatus.Draft;
        PilotReadinessStatus = PilotReadinessStatus.NotEvaluated;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public string Name { get; private set; } = string.Empty;

    public TrialCampaignStatus Status { get; private set; }

    public string[] AllowedSourceModes { get; private set; } = [];

    public string? OwnerDepartment { get; private set; }

    public DateTimeOffset? StartAt { get; private set; }

    public DateTimeOffset? EndAt { get; private set; }

    public string? Summary { get; private set; }

    public PilotReadinessStatus PilotReadinessStatus { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<TrialScenarioRun> ScenarioRuns => _scenarioRuns.AsReadOnly();

    public IReadOnlyCollection<TrialRiskIssue> RiskIssues => _riskIssues.AsReadOnly();

    public void UpdateStatus(TrialCampaignStatus status, DateTimeOffset nowUtc)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Trial campaign status is invalid.");
        }

        Status = status;
        UpdatedAt = nowUtc;
    }

    public TrialScenarioRun AttachScenarioRun(
        string scenarioId,
        string trialMode,
        string sourceMode,
        string? boundary,
        AgentTaskId taskId,
        IReadOnlyCollection<Guid>? artifactIds,
        IReadOnlyCollection<string>? queryHashes,
        IReadOnlyCollection<string>? resultHashes,
        string approvalStatus,
        TrialScenarioRunStatus status,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        DateTimeOffset nowUtc)
    {
        var normalizedSourceMode = NormalizeAllowedSourceMode(sourceMode);
        if (!AllowedSourceModes.Contains(normalizedSourceMode, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"trial_source_mode_blocked: Source mode {normalizedSourceMode} is not allowed for this campaign.");
        }

        var existing = _scenarioRuns.FirstOrDefault(item => item.TaskId == taskId);
        if (existing is not null)
        {
            existing.Update(
                scenarioId,
                trialMode,
                normalizedSourceMode,
                boundary,
                artifactIds,
                queryHashes,
                resultHashes,
                approvalStatus,
                status,
                startedAt,
                completedAt,
                nowUtc);
            UpdatedAt = nowUtc;
            return existing;
        }

        var run = new TrialScenarioRun(
            Id,
            scenarioId,
            trialMode,
            normalizedSourceMode,
            boundary,
            taskId,
            artifactIds,
            queryHashes,
            resultHashes,
            approvalStatus,
            status,
            startedAt,
            completedAt,
            nowUtc);
        _scenarioRuns.Add(run);
        UpdatedAt = nowUtc;
        return run;
    }

    public TrialRiskIssue UpsertRiskIssue(
        TrialRiskIssueId? issueId,
        TrialRiskSeverity severity,
        string category,
        TrialRiskStatus status,
        string? owner,
        string? sourceRef,
        string? resolutionHash,
        DateTimeOffset nowUtc)
    {
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity), severity, "Trial risk severity is invalid.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Trial risk status is invalid.");
        }

        var existing = issueId.HasValue
            ? _riskIssues.FirstOrDefault(item => item.Id == issueId.Value)
            : null;
        if (existing is not null)
        {
            existing.Update(severity, category, status, owner, sourceRef, resolutionHash, nowUtc);
            UpdatedAt = nowUtc;
            return existing;
        }

        var issue = new TrialRiskIssue(Id, severity, category, status, owner, sourceRef, resolutionHash, nowUtc);
        _riskIssues.Add(issue);
        UpdatedAt = nowUtc;
        return issue;
    }

    public void SetPilotReadinessStatus(PilotReadinessStatus status, DateTimeOffset nowUtc)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Pilot readiness status is invalid.");
        }

        PilotReadinessStatus = status;
        UpdatedAt = nowUtc;
    }

    private static string[] NormalizeSourceModes(IReadOnlyCollection<string>? sourceModes)
    {
        var values = sourceModes is { Count: > 0 }
            ? sourceModes
            : SupportedSourceModes;
        return values
            .Select(NormalizeAllowedSourceMode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeAllowedSourceMode(string sourceMode)
    {
        var normalized = NormalizeRequired(sourceMode, nameof(sourceMode), 80);
        var allowed = SupportedSourceModes.FirstOrDefault(mode =>
            string.Equals(mode, normalized, StringComparison.OrdinalIgnoreCase));
        if (allowed is null)
        {
            throw new InvalidOperationException($"trial_source_mode_blocked: Source mode {normalized} is outside P10 trial boundaries.");
        }

        return allowed;
    }

    internal static string NormalizeRequired(string? value, string paramName, int maxLength)
    {
        var normalized = NormalizeOptional(value, maxLength);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized;
    }

    internal static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }

    internal static string[] NormalizeStrings(IReadOnlyCollection<string>? values, int maxLength)
    {
        return (values ?? [])
            .Select(value => NormalizeOptional(value, maxLength))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static Guid[] NormalizeGuids(IReadOnlyCollection<Guid>? values)
    {
        return (values ?? [])
            .Where(value => value != Guid.Empty)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
    }
}

public sealed class TrialScenarioRun : BaseEntity<TrialScenarioRunId>
{
    private TrialScenarioRun()
    {
    }

    internal TrialScenarioRun(
        TrialCampaignId campaignId,
        string scenarioId,
        string trialMode,
        string sourceMode,
        string? boundary,
        AgentTaskId taskId,
        IReadOnlyCollection<Guid>? artifactIds,
        IReadOnlyCollection<string>? queryHashes,
        IReadOnlyCollection<string>? resultHashes,
        string approvalStatus,
        TrialScenarioRunStatus status,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        DateTimeOffset nowUtc)
    {
        Id = TrialScenarioRunId.New();
        CampaignId = campaignId;
        TaskId = taskId;
        CreatedAt = nowUtc;
        Update(
            scenarioId,
            trialMode,
            sourceMode,
            boundary,
            artifactIds,
            queryHashes,
            resultHashes,
            approvalStatus,
            status,
            startedAt,
            completedAt,
            nowUtc);
    }

    public TrialCampaignId CampaignId { get; private set; }

    public string ScenarioId { get; private set; } = string.Empty;

    public string TrialMode { get; private set; } = string.Empty;

    public string SourceMode { get; private set; } = string.Empty;

    public string? Boundary { get; private set; }

    public AgentTaskId TaskId { get; private set; }

    public Guid[] ArtifactIds { get; private set; } = [];

    public string[] QueryHashes { get; private set; } = [];

    public string[] ResultHashes { get; private set; } = [];

    public string ApprovalStatus { get; private set; } = "None";

    public TrialScenarioRunStatus Status { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal void Update(
        string scenarioId,
        string trialMode,
        string sourceMode,
        string? boundary,
        IReadOnlyCollection<Guid>? artifactIds,
        IReadOnlyCollection<string>? queryHashes,
        IReadOnlyCollection<string>? resultHashes,
        string approvalStatus,
        TrialScenarioRunStatus status,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        DateTimeOffset nowUtc)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Trial scenario run status is invalid.");
        }

        ScenarioId = TrialCampaign.NormalizeRequired(scenarioId, nameof(scenarioId), 160);
        TrialMode = TrialCampaign.NormalizeRequired(trialMode, nameof(trialMode), 80);
        SourceMode = TrialCampaign.NormalizeRequired(sourceMode, nameof(sourceMode), 80);
        Boundary = TrialCampaign.NormalizeOptional(boundary, 120);
        ArtifactIds = TrialCampaign.NormalizeGuids(artifactIds);
        QueryHashes = TrialCampaign.NormalizeStrings(queryHashes, 128);
        ResultHashes = TrialCampaign.NormalizeStrings(resultHashes, 128);
        ApprovalStatus = TrialCampaign.NormalizeRequired(approvalStatus, nameof(approvalStatus), 80);
        Status = status;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        UpdatedAt = nowUtc;
    }
}

public sealed class TrialRiskIssue : BaseEntity<TrialRiskIssueId>
{
    private TrialRiskIssue()
    {
    }

    internal TrialRiskIssue(
        TrialCampaignId campaignId,
        TrialRiskSeverity severity,
        string category,
        TrialRiskStatus status,
        string? owner,
        string? sourceRef,
        string? resolutionHash,
        DateTimeOffset nowUtc)
    {
        Id = TrialRiskIssueId.New();
        CampaignId = campaignId;
        CreatedAt = nowUtc;
        Update(severity, category, status, owner, sourceRef, resolutionHash, nowUtc);
    }

    public TrialCampaignId CampaignId { get; private set; }

    public TrialRiskSeverity Severity { get; private set; }

    public string Category { get; private set; } = string.Empty;

    public TrialRiskStatus Status { get; private set; }

    public string? Owner { get; private set; }

    public string? SourceRef { get; private set; }

    public string? ResolutionHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal void Update(
        TrialRiskSeverity severity,
        string category,
        TrialRiskStatus status,
        string? owner,
        string? sourceRef,
        string? resolutionHash,
        DateTimeOffset nowUtc)
    {
        Severity = severity;
        Category = TrialCampaign.NormalizeRequired(category, nameof(category), 120);
        Status = status;
        Owner = TrialCampaign.NormalizeOptional(owner, 120);
        SourceRef = TrialCampaign.NormalizeOptional(sourceRef, 240);
        ResolutionHash = TrialCampaign.NormalizeOptional(resolutionHash, 128);
        UpdatedAt = nowUtc;
    }
}
