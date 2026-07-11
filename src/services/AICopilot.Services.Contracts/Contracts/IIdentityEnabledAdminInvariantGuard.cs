namespace AICopilot.Services.Contracts;

public interface IIdentityEnabledAdminInvariantGuard
{
    Task AcquireAsync(CancellationToken cancellationToken = default);
}
