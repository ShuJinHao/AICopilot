using System.Security.Claims;
using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.Contracts.Authentication;

public static class CloudOidcFinalizationWorkflow
{
    public const string SignOutFailureDataKey = "AICopilot.CloudOidc.SignOutFailureType";

    public static async Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<ClaimsPrincipal?>> authenticate,
        string issuer,
        Func<CloudOidcIdentityProfile, CancellationToken, Task<Result<T>>> finalize,
        Func<CancellationToken, Task> signOut,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticate);
        ArgumentNullException.ThrowIfNull(finalize);
        ArgumentNullException.ThrowIfNull(signOut);

        Exception? primaryException = null;
        try
        {
            var principal = await authenticate(cancellationToken);
            if (principal is null)
            {
                return Result.Unauthorized(new ApiProblemDescriptor(
                    AuthProblemCodes.CloudOidcInvalidPrincipal,
                    "Cloud 登录态无效或已过期，请重新从 Cloud 登录。"));
            }

            if (!CloudOidcPrincipalMapper.TryMap(principal, issuer, out var profile, out var problem))
            {
                return Result.Unauthorized(problem!);
            }

            return await finalize(profile!, cancellationToken);
        }
        catch (Exception exception)
        {
            primaryException = exception;
            throw;
        }
        finally
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
