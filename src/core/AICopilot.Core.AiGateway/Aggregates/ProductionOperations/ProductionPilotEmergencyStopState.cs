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
