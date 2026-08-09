using System.Security.Claims;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.HttpApi.Infrastructure;

internal sealed record CloudOidcExternalSession(
    ClaimsPrincipal Principal,
    CloudDelegationTokenInput DelegationToken);

internal static class CloudOidcFinalizationWorkflow
{
    public const string SignOutFailureDataKey = "AICopilot.CloudOidc.SignOutFailureType";

    public static async Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<CloudOidcExternalSession?>> authenticate,
        string issuer,
        Func<CloudOidcIdentityProfile, CloudDelegationTokenInput, CancellationToken, Task<Result<T>>> finalize,
        Func<CancellationToken, Task> signOut,
        CancellationToken cancellationToken = default,
        Func<Result<T>, bool>? retainExternalSession = null)
    {
        ArgumentNullException.ThrowIfNull(authenticate);
        ArgumentNullException.ThrowIfNull(finalize);
        ArgumentNullException.ThrowIfNull(signOut);

        Exception? primaryException = null;
        var shouldSignOut = true;
        try
        {
            var session = await authenticate(cancellationToken);
            if (session is null ||
                string.IsNullOrWhiteSpace(session.DelegationToken.AccessToken) ||
                session.DelegationToken.ExpiresAtUtc <= DateTime.UtcNow)
            {
                return Result.Unauthorized(new ApiProblemDescriptor(
                    AuthProblemCodes.CloudOidcInvalidPrincipal,
                    "Cloud 登录态无效或已过期，请重新从 Cloud 登录。"));
            }

            if (!CloudOidcPrincipalMapper.TryMap(
                    session.Principal,
                    issuer,
                    out var profile,
                    out var problem))
            {
                return Result.Unauthorized(problem!);
            }

            var result = await finalize(
                profile!,
                session.DelegationToken,
                cancellationToken);
            shouldSignOut = retainExternalSession?.Invoke(result) != true;
            return result;
        }
        catch (Exception exception)
        {
            primaryException = exception;
            throw;
        }
        finally
        {
            if (shouldSignOut)
            {
                try
                {
                    await signOut(CancellationToken.None);
                }
                catch (Exception signOutException) when (primaryException is not null)
                {
                    RecordSignOutFailure(primaryException, signOutException);
                }
            }
        }
    }

    private static void RecordSignOutFailure(Exception primaryException, Exception signOutException)
    {
        try
        {
            primaryException.Data[SignOutFailureDataKey] = signOutException.GetType().Name;
        }
        catch
        {
            // Diagnostic attachment must never replace the primary business failure or caller cancellation.
        }
    }
}
