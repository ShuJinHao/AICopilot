using AICopilot.Services.Contracts;

namespace AICopilot.MigrationWorkApp;

public class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";
    public const string CanonicalCloudAdminEmployeeNo = "101650";
    public const string CanonicalConflictReasonCode =
        CloudOidcCanonicalAdminOptions.EmergencyAdminConflictReasonCode;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public void EnsureSeparatedFromCanonicalCloudAdmin()
    {
        var normalizedUserName = UserName?.Trim().ToUpperInvariant();
        if (string.Equals(
                normalizedUserName,
                CanonicalCloudAdminEmployeeNo,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{CanonicalConflictReasonCode}: BootstrapAdmin user name must not equal the canonical Cloud OIDC employee number after normalization.");
        }
    }
}
