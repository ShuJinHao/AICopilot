using AICopilot.SharedKernel.Result;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using IResult = AICopilot.SharedKernel.Result.IResult;

namespace AICopilot.HttpApi.Infrastructure;

[ApiController]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public abstract class ApiControllerBase(ISender sender) : ControllerBase
{
    protected ISender Sender { get; } = sender;

    [NonAction]
    public IActionResult ReturnResult(IResult result)
    {
        switch (result.Status)
        {
            case ResultStatus.Ok:
                {
                    var value = result.GetValue();
                    return value is null ? NoContent() : Ok(value);
                }
            case ResultStatus.Error:
                return StatusCode(
                    StatusCodes.Status400BadRequest,
                    CreateProblemDetails(StatusCodes.Status400BadRequest, result.Errors));

            case ResultStatus.NotFound:
                return StatusCode(
                    StatusCodes.Status404NotFound,
                    CreateProblemDetails(StatusCodes.Status404NotFound, result.Errors));

            case ResultStatus.Invalid:
                return StatusCode(
                    StatusCodes.Status400BadRequest,
                    CreateProblemDetails(StatusCodes.Status400BadRequest, result.Errors));

            case ResultStatus.Forbidden:
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    CreateProblemDetails(StatusCodes.Status403Forbidden, result.Errors));

            case ResultStatus.Unauthorized:
                return StatusCode(
                    StatusCodes.Status401Unauthorized,
                    CreateProblemDetails(StatusCodes.Status401Unauthorized, result.Errors));

            default:
                return StatusCode(
                    StatusCodes.Status400BadRequest,
                    CreateProblemDetails(StatusCodes.Status400BadRequest, result.Errors));
        }
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
