using System.Text;
using System.Text.RegularExpressions;

namespace AICopilot.AiGatewayService.Agents;

public sealed class StreamingThinkTagFilter
{
    private const int MaxPartialTagLength = 64;

    private static readonly Regex OpeningThinkTagPattern = new(
        @"<\s*(?:mm:)?think\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ClosingThinkTagPattern = new(
        @"<\s*/\s*(?:mm:)?think\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NakedThinkLinePattern = new(
        @"(?im)^[ \t]*(?:(?:/?mm:think)|(?:/?think\b)).*(?:\r?\n|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private bool insideThinkBlock;
    private string pending = string.Empty;

    public string Append(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var buffer = pending + text;
        pending = string.Empty;
        var output = new StringBuilder();

        while (buffer.Length > 0)
        {
            if (insideThinkBlock)
            {
                var closingTag = ClosingThinkTagPattern.Match(buffer);
                if (!closingTag.Success)
                {
                    pending = KeepPotentialClosingTagTail(buffer);
                    return output.ToString();
                }

                buffer = buffer[(closingTag.Index + closingTag.Length)..];
                insideThinkBlock = false;
                continue;
            }

            var openingTag = OpeningThinkTagPattern.Match(buffer);
            var nakedThinkLine = NakedThinkLinePattern.Match(buffer);
            var nextThinkMarker = Earliest(openingTag, nakedThinkLine);

            if (nextThinkMarker is null)
            {
                var safeLength = GetSafePrefixLength(buffer);
                output.Append(buffer[..safeLength]);
                pending = buffer[safeLength..];
                return output.ToString();
            }

            if (nextThinkMarker.Index > 0)
            {
                output.Append(buffer[..nextThinkMarker.Index]);
            }

            var matchedText = buffer.Substring(nextThinkMarker.Index, nextThinkMarker.Length);
            var consumed = nextThinkMarker.Index + nextThinkMarker.Length;
            buffer = buffer[consumed..];

            insideThinkBlock = !ClosingThinkTagPattern.IsMatch(matchedText);
        }

        return output.ToString();
    }

    public string Flush()
    {
        if (pending.Length == 0)
        {
            return string.Empty;
        }

        var flushed = insideThinkBlock || LooksLikePartialThinkTag(pending) || LooksLikePartialNakedThinkMarker(pending)
            ? string.Empty
            : ModelOutputSanitizer.Strip(pending).CleanText;

        pending = string.Empty;
        insideThinkBlock = false;
        return flushed;
    }

    private static Match? Earliest(Match first, Match second)
    {
        if (!first.Success)
        {
            return second.Success ? second : null;
        }

        if (!second.Success)
        {
            return first;
        }

        return first.Index <= second.Index ? first : second;
    }

    private static int GetSafePrefixLength(string buffer)
    {
        var keepFrom = buffer.Length;
        var lastAngle = buffer.LastIndexOf('<');
        if (lastAngle >= 0
            && buffer.Length - lastAngle <= MaxPartialTagLength
            && LooksLikePartialThinkTag(buffer[lastAngle..]))
        {
            keepFrom = Math.Min(keepFrom, lastAngle);
        }

        var lineStart = Math.Max(buffer.LastIndexOf('\n'), buffer.LastIndexOf('\r')) + 1;
        if (lineStart < buffer.Length && LooksLikePartialNakedThinkMarker(buffer[lineStart..]))
        {
            keepFrom = Math.Min(keepFrom, lineStart);
        }

        return keepFrom;
    }

    private static string KeepPotentialClosingTagTail(string buffer)
    {
        var lastAngle = buffer.LastIndexOf('<');
        if (lastAngle >= 0
            && buffer.Length - lastAngle <= MaxPartialTagLength
            && LooksLikePartialThinkTag(buffer[lastAngle..]))
        {
            return buffer[lastAngle..];
        }

        var lineStart = Math.Max(buffer.LastIndexOf('\n'), buffer.LastIndexOf('\r')) + 1;
        if (lineStart < buffer.Length && LooksLikePartialNakedThinkMarker(buffer[lineStart..]))
        {
            return buffer[lineStart..];
        }

        return string.Empty;
    }

    private static bool LooksLikePartialThinkTag(string value)
    {
        var normalized = NormalizeThinkMarker(value);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        return "<think".StartsWith(normalized, StringComparison.Ordinal)
               || "</think".StartsWith(normalized, StringComparison.Ordinal)
               || "<mm:think".StartsWith(normalized, StringComparison.Ordinal)
               || "</mm:think".StartsWith(normalized, StringComparison.Ordinal);
    }

    private static bool LooksLikePartialNakedThinkMarker(string value)
    {
        var normalized = value.TrimStart().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        return "think".StartsWith(normalized, StringComparison.Ordinal)
               || "/think".StartsWith(normalized, StringComparison.Ordinal)
               || "mm:think".StartsWith(normalized, StringComparison.Ordinal)
               || "/mm:think".StartsWith(normalized, StringComparison.Ordinal);
    }

    private static string NormalizeThinkMarker(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }
}
