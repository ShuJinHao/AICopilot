namespace AICopilot.MigrationWorkApp;

public static class MigrationWorkAppProcess
{
    public const string SuccessMarker = "aicopilot_migration_result=success";

    public static async Task<int> RunAsync(
        Func<CancellationToken, Task> runMigrationAsync,
        string invocationId,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runMigrationAsync);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (string.IsNullOrWhiteSpace(invocationId) ||
            invocationId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':')))
        {
            await standardError.WriteLineAsync(
                "aicopilot_migration_result=failure reason=invalid-invocation-id");
            return 2;
        }

        try
        {
            await runMigrationAsync(cancellationToken);
            await standardOutput.WriteLineAsync($"{SuccessMarker} invocation_id={invocationId}");
            await standardOutput.FlushAsync(cancellationToken);
            return 0;
        }
        catch (Exception exception)
        {
            await standardError.WriteLineAsync(
                $"aicopilot_migration_result=failure exception={exception.GetType().Name}");
            await standardError.FlushAsync(cancellationToken);
            return 1;
        }
    }
}
