namespace AICopilot.Services.Contracts;

public sealed record CloudReadOnlyTextToSqlGenerationRequest(
    string Question,
    int Limit,
    IReadOnlySet<string> AllowedTables,
    IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedColumns,
    IReadOnlyList<CloudReadOnlyTextToSqlRepairAttemptRecord> RepairHistory)
{
    public string? PreviousSqlForRepair { get; init; }
}

public sealed record CloudReadOnlyTextToSqlGenerationResult(
    bool IsSuccess,
    string? Sql,
    IReadOnlyDictionary<string, object?> Parameters,
    string Explanation,
    IReadOnlyList<string> Warnings,
    string? FailureReason)
{
    public static CloudReadOnlyTextToSqlGenerationResult Success(
        string sql,
        string explanation,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyList<string>? warnings = null)
    {
        return new CloudReadOnlyTextToSqlGenerationResult(
            true,
            sql,
            parameters ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            explanation,
            warnings ?? [],
            null);
    }

    public static CloudReadOnlyTextToSqlGenerationResult Failure(string failureReason)
    {
        return new CloudReadOnlyTextToSqlGenerationResult(
            false,
            null,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            string.Empty,
            [],
            failureReason);
    }
}

public interface ICloudReadOnlyTextToSqlGenerator
{
    Task<CloudReadOnlyTextToSqlGenerationResult> GenerateAsync(
        CloudReadOnlyTextToSqlGenerationRequest request,
        CancellationToken cancellationToken = default);
}
