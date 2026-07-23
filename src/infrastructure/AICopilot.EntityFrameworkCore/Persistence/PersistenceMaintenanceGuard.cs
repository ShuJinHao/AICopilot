namespace AICopilot.EntityFrameworkCore.Persistence;

internal static class PersistenceMaintenanceGuard
{
    public static void RequirePositiveDelay(TimeSpan reconciliationDelay)
    {
        if (reconciliationDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reconciliationDelay));
        }
    }

    public static void RequirePositiveBatchSize(int batchSize)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }
    }
}
