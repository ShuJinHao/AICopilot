namespace AICopilot.Services.Contracts;

public sealed record BusinessTextToSqlGenerationRequest(
    string Question,
    int Limit,
    BusinessDataSourceProfile SourceProfile,
    IReadOnlyList<CloudReadOnlyTextToSqlRepairAttemptRecord> RepairHistory)
{
    public string? PreviousSqlForRepair { get; init; }
}

public sealed record BusinessTextToSqlGenerationResult(
    bool IsSuccess,
    string? Sql,
    IReadOnlyDictionary<string, object?> Parameters,
    string Explanation,
    IReadOnlyList<string> Warnings,
    string? FailureReason)
{
    public static BusinessTextToSqlGenerationResult Success(
        string sql,
        string explanation,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyList<string>? warnings = null)
    {
        return new BusinessTextToSqlGenerationResult(
            true,
            sql,
            parameters ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            explanation,
            warnings ?? [],
            null);
    }

    public static BusinessTextToSqlGenerationResult Failure(string failureReason)
    {
        return new BusinessTextToSqlGenerationResult(
            false,
            null,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            string.Empty,
            [],
            failureReason);
    }
}

public interface IBusinessTextToSqlGenerator
{
    Task<BusinessTextToSqlGenerationResult> GenerateAsync(
        BusinessTextToSqlGenerationRequest request,
        CancellationToken cancellationToken = default);
}
