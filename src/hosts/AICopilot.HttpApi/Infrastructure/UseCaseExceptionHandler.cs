using AICopilot.Services.CrossCutting.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
namespace AICopilot.HttpApi.Infrastructure;

public class UseCaseExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ApiProblemException apiProblemException)
        {
            return false;
        }

        httpContext.Response.StatusCode = apiProblemException.StatusCode;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(
            ApiProblemDetailsFactory.Create(apiProblemException.StatusCode, apiProblemException.Problem),
            cancellationToken);

        return true;
    }
}
