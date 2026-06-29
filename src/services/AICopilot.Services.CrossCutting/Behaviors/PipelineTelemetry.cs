using System.Diagnostics;

namespace AICopilot.Services.CrossCutting.Behaviors;

public static class PipelineTelemetry
{
    public const string ActivitySourceName = "AICopilot.MediatR";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
