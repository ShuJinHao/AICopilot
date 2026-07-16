using System.Security.Claims;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.SharedKernel.Result;

namespace AICopilot.InProcessTests;

public sealed class CloudOidcFinalizationWorkflowTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnAiJwtAndClearExternalSession()
    {
        var signOutCount = 0;

        var result = await CloudOidcFinalizationWorkflow.ExecuteAsync(
            _ => Task.FromResult<ClaimsPrincipal?>(CreateCloudPrincipal()),
            "https://cloud.example.com",
            (profile, _) => Task.FromResult(Result.Success(new TestLoginResult(profile.EmployeeNo!, "ai-token"))),
            _ =>
            {
                signOutCount++;
                return Task.CompletedTask;
            });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new TestLoginResult("E0001", "ai-token"));
        signOutCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInvalidPrincipalAndClearExternalSession()
    {
        var signOutCount = 0;

        var result = await CloudOidcFinalizationWorkflow.ExecuteAsync<TestLoginResult>(
            _ => Task.FromResult<ClaimsPrincipal?>(null),
            "https://cloud.example.com",
            (_, _) => throw new InvalidOperationException("finalize must not run"),
            _ =>
            {
                signOutCount++;
                return Task.CompletedTask;
            });

        result.Status.Should().Be(ResultStatus.Unauthorized);
        signOutCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPreserveFinalizeFailureAndClearExternalSession()
    {
        var signOutCount = 0;

        var result = await CloudOidcFinalizationWorkflow.ExecuteAsync(
            _ => Task.FromResult<ClaimsPrincipal?>(CreateCloudPrincipal()),
            "https://cloud.example.com",
            (_, _) => Task.FromResult<Result<TestLoginResult>>(Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.ExternalIdentityConflict,
                "binding conflict"))),
            _ =>
            {
                signOutCount++;
                return Task.CompletedTask;
            });

        result.Status.Should().Be(ResultStatus.Unauthorized);
        signOutCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldClearExternalSession_WhenFinalizeThrows()
    {
        var signOutCount = 0;

        var action = () => CloudOidcFinalizationWorkflow.ExecuteAsync<TestLoginResult>(
            _ => Task.FromResult<ClaimsPrincipal?>(CreateCloudPrincipal()),
            "https://cloud.example.com",
            (_, _) => throw new InvalidOperationException("finalize failed"),
            _ =>
            {
                signOutCount++;
                return Task.CompletedTask;
            });

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("finalize failed");
        signOutCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPreserveCallerCancellation_WhenNonCancelableSignOutFails()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var primary = new OperationCanceledException(cancellation.Token);
        var signOutCount = 0;
        var signOutToken = new CancellationToken(canceled: true);

        var action = () => CloudOidcFinalizationWorkflow.ExecuteAsync<TestLoginResult>(
            _ => Task.FromException<ClaimsPrincipal?>(primary),
            "https://cloud.example.com",
            (_, _) => throw new InvalidOperationException("finalize must not run"),
            token =>
            {
                signOutCount++;
                signOutToken = token;
                throw new InvalidOperationException("sign-out failed");
            },
            cancellation.Token);

        var assertion = await action.Should().ThrowAsync<OperationCanceledException>();
        assertion.Which.Should().BeSameAs(primary);
        primary.Data[CloudOidcFinalizationWorkflow.SignOutFailureDataKey]
            .Should().Be(nameof(InvalidOperationException));
        signOutCount.Should().Be(1);
        signOutToken.CanBeCanceled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPreserveFinalizeException_WhenSignOutAlsoFails()
    {
        var primary = new InvalidOperationException("finalize failed");
        var signOutCount = 0;

        var action = () => CloudOidcFinalizationWorkflow.ExecuteAsync<TestLoginResult>(
            _ => Task.FromResult<ClaimsPrincipal?>(CreateCloudPrincipal()),
            "https://cloud.example.com",
            (_, _) => Task.FromException<Result<TestLoginResult>>(primary),
            token =>
            {
                token.CanBeCanceled.Should().BeFalse();
                signOutCount++;
                throw new ApplicationException("sign-out failed");
            });

        var assertion = await action.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(primary);
        primary.Data[CloudOidcFinalizationWorkflow.SignOutFailureDataKey]
            .Should().Be(nameof(ApplicationException));
        signOutCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPropagateSignOutFailure_WhenPrimaryFlowSucceeds()
    {
        var cleanupFailure = new InvalidOperationException("sign-out failed");
        var signOutCount = 0;

        var action = () => CloudOidcFinalizationWorkflow.ExecuteAsync(
            _ => Task.FromResult<ClaimsPrincipal?>(CreateCloudPrincipal()),
            "https://cloud.example.com",
            (profile, _) => Task.FromResult(Result.Success(new TestLoginResult(profile.EmployeeNo!, "ai-token"))),
            token =>
            {
                token.CanBeCanceled.Should().BeFalse();
                signOutCount++;
                return Task.FromException(cleanupFailure);
            });

        var assertion = await action.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(cleanupFailure);
        signOutCount.Should().Be(1);
    }

    private static ClaimsPrincipal CreateCloudPrincipal()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "cloud-user-1"),
                new Claim("preferred_username", "E0001"),
                new Claim("name", "E0001"),
                new Claim("tenant_id", CloudOidcIdentityProfile.DefaultTenantId),
                new Claim("employee_id", "employee-1"),
                new Claim("employee_no", "E0001"),
                new Claim("account_enabled", "true"),
                new Claim("employee_active", "true"),
                new Claim("status_version", "v1")
            ],
            "oidc"));
    }

    private sealed record TestLoginResult(string UserName, string Token);
}
