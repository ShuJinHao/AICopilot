using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.ProductionOperations;

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
