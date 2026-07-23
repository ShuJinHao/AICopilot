namespace AICopilot.Core.AiGateway.Runtime.AgentExecution;

internal static class AgentRuntimeValueNormalizer
{
    public static string NormalizeRequired(string value, string paramName, int? maxLength = null)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return maxLength.HasValue && normalized.Length > maxLength.Value
            ? normalized[..maxLength.Value]
            : normalized;
    }

    public static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }

    public static string Required(string value, string paramName, int? maxLength = null) =>
        NormalizeRequired(value, paramName, maxLength);

    public static string? Optional(string? value, int maxLength) =>
        NormalizeOptional(value, maxLength);
}
