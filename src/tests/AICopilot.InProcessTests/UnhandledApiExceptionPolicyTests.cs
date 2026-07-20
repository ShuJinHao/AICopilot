using System.Text.Json;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
    public void TryCreate_ShouldPreservePlanPayloadTooLargeAsStableClientProblem()
    {
        var cases = new[]
        {
            (AppProblemCodes.AgentPlanInvalid, "Agent task plan failed integrity validation."),
            (AppProblemCodes.AgentPlanSchemaInvalid, "Agent task plan does not match the required schema."),
            (AppProblemCodes.PlanPayloadTooLarge, "Agent task plan exceeds the maximum allowed size of 262144 UTF-8 bytes.")
        };

        foreach (var (code, expectedDetail) in cases)
        {
            var taskId = Guid.NewGuid();
            var created = UnhandledApiExceptionPolicy.TryCreate(
                new AgentTaskPlanPersistenceIntegrityException(
                    taskId,
                    code,
                    "opaque-plan-secret-9f4c raw owner=node-controlled SELECT payroll"),
                out var decision);

            created.Should().BeTrue();
            decision!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            decision.Problem.Code.Should().Be(code);
            decision.Problem.Detail.Should().Be(expectedDetail);
            decision.Problem.ToString().Should().NotContain("opaque-plan-secret-9f4c")
                .And.NotContain("node-controlled")
                .And.NotContain("payroll");
            decision.Problem.Extensions!["taskId"].Should().Be(taskId);

            var wrappedTaskId = Guid.NewGuid();
            var wrappedCreated = UnhandledApiExceptionPolicy.TryCreate(
                new AggregateException(
                    new AgentTaskPlanPersistenceIntegrityException(
                        Guid.NewGuid(),
                        "agent_plan_internal_roundtrip_failed",
                        "first-inner-opaque-rest-secret-a1b2"),
                    new AgentTaskPlanPersistenceIntegrityException(
                        wrappedTaskId,
                        code,
                        "non-first-plan-opaque-rest-secret-c3d4 raw owner=aggregate-controlled")),
                out var wrappedDecision);

            wrappedCreated.Should().BeTrue();
            wrappedDecision!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            wrappedDecision.Problem.Code.Should().Be(code);
            wrappedDecision.Problem.Detail.Should().Be(expectedDetail);
            wrappedDecision.Problem.Extensions!["taskId"].Should().Be(wrappedTaskId);
            var wrappedDetails = ApiProblemDetailsFactory.Create(
                wrappedDecision.StatusCode,
                wrappedDecision.Problem);
            wrappedDetails.Extensions[ApiProblemExtensionKeys.UserFacingMessage]
                .Should().Be(AgentPlanPublicFailureDisclosurePolicy.Resolve(code)!.UserFacingMessage);
            wrappedDetails.Extensions["taskId"].Should().Be(wrappedTaskId);
            JsonSerializer.Serialize(wrappedDetails).Should()
                .NotContain("first-inner-opaque-rest-secret-a1b2")
                .And.NotContain("non-first-plan-opaque-rest-secret-c3d4")
                .And.NotContain("aggregate-controlled");

            var invalidExtensionProblem = new ApiProblemDescriptor(
                code,
                "opaque-result-detail-12ab raw plan owner=result-controlled",
                new Dictionary<string, object?>
                {
                    [ApiProblemExtensionKeys.UserFacingMessage] = "opaque-result-user-message-34cd",
                    ["taskId"] = "opaque-string-task-id-56ef",
                    ["opaquePlanMetadata"] = "opaque-extension-secret-78ab"
                });
            var invalidExtensionDetails = ApiProblemDetailsFactory.Create(
                StatusCodes.Status400BadRequest,
                invalidExtensionProblem,
                traceIdentifier: "trace-plan-fixed-disclosure");

            invalidExtensionDetails.Detail.Should().Be(expectedDetail);
            invalidExtensionDetails.Extensions[ApiProblemExtensionKeys.Code].Should().Be(code);
            invalidExtensionDetails.Extensions[ApiProblemExtensionKeys.TraceId]
                .Should().Be("trace-plan-fixed-disclosure");
            invalidExtensionDetails.Extensions[ApiProblemExtensionKeys.UserFacingMessage]
                .Should().Be(AgentPlanPublicFailureDisclosurePolicy.Resolve(code)!.UserFacingMessage);
            invalidExtensionDetails.Extensions.Should().NotContainKey("taskId")
                .And.NotContainKey("opaquePlanMetadata");
            JsonSerializer.Serialize(invalidExtensionDetails).Should()
                .NotContain("opaque-result-detail-12ab")
                .And.NotContain("result-controlled")
                .And.NotContain("opaque-result-user-message-34cd")
                .And.NotContain("opaque-string-task-id-56ef")
                .And.NotContain("opaque-extension-secret-78ab");

            var validTaskId = Guid.NewGuid();
            var validTaskIdDetails = ApiProblemDetailsFactory.Create(
                StatusCodes.Status400BadRequest,
                new ApiProblemDescriptor(
                    code,
                    "opaque-second-result-detail-90ab",
                    new Dictionary<string, object?>
                    {
                        ["taskId"] = validTaskId,
                        ["opaquePlanMetadata"] = "opaque-second-extension-secret-cdef"
                    }));
            validTaskIdDetails.Extensions["taskId"].Should().Be(validTaskId);
            validTaskIdDetails.Extensions.Should().NotContainKey("opaquePlanMetadata");
            JsonSerializer.Serialize(validTaskIdDetails).Should()
                .NotContain("opaque-second-result-detail-90ab")
                .And.NotContain("opaque-second-extension-secret-cdef");

            var resultTaskId = Guid.NewGuid();
            var resultController = new ResultProbeController
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        TraceIdentifier = "trace-multi-error-plan-disclosure"
                    }
                }
            };
            var resultResponse = resultController.ReturnResult(Result.Failure(
                    new ApiProblemDescriptor(
                        "agent_plan_internal_roundtrip_failed",
                        "first-rest-result-secret-1122 raw owner=internal-rest-result-controlled"),
                    new ApiProblemDescriptor(
                        code,
                        "second-rest-plan-secret-3344 raw owner=public-plan-result-controlled",
                        new Dictionary<string, object?>
                        {
                            ["taskId"] = resultTaskId,
                            [ApiProblemExtensionKeys.UserFacingMessage] = "malicious-result-user-message",
                            ["opaquePlanMetadata"] = "opaque-result-extension-secret"
                        })))
                .Should().BeOfType<ObjectResult>().Subject;
            resultResponse.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            var resultDetails = resultResponse.Value.Should().BeOfType<ProblemDetails>().Subject;
            var serializedResultDetails = JsonSerializer.Serialize(resultDetails);
            serializedResultDetails.Should()
                .NotContain("first-rest-result-secret-1122")
                .And.NotContain("internal-rest-result-controlled")
                .And.NotContain("second-rest-plan-secret-3344")
                .And.NotContain("public-plan-result-controlled")
                .And.NotContain("malicious-result-user-message")
                .And.NotContain("opaque-result-extension-secret");
            resultDetails.Detail.Should().Be(expectedDetail);
            resultDetails.Extensions[ApiProblemExtensionKeys.Code].Should().Be(code);
            resultDetails.Extensions[ApiProblemExtensionKeys.UserFacingMessage]
                .Should().Be(AgentPlanPublicFailureDisclosurePolicy.Resolve(code)!.UserFacingMessage);
            resultDetails.Extensions["taskId"].Should().Be(resultTaskId);
            resultDetails.Extensions.Should().NotContainKey("opaquePlanMetadata");
        }
    }

    [Fact]
    public void TryCreate_ShouldPreserveToolOutputSchemaInvalidWithoutRawProviderOutput()
    {
        var taskId = Guid.NewGuid();
        var created = UnhandledApiExceptionPolicy.TryCreate(
            new AgentTaskPlanPersistenceIntegrityException(
                taskId,
                AppProblemCodes.ToolOutputSchemaInvalid,
                "provider raw token=secret SELECT * FROM payroll"),
            out var decision);

        created.Should().BeTrue();
        decision!.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        decision.Problem.Code.Should().Be(AppProblemCodes.ToolOutputSchemaInvalid);
        decision.Problem.Detail.Should().Be(
            "Tool output failed its closed schema or durable-snapshot binding checks.");
        decision.Problem.ToString().Should().NotContain("provider raw")
            .And.NotContain("token=secret")
            .And.NotContain("payroll");
        decision.Problem.Extensions!["taskId"].Should().Be(taskId);
    }

    [Fact]
    public void TryCreate_ShouldHideInternalPlanRoundTripFailureAndRawPlanDetail()
    {
        var created = UnhandledApiExceptionPolicy.TryCreate(
            new AgentTaskPlanPersistenceIntegrityException(
                Guid.NewGuid(),
                "agent_plan_persistence_roundtrip_failed",
                "raw plan={\"secret\":\"do-not-expose\"}"),
            out var decision);

        created.Should().BeTrue();
        decision!.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        decision.Problem.Code.Should().Be(AppProblemCodes.InternalServerError);
        decision.Problem.Detail.Should().Be("Request failed unexpectedly. Contact support with the trace id.");
        decision.Problem.ToString().Should().NotContain("secret").And.NotContain("do-not-expose");
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

    private sealed class ResultProbeController : ApiControllerBase
    {
        public ResultProbeController()
            : base(null!)
        {
        }
    }
}
