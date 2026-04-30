namespace AICopilot.EntityFrameworkCore.AuditLogs;

public class AuditLogEntry
{
    public Guid Id { get; set; }

    public string ActionGroup { get; set; } = null!;

    public string ActionCode { get; set; } = null!;

    public string TargetType { get; set; } = null!;

    public string? TargetId { get; set; }

    public string TargetName { get; set; } = null!;

    public string? OperatorUserId { get; set; }

    public string OperatorUserName { get; set; } = null!;

    public string? OperatorRoleName { get; set; }

    public string Result { get; set; } = null!;

    public string Summary { get; set; } = null!;

    public string[] ChangedFields { get; set; } = [];

    public DateTime CreatedAt { get; set; }
}
