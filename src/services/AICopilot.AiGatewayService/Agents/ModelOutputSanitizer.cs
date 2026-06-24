using System.Text.RegularExpressions;

namespace AICopilot.AiGatewayService.Agents;

public static class ModelOutputSanitizer
{
    private static readonly Regex CompleteThinkTagPattern = new(
        @"<\s*(?:mm:)?think\s*>[\s\S]*?<\s*/\s*(?:mm:)?think\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LeadingCloseTagPattern = new(
        @"^[\s\S]*?<\s*/\s*(?:mm:)?think\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TrailingOpenTagPattern = new(
        @"<\s*(?:mm:)?think\s*>[\s\S]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NakedThinkLinePattern = new(
        @"^\s*(?:/?mm:think|/?think)\b.*(?:\r?\n|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex ResidualTagPattern = new(
        @"<\s*/?\s*(?:mm:)?think\s*/?\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ModelOutputSanitizerResult Strip(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new ModelOutputSanitizerResult(string.Empty, null);
        }

        var thinkingParts = new List<string>();
        var clean = CompleteThinkTagPattern.Replace(text, match =>
        {
            var thinking = StripResidualTags(match.Value).Trim();
            if (!string.IsNullOrWhiteSpace(thinking))
            {
                thinkingParts.Add(thinking);
            }

            return string.Empty;
        });

        clean = LeadingCloseTagPattern.Replace(clean, match =>
        {
            var thinking = StripResidualTags(match.Value).Trim();
            if (!string.IsNullOrWhiteSpace(thinking))
            {
                thinkingParts.Add(thinking);
            }

            return string.Empty;
        });
        clean = TrailingOpenTagPattern.Replace(clean, match =>
        {
            var thinking = StripResidualTags(match.Value).Trim();
            if (!string.IsNullOrWhiteSpace(thinking))
            {
                thinkingParts.Add(thinking);
            }

            return string.Empty;
        });
        clean = NakedThinkLinePattern.Replace(clean, match =>
        {
            var thinking = match.Value.Trim();
            if (!string.IsNullOrWhiteSpace(thinking))
            {
                thinkingParts.Add(thinking);
            }

            return string.Empty;
        });
        clean = StripResidualTags(clean);

        var thinkingText = thinkingParts.Count > 0
            ? string.Join(Environment.NewLine, thinkingParts)
            : null;
        return new ModelOutputSanitizerResult(clean, thinkingText);
    }

    private static string StripResidualTags(string value)
    {
        return ResidualTagPattern.Replace(value, string.Empty);
    }
}

public sealed record ModelOutputSanitizerResult(string CleanText, string? ThinkingText);
