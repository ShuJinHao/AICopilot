namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentPlanCanonicalCollections
{
    public static string[] Strings(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    public static Guid[] Guids(IEnumerable<Guid> values)
    {
        return values
            .Where(value => value != Guid.Empty)
            .Distinct()
            .OrderBy(value => value.ToString("D"), StringComparer.Ordinal)
            .ToArray();
    }
}
