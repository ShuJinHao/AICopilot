using AICopilot.HttpApi.Infrastructure;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Http;

namespace AICopilot.InProcessTests;

public sealed class UnhandledApiExceptionPolicyTests
{
    [Fact]
    public void TryCreate_ShouldReturnSanitizedServiceUnavailable_WhenCommitOutcomeIsUnknown()
    {
        var commitId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var created = UnhandledApiExceptionPolicy.TryCreate(
            new PersistenceCommitOutcomeUnknownException(
                commitId,
                new HttpRequestException("Host=prod;Password=secret")),
            out var decision);

        created.Should().BeTrue();
        decision.Should().NotBeNull();
        var actual = decision!;
        actual.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        actual.Problem.Code.Should().Be(AppProblemCodes.PersistenceCommitOutcomeUnknown);
        actual.Problem.Detail.Should().Be(
            "The write may have committed and must be reconciled before retrying.");
        actual.Problem.Extensions!["commitId"].Should().Be(commitId);
        actual.ExceptionType.Should().Be(nameof(PersistenceCommitOutcomeUnknownException));
        actual.InnerExceptionType.Should().Be(nameof(HttpRequestException));
        actual.Problem.ToString().Should().NotContain("Host=prod").And.NotContain("Password=secret");
    }

    [Fact]
    public void TryCreate_ShouldReturnSanitizedCatchAllWithoutRawExceptionMessage()
    {
        var created = UnhandledApiExceptionPolicy.TryCreate(new InvalidOperationException(
            "Provider endpoint http://model.internal.example failed with token=secret and SQL SELECT * FROM device_logs",
            new HttpRequestException("ConnectionString=Host=prod;Password=secret")),
            out var decision);

        created.Should().BeTrue();
        decision.Should().NotBeNull();
        var actual = decision!;
        actual.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        actual.Problem.Code.Should().Be(AppProblemCodes.InternalServerError);
        actual.Problem.Detail.Should().Be("Request failed unexpectedly. Contact support with the trace id.");
        actual.ExceptionType.Should().Be(nameof(InvalidOperationException));
        actual.InnerExceptionType.Should().Be(nameof(HttpRequestException));
        actual.Problem.Detail.Should().NotContain("model.internal.example")
            .And.NotContain("token=secret")
            .And.NotContain("SELECT")
            .And.NotContain("device_logs")
            .And.NotContain("ConnectionString")
            .And.NotContain("Password=secret");
    }

    [Fact]
    public void TryCreate_ShouldSanitizeInternalCancellationAsUnexpectedFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var created = UnhandledApiExceptionPolicy.TryCreate(
            new OperationCanceledException(cancellation.Token),
            out var decision);

        created.Should().BeTrue();
        decision.Should().NotBeNull();
        decision!.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        decision.Problem.Code.Should().Be(AppProblemCodes.InternalServerError);
        decision.ExceptionType.Should().Be(nameof(OperationCanceledException));
    }
}
