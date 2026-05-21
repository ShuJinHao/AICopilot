using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.SharedKernel.Result;
using MediatR;
using AppResult = AICopilot.SharedKernel.Result.IResult;
using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace AICopilot.HttpApi;

public static class CloudReadonlyReadinessEndpoints
{
    public static IEndpointRouteBuilder MapCloudReadonlyReadinessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/aigateway/cloud-readonly/readiness")
            .RequireAuthorization()
            .WithTags("CloudReadonlyReadiness");

        group.MapGet(string.Empty, async (ISender sender) =>
            ToHttpResult(await sender.Send(new GetCloudReadonlyReadinessQuery())));

        group.MapGet("history", async (ISender sender) =>
            ToHttpResult(await sender.Send(new GetCloudReadonlyReadinessHistoryQuery())));

        group.MapGet("sandbox", async (ISender sender) =>
            ToHttpResult(await sender.Send(new GetCloudReadonlySandboxStatusQuery())));

        group.MapGet("sandbox/history", async (ISender sender) =>
            ToHttpResult(await sender.Send(new GetCloudReadonlySandboxSmokeHistoryQuery())));

        group.MapGet("sandbox-agent-trial", async (ISender sender) =>
            ToHttpResult(await sender.Send(new GetCloudReadonlySandboxAgentTrialStatusQuery())));

        group.MapGet("sandbox-controlled-trial", async (ISender sender) =>
            ToHttpResult(await sender.Send(new GetCloudReadonlySandboxControlledTrialStatusQuery())));

        group.MapGet("pilot-readiness", async (ISender sender) =>
            ToHttpResult(await sender.Send(new GetCloudReadonlyPilotReadinessQuery())));

        group.MapGet("production-pilot", async (ISender sender) =>
            ToHttpResult(await sender.Send(new GetCloudReadonlyProductionPilotStatusQuery())));

        group.MapPost("sandbox-agent-trial/run", async (RunCloudReadonlySandboxAgentTrialCommand command, ISender sender) =>
            ToHttpResult(await sender.Send(command)));

        group.MapPost("pilot-readiness/config-package", async (CreateCloudReadonlyPilotConfigPackageCommand command, ISender sender) =>
            ToHttpResult(await sender.Send(command)));

        group.MapPost("pilot-readiness/gate", async (RunCloudReadonlyPilotGateEvaluationCommand command, ISender sender) =>
            ToHttpResult(await sender.Send(command)));

        group.MapPost("pilot-readiness/approval-rehearsal", async (RunCloudReadonlyPilotApprovalRehearsalCommand command, ISender sender) =>
            ToHttpResult(await sender.Send(command)));

        group.MapPost("pilot-readiness/contract-rehearsal", async (RunCloudReadonlyPilotContractRehearsalCommand command, ISender sender) =>
            ToHttpResult(await sender.Send(command)));

        group.MapPost("production-pilot/window", async (CreateCloudReadonlyProductionPilotWindowCommand command, ISender sender) =>
            ToHttpResult(await sender.Send(command)));

        group.MapPost("production-pilot/window/status", async (UpdateCloudReadonlyProductionPilotWindowStatusCommand command, ISender sender) =>
            ToHttpResult(await sender.Send(command)));

        group.MapPost("production-pilot/gate", async (ISender sender) =>
            ToHttpResult(await sender.Send(new RunCloudReadonlyProductionPilotGateEvaluationCommand())));

        group.MapPost("production-pilot/run", async (RunCloudReadonlyProductionPilotScenarioCommand command, ISender sender) =>
            ToHttpResult(await sender.Send(command)));

        group.MapPost("run", async (RunCloudReadonlyReadinessCheckCommand command, ISender sender) =>
            ToHttpResult(await sender.Send(command)));

        return endpoints;
    }

    private static HttpResult ToHttpResult(AppResult result)
    {
        return result.Status switch
        {
            ResultStatus.Ok => result.GetValue() is { } value
                ? Results.Ok(value)
                : Results.NoContent(),
            ResultStatus.Error => result.Errors is null
                ? Results.BadRequest()
                : Results.BadRequest(new { errors = result.Errors }),
            ResultStatus.NotFound => result.Errors is null
                ? Results.NotFound()
                : Results.NotFound(new { errors = result.Errors }),
            ResultStatus.Invalid => result.Errors is null
                ? Results.BadRequest()
                : Results.BadRequest(new { errors = result.Errors }),
            ResultStatus.Forbidden => Results.Json(
                CreateProblemDetails(StatusCodes.Status403Forbidden, result.Errors),
                statusCode: StatusCodes.Status403Forbidden),
            ResultStatus.Unauthorized => Results.Json(
                CreateProblemDetails(StatusCodes.Status401Unauthorized, result.Errors),
                statusCode: StatusCodes.Status401Unauthorized),
            _ => Results.BadRequest()
        };
    }

    private static ProblemDetails CreateProblemDetails(int statusCode, IEnumerable<object>? errors)
    {
        var problem = errors?.OfType<ApiProblemDescriptor>().FirstOrDefault();
        return ApiProblemDetailsFactory.Create(
            statusCode,
            problem,
            errors?.Select(error => error?.ToString())
                .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message)));
    }
}
