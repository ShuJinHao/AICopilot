namespace AICopilot.Services.Contracts;

public static class BusinessDatabaseConnectionPolicy
{
    public static string RequireConnectionString(BusinessDatabaseConnectionInfo database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (string.IsNullOrWhiteSpace(database.ConnectionString))
        {
            throw new ArgumentException(
                $"Connection string is required for data source '{database.Name}'.",
                nameof(database));
        }

        return database.ConnectionString;
    }

    public static void EnsureQueryable(BusinessDatabaseConnectionInfo database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (!database.IsEnabled)
        {
            throw new InvalidOperationException($"Data source '{database.Name}' is disabled (已被禁用).");
        }

        if (!database.IsReadOnly)
        {
            throw new InvalidOperationException($"Data source '{database.Name}' is not configured as read-only (只读模式).");
        }

        if (database.ExternalSystemType == DataSourceExternalSystemType.CloudReadOnly &&
            !database.ReadOnlyCredentialVerified)
        {
            throw new InvalidOperationException(
                $"Data source '{database.Name}' targets Cloud read-only data but its database account has not been verified as read-only.");
        }

        if (database.Provider != DatabaseProviderType.PostgreSql &&
            !database.ReadOnlyCredentialVerified)
        {
            throw new InvalidOperationException(
                $"Data source '{database.Name}' uses {database.Provider}; provider-specific read-only session enforcement is not available, so a verified read-only database account is required.");
        }
    }
}
