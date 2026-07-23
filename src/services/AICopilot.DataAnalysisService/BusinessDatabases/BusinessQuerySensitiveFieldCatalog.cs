namespace AICopilot.DataAnalysisService.BusinessDatabases;

internal static class BusinessQuerySensitiveFieldCatalog
{
    private static readonly string[] DefaultSensitiveIdentifierFragments =
    [
        "api_key",
        "apikey",
        "connection_string",
        "credential",
        "password",
        "secret",
        "token"
    ];

    public static IReadOnlyList<string> GetAll(BusinessQuerySafetySchema? schema)
    {
        return DefaultSensitiveIdentifierFragments
            .Concat((IEnumerable<string>?)schema?.BlockedFieldFragments ?? [])
            .Concat((IEnumerable<string>?)schema?.SensitiveColumnFragments ?? [])
            .Where(fragment => !string.IsNullOrWhiteSpace(fragment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
