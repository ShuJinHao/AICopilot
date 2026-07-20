using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.HttpApi.Infrastructure;

public static class ApiProblemDetailsFactory
{
    public static ProblemDetails Create(
        int statusCode,
        ApiProblemDescriptor? problem = null,
        string? fallbackDetail = null,
        string? traceIdentifier = null)
    {
        var planFailure = AgentPlanPublicFailureDisclosurePolicy.Resolve(problem?.Code);
        var details = new ProblemDetails
        {
            Status = statusCode,
            Title = GetTitle(statusCode),
            Type = GetType(statusCode),
            Detail = planFailure?.Detail ?? problem?.Detail ?? fallbackDetail
        };

        if (planFailure is not null)
        {
            details.Extensions[ApiProblemExtensionKeys.UserFacingMessage] =
                planFailure.UserFacingMessage;
            if (problem?.Extensions is not null &&
                problem.Extensions.TryGetValue("taskId", out var taskId) &&
                taskId is Guid taskGuid &&
                taskGuid != Guid.Empty)
            {
                details.Extensions["taskId"] = taskGuid;
            }
        }
        else if (problem?.Extensions is not null)
        {
            foreach (var (key, value) in problem.Extensions)
            {
                if (IsReservedExtensionKey(key))
                {
                    continue;
                }

                details.Extensions[key] = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(problem?.Code))
        {
            details.Extensions[ApiProblemExtensionKeys.Code] = problem.Code;
        }

        if (!string.IsNullOrWhiteSpace(traceIdentifier))
        {
            details.Extensions[ApiProblemExtensionKeys.TraceId] = traceIdentifier;
        }

        return details;
    }

    private static bool IsReservedExtensionKey(string key)
    {
        return string.Equals(
                key,
                ApiProblemExtensionKeys.Code,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                key,
                ApiProblemExtensionKeys.TraceId,
                StringComparison.OrdinalIgnoreCase);
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
            StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
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
            StatusCodes.Status503ServiceUnavailable => "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/503",
            _ => "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status"
        };
    }
}
