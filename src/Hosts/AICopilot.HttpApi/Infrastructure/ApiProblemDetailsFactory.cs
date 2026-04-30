using Microsoft.AspNetCore.Mvc;
using AICopilot.SharedKernel.Result;

namespace AICopilot.HttpApi.Infrastructure;

public static class ApiProblemDetailsFactory
{
    public static ProblemDetails Create(int statusCode, ApiProblemDescriptor? problem = null, string? fallbackDetail = null)
    {
        var details = new ProblemDetails
        {
            Status = statusCode,
            Title = GetTitle(statusCode),
            Type = GetType(statusCode),
            Detail = problem?.Detail ?? fallbackDetail
        };

        if (!string.IsNullOrWhiteSpace(problem?.Code))
        {
            details.Extensions["code"] = problem.Code;
        }

        if (problem?.Extensions is not null)
        {
            foreach (var (key, value) in problem.Extensions)
            {
                details.Extensions[key] = value;
            }
        }

        return details;
    }

    private static string GetTitle(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status404NotFound => "Not Found",
            StatusCodes.Status400BadRequest => "Bad Request",
            StatusCodes.Status429TooManyRequests => "Too Many Requests",
            _ => "Error"
        };
    }

    private static string GetType(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status401Unauthorized => "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/401",
            StatusCodes.Status403Forbidden => "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/403",
            StatusCodes.Status404NotFound => "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/404",
            StatusCodes.Status400BadRequest => "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/400",
            StatusCodes.Status429TooManyRequests => "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/429",
            _ => "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status"
        };
    }
}
