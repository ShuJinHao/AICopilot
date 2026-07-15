using System.Diagnostics;

namespace AICopilot.HttpApi.Infrastructure;

public sealed class HttpTraceCorrelationMiddleware(
    RequestDelegate next,
    ILogger<HttpTraceCorrelationMiddleware> logger)
{
    public const string ResponseHeaderName = "X-AICopilot-Trace-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString();
        if (string.IsNullOrWhiteSpace(traceId) || string.Equals(traceId, default(ActivityTraceId).ToString(), StringComparison.Ordinal))
        {
            traceId = ActivityTraceId.CreateRandom().ToString();
        }

        // A single canonical value is used by logs, ProblemDetails, and the response.  This also
        // preserves the W3C traceparent id that ASP.NET Core placed in Activity.Current.
        context.TraceIdentifier = traceId;
        context.Response.OnStarting(static state =>
        {
            var (httpContext, canonicalTraceId) = ((HttpContext, string))state;
            httpContext.Response.Headers[ResponseHeaderName] = canonicalTraceId;
            return Task.CompletedTask;
        }, (context, traceId));

        using (logger.BeginScope(new Dictionary<string, object?> { ["TraceId"] = traceId }))
        {
            logger.LogInformation(
                "HTTP request started. TraceId={TraceId}; Method={Method}; Path={Path}",
                traceId,
                context.Request.Method,
                context.Request.Path.Value);
            try
            {
                await next(context).ConfigureAwait(false);
            }
            finally
            {
                logger.LogInformation(
                    "HTTP request completed. TraceId={TraceId}; StatusCode={StatusCode}",
                    traceId,
                    context.Response.StatusCode);
            }
        }
    }
}
