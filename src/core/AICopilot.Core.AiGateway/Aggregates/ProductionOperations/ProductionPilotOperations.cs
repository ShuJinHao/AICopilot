using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.ProductionOperations;

public sealed class ProductionPilotEmergencyStopState
    : BaseEntity<ProductionPilotEmergencyStopStateId>, IAggregateRoot<ProductionPilotEmergencyStopStateId>
{
    private ProductionPilotEmergencyStopState()
    {
    }

    private ProductionPilotEmergencyStopState(DateTimeOffset nowUtc)
    {
        Id = ProductionPilotEmergencyStopStateId.Default;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public bool IsActive { get; private set; }

    public string? Reason { get; private set; }

    public string? ActivatedBy { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public string? ClearedBy { get; private set; }

    public DateTimeOffset? ClearedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ProductionPilotEmergencyStopState CreateDefault(DateTimeOffset nowUtc) => new(nowUtc);

    public void Activate(string reason, string activatedBy, DateTimeOffset nowUtc)
    {
        IsActive = true;
        Reason = NormalizeRequired(reason, nameof(reason), 240);
        ActivatedBy = NormalizeRequired(activatedBy, nameof(activatedBy), 160);
        ActivatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public void Clear(string reason, string clearedBy, DateTimeOffset nowUtc)
    {
        IsActive = false;
        Reason = NormalizeRequired(reason, nameof(reason), 240);
        ClearedBy = NormalizeRequired(clearedBy, nameof(clearedBy), 160);
        ClearedAt = nowUtc;
        UpdatedAt = nowUtc;
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
        return normalized is { Length: > 0 } && normalized.Length > maxLength ? normalized[..maxLength] : normalized;
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

public sealed class ProductionPilotIncident
    : BaseEntity<ProductionPilotIncidentId>, IAggregateRoot<ProductionPilotIncidentId>
{
    private ProductionPilotIncident()
    {
    }

    public ProductionPilotIncident(
        ProductionPilotIncidentId? incidentId,
        string severity,
        string category,
        string status,
        string? owner,
        string? sourceRef,
        string? resolutionHash,
        DateTimeOffset nowUtc)
    {
        Id = incidentId.GetValueOrDefault();
        if (Id.Value == Guid.Empty)
        {
            Id = ProductionPilotIncidentId.New();
        }

        CreatedAt = nowUtc;
        Update(severity, category, status, owner, sourceRef, resolutionHash, nowUtc);
    }

    public string Severity { get; private set; } = "Medium";

    public string Category { get; private set; } = "Operations";

    public string Status { get; private set; } = "Open";

    public string? Owner { get; private set; }

    public string? SourceRef { get; private set; }

    public string? ResolutionHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        string severity,
        string category,
        string status,
        string? owner,
        string? sourceRef,
        string? resolutionHash,
        DateTimeOffset nowUtc)
    {
        Severity = NormalizeSeverity(severity);
        Category = ProductionPilotEmergencyStopState.NormalizeRequired(category, nameof(category), 120);
        Status = NormalizeStatus(status);
        Owner = ProductionPilotEmergencyStopState.NormalizeOptional(owner, 120);
        SourceRef = ProductionPilotEmergencyStopState.NormalizeOptional(sourceRef, 240);
        ResolutionHash = ProductionPilotEmergencyStopState.NormalizeOptional(resolutionHash, 128);
        UpdatedAt = nowUtc;
    }

    private static string NormalizeSeverity(string? severity)
    {
        var value = ProductionPilotEmergencyStopState.NormalizeRequired(severity, nameof(severity), 40);
        return new[] { "Low", "Medium", "High", "Critical" }
            .FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
            ?? "Medium";
    }

    private static string NormalizeStatus(string? status)
    {
        var value = ProductionPilotEmergencyStopState.NormalizeRequired(status, nameof(status), 40);
        return new[] { "Open", "Mitigating", "Resolved", "ClosedAsOutOfScope" }
            .FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
            ?? "Open";
    }
}

public sealed class ProductionPilotRunLedger
    : BaseEntity<ProductionPilotRunLedgerId>, IAggregateRoot<ProductionPilotRunLedgerId>
{
    private ProductionPilotRunLedger()
    {
    }

    public ProductionPilotRunLedger(
        string runId,
        Guid? taskId,
        string sourceMode,
        string boundary,
        string trialMode,
        string pilotWindowId,
        string? intentId,
        string endpointCode,
        IReadOnlyCollection<Guid>? artifactIds,
        string approvalStatus,
        string status,
        long durationMs,
        int rowCount,
        bool isTruncated,
        string queryHash,
        string resultHash,
        DateTimeOffset executedAt,
        DateTimeOffset nowUtc)
    {
        Id = ProductionPilotRunLedgerId.New();
        CreatedAt = nowUtc;
        Update(
            runId,
            taskId,
            sourceMode,
            boundary,
            trialMode,
            pilotWindowId,
            intentId,
            endpointCode,
            artifactIds,
            approvalStatus,
            status,
            durationMs,
            rowCount,
            isTruncated,
            queryHash,
            resultHash,
            executedAt,
            nowUtc);
    }

    public string RunId { get; private set; } = string.Empty;

    public Guid? TaskId { get; private set; }

    public string SourceMode { get; private set; } = string.Empty;

    public string Boundary { get; private set; } = string.Empty;

    public string TrialMode { get; private set; } = string.Empty;

    public string PilotWindowId { get; private set; } = string.Empty;

    public string? IntentId { get; private set; }

    public string EndpointCode { get; private set; } = string.Empty;

    public Guid[] ArtifactIds { get; private set; } = [];

    public string ApprovalStatus { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public long DurationMs { get; private set; }

    public int RowCount { get; private set; }

    public bool IsTruncated { get; private set; }

    public string QueryHash { get; private set; } = string.Empty;

    public string ResultHash { get; private set; } = string.Empty;

    public DateTimeOffset ExecutedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        string runId,
        Guid? taskId,
        string sourceMode,
        string boundary,
        string trialMode,
        string pilotWindowId,
        string? intentId,
        string endpointCode,
        IReadOnlyCollection<Guid>? artifactIds,
        string approvalStatus,
        string status,
        long durationMs,
        int rowCount,
        bool isTruncated,
        string queryHash,
        string resultHash,
        DateTimeOffset executedAt,
        DateTimeOffset nowUtc)
    {
        RunId = ProductionPilotEmergencyStopState.NormalizeRequired(runId, nameof(runId), 200);
        TaskId = taskId == Guid.Empty ? null : taskId;
        SourceMode = ProductionPilotEmergencyStopState.NormalizeRequired(sourceMode, nameof(sourceMode), 80);
        Boundary = ProductionPilotEmergencyStopState.NormalizeRequired(boundary, nameof(boundary), 120);
        TrialMode = ProductionPilotEmergencyStopState.NormalizeRequired(trialMode, nameof(trialMode), 120);
        PilotWindowId = ProductionPilotEmergencyStopState.NormalizeRequired(pilotWindowId, nameof(pilotWindowId), 160);
        IntentId = ProductionPilotEmergencyStopState.NormalizeOptional(intentId, 200);
        EndpointCode = ProductionPilotEmergencyStopState.NormalizeRequired(endpointCode, nameof(endpointCode), 120);
        ArtifactIds = ProductionPilotEmergencyStopState.NormalizeGuids(artifactIds);
        ApprovalStatus = ProductionPilotEmergencyStopState.NormalizeRequired(approvalStatus, nameof(approvalStatus), 80);
        Status = ProductionPilotEmergencyStopState.NormalizeRequired(status, nameof(status), 80);
        DurationMs = Math.Max(0, durationMs);
        RowCount = Math.Max(0, rowCount);
        IsTruncated = isTruncated;
        QueryHash = ProductionPilotEmergencyStopState.NormalizeRequired(queryHash, nameof(queryHash), 128);
        ResultHash = ProductionPilotEmergencyStopState.NormalizeRequired(resultHash, nameof(resultHash), 128);
        ExecutedAt = executedAt;
        UpdatedAt = nowUtc;
    }
}

public sealed class ProductionPilotGaReadinessAssessment
    : BaseEntity<ProductionPilotGaReadinessAssessmentId>, IAggregateRoot<ProductionPilotGaReadinessAssessmentId>
{
    private ProductionPilotGaReadinessAssessment()
    {
    }

    public ProductionPilotGaReadinessAssessment(
        string status,
        string checksJson,
        IReadOnlyCollection<string>? blockers,
        IReadOnlyCollection<string>? warnings,
        int totalRuns,
        int succeededRuns,
        int failedRuns,
        int rejectedRuns,
        int timeoutRuns,
        int truncatedRuns,
        int totalRows,
        int finalArtifactCount,
        int openIncidentCount,
        string endpointDistributionJson,
        DateTimeOffset generatedAt,
        DateTimeOffset nowUtc)
    {
        Id = ProductionPilotGaReadinessAssessmentId.New();
        Status = ProductionPilotEmergencyStopState.NormalizeRequired(status, nameof(status), 80);
        ChecksJson = ProductionPilotEmergencyStopState.NormalizeRequired(checksJson, nameof(checksJson), 8000);
        Blockers = ProductionPilotEmergencyStopState.NormalizeStrings(blockers, 500);
        Warnings = ProductionPilotEmergencyStopState.NormalizeStrings(warnings, 500);
        TotalRuns = Math.Max(0, totalRuns);
        SucceededRuns = Math.Max(0, succeededRuns);
        FailedRuns = Math.Max(0, failedRuns);
        RejectedRuns = Math.Max(0, rejectedRuns);
        TimeoutRuns = Math.Max(0, timeoutRuns);
        TruncatedRuns = Math.Max(0, truncatedRuns);
        TotalRows = Math.Max(0, totalRows);
        FinalArtifactCount = Math.Max(0, finalArtifactCount);
        OpenIncidentCount = Math.Max(0, openIncidentCount);
        EndpointDistributionJson = ProductionPilotEmergencyStopState.NormalizeRequired(endpointDistributionJson, nameof(endpointDistributionJson), 8000);
        GeneratedAt = generatedAt;
        CreatedAt = nowUtc;
    }

    public string Status { get; private set; } = string.Empty;

    public string ChecksJson { get; private set; } = "[]";

    public string[] Blockers { get; private set; } = [];

    public string[] Warnings { get; private set; } = [];

    public int TotalRuns { get; private set; }

    public int SucceededRuns { get; private set; }

    public int FailedRuns { get; private set; }

    public int RejectedRuns { get; private set; }

    public int TimeoutRuns { get; private set; }

    public int TruncatedRuns { get; private set; }

    public int TotalRows { get; private set; }

    public int FinalArtifactCount { get; private set; }

    public int OpenIncidentCount { get; private set; }

    public string EndpointDistributionJson { get; private set; } = "{}";

    public DateTimeOffset GeneratedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
