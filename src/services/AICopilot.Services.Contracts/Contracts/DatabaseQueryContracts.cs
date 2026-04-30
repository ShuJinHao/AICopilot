namespace AICopilot.Services.Contracts;

public sealed record DatabaseQueryOptions(
    int MaxRows = 200,
    int CommandTimeoutSeconds = 15);

public sealed record DatabaseQueryResult(
    IReadOnlyList<Dictionary<string, object?>> Rows,
    int ReturnedRowCount,
    bool IsTruncated,
    long ElapsedMilliseconds);
