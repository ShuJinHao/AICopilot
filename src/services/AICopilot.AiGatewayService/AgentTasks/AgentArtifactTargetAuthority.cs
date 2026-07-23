using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentArtifactTargetAuthority
{
    private static readonly string[] DefaultTargets =
        ["chart", "html", "markdown", "pdf", "pptx", "xlsx"];

    private static readonly IReadOnlySet<string> AllowedTargets =
        new HashSet<string>(DefaultTargets, StringComparer.Ordinal);

    public static Result<IReadOnlySet<string>> Resolve(
        IReadOnlyCollection<string>? artifactTargets)
    {
        if (artifactTargets is null)
        {
            return Result.Success<IReadOnlySet<string>>(
                new HashSet<string>(DefaultTargets, StringComparer.Ordinal));
        }

        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in artifactTargets)
        {
            if (target is null || !AllowedTargets.Contains(target))
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentPlanSchemaInvalid,
                    "artifactTargets contains an unknown Plan v2 artifact target."));
            }

            resolved.Add(target);
        }

        return Result.Success<IReadOnlySet<string>>(resolved);
    }
}
