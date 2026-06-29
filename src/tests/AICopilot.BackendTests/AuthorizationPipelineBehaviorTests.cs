using AICopilot.Services.Contracts;
using AICopilot.AiGatewayService.Agents;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.CrossCutting.Behaviors;
using AICopilot.Services.CrossCutting.Exceptions;
using AICopilot.SharedKernel.Result;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.BackendTests;

public sealed class AuthorizationPipelineBehaviorTests
{
    [Fact]
    public async Task ChatStreamRequestValidator_ShouldRejectEmptyMessage()
    {
        var validator = new ChatStreamRequestValidator();

        var problem = await validator.ValidateAsync(
            new ChatStreamRequest(Guid.NewGuid(), " "),
            CancellationToken.None);

        problem.Should().NotBeNull();
        problem!.Code.Should().Be(AppProblemCodes.RequestValidationFailed);
        problem.Detail.Should().Be("Message is required.");
    }

    [Fact]
    public async Task ValidationStream_ShouldRejectBeforeCallingNext()
    {
        var behavior = new ValidationStreamBehavior<ValidatedStreamRequest, int>(
            [new ValidatedStreamRequestValidator()]);
        var nextCalled = false;

        var act = async () => await DrainAsync(behavior.Handle(
            new ValidatedStreamRequest(""),
            Next,
            CancellationToken.None));

        var exception = await act.Should().ThrowAsync<RequestValidationException>();
        exception.Which.Problem.Code.Should().Be(AppProblemCodes.RequestValidationFailed);
        exception.Which.Problem.Detail.Should().Be("Value is required.");
        nextCalled.Should().BeFalse("validation must fail before entering the streaming handler");

        async IAsyncEnumerable<int> Next()
        {
            nextCalled = true;
            yield return 1;
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TelemetryStream_ShouldForwardItemsWithoutBufferingTheStream()
    {
        var behavior = new TelemetryStreamBehavior<PublicStreamRequest, int>(
            NullLogger<TelemetryStreamBehavior<PublicStreamRequest, int>>.Instance);
        var releaseSecondItem = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var enumerator = behavior
            .Handle(new PublicStreamRequest(), Next, CancellationToken.None)
            .GetAsyncEnumerator();

        (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        enumerator.Current.Should().Be(1);

        releaseSecondItem.SetResult();
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        enumerator.Current.Should().Be(2);

        async IAsyncEnumerable<int> Next()
        {
            yield return 1;
            await releaseSecondItem.Task;
            yield return 2;
        }
    }

    [Fact]
    public async Task StreamAuthorization_ShouldRejectMissingPermissionBeforeCallingNext()
    {
        var userId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var identityAccess = new FakeIdentityAccessService(
            new CurrentUserAccess(userId, "operator", "User", []));
        var services = new ServiceCollection()
            .AddSingleton<ICurrentUser>(new TestCurrentUser(userId))
            .AddSingleton<IIdentityAccessService>(identityAccess)
            .BuildServiceProvider();
        var behavior = new AuthorizationStreamBehavior<ProtectedStreamRequest, int>(
            new AuthorizationRequirementEvaluator(services));
        var nextCalled = false;

        var act = async () => await DrainAsync(behavior.Handle(
            new ProtectedStreamRequest(),
            Next,
            CancellationToken.None));

        var exception = await act.Should().ThrowAsync<ForbiddenException>();
        exception.Which.Problem.Code.Should().Be(AuthProblemCodes.MissingPermission);
        exception.Which.Problem.Extensions![ApiProblemExtensionKeys.MissingPermissions]
            .Should()
            .BeEquivalentTo(new[] { "Protected.Stream" });
        nextCalled.Should().BeFalse("stream authorization must fail before entering the streaming handler");

        async IAsyncEnumerable<int> Next()
        {
            nextCalled = true;
            yield return 1;
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task StreamAuthorization_ShouldForwardItemsWithoutBufferingTheStream()
    {
        var userId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var identityAccess = new FakeIdentityAccessService(
            new CurrentUserAccess(userId, "operator", "User", ["Protected.Stream"]));
        var services = new ServiceCollection()
            .AddSingleton<ICurrentUser>(new TestCurrentUser(userId))
            .AddSingleton<IIdentityAccessService>(identityAccess)
            .BuildServiceProvider();
        var behavior = new AuthorizationStreamBehavior<ProtectedStreamRequest, int>(
            new AuthorizationRequirementEvaluator(services));
        var releaseSecondItem = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var enumerator = behavior
            .Handle(new ProtectedStreamRequest(), Next, CancellationToken.None)
            .GetAsyncEnumerator();

        (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        enumerator.Current.Should().Be(1);

        releaseSecondItem.SetResult();
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        enumerator.Current.Should().Be(2);
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2))).Should().BeFalse();

        async IAsyncEnumerable<int> Next()
        {
            yield return 1;
            await releaseSecondItem.Task;
            yield return 2;
        }
    }

    [Fact]
    public async Task StreamAuthorization_ShouldPassThroughPublicRequestsWithoutUserServices()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var behavior = new AuthorizationStreamBehavior<PublicStreamRequest, int>(
            new AuthorizationRequirementEvaluator(services));

        var items = await CollectAsync(behavior.Handle(
            new PublicStreamRequest(),
            Next,
            CancellationToken.None));

        items.Should().Equal(7, 8);

        static async IAsyncEnumerable<int> Next()
        {
            yield return 7;
            await Task.Yield();
            yield return 8;
        }
    }

    private static async Task DrainAsync<T>(IAsyncEnumerable<T> items)
    {
        await foreach (var _ in items)
        {
        }
    }

    private static async Task<IReadOnlyList<T>> CollectAsync<T>(IAsyncEnumerable<T> items)
    {
        var result = new List<T>();
        await foreach (var item in items)
        {
            result.Add(item);
        }

        return result;
    }

    [AuthorizeRequirement("Protected.Stream")]
    private sealed record ProtectedStreamRequest : IStreamRequest<int>;

    private sealed record PublicStreamRequest : IStreamRequest<int>;

    private sealed record ValidatedStreamRequest(string Value) : IStreamRequest<int>;

    private sealed class ValidatedStreamRequestValidator : IRequestValidator<ValidatedStreamRequest>
    {
        public ValueTask<ApiProblemDescriptor?> ValidateAsync(
            ValidatedStreamRequest request,
            CancellationToken cancellationToken)
        {
            return string.IsNullOrWhiteSpace(request.Value)
                ? ValueTask.FromResult<ApiProblemDescriptor?>(RequestValidation.Failed("Value is required."))
                : ValueTask.FromResult<ApiProblemDescriptor?>(null);
        }
    }

    private sealed class FakeIdentityAccessService(CurrentUserAccess? currentUserAccess) : IIdentityAccessService
    {
        public Task<CurrentUserAccess?> GetCurrentUserAccessAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(currentUserAccess);
        }

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(
            string roleName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<string>>([]);
        }

        public Task SyncRolePermissionsAsync(
            string roleName,
            IEnumerable<string> permissionCodes,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
