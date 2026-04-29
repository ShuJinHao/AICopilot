namespace AICopilot.Services.Contracts;

public static class AuditActionGroups
{
    public const string Identity = "Identity";
    public const string Config = "Config";
    public const string Approval = "Approval";
    public const string DataAnalysis = "DataAnalysis";
}

public static class AuditResults
{
    public const string Succeeded = "Succeeded";
    public const string Rejected = "Rejected";
}

public sealed record AuditLogWriteRequest(
    string ActionGroup,
    string ActionCode,
    string TargetType,
    string? TargetId,
    string TargetName,
    string Result,
    string Summary,
    IReadOnlyCollection<string>? ChangedFields = null);

public interface IAuditLogWriter
{
    Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IIdentityAuditLogWriter
{
    Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default);
}

public sealed record AuditLogSummaryDto(
    Guid Id,
    string ActionGroup,
    string ActionCode,
    string TargetType,
    string? TargetId,
    string TargetName,
    string OperatorUserName,
    string? OperatorRoleName,
    string Result,
    string Summary,
    IReadOnlyCollection<string> ChangedFields,
    DateTime CreatedAt);

public sealed record AuditLogListDto(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyCollection<AuditLogSummaryDto> Items);

public interface IAuditLogQueryService
{
    Task<AuditLogListDto> GetListAsync(
        int page,
        int pageSize,
        string? actionGroup,
        string? actionCode,
        string? targetType,
        string? targetName,
        string? operatorUserName,
        string? result,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);
}
