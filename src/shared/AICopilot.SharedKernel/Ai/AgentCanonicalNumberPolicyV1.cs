using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AICopilot.SharedKernel.Ai;

/// <summary>
/// The single finite arbitrary-precision numeric domain for canonical agent JSON.
/// Limits are checked before numeric parsing to bound CPU and memory use.
/// </summary>
public static class AgentCanonicalNumberPolicyV1
{
    public const string Version = "canonical-json-number-policy:v1";
    public const string PolicyVersion = Version;
    public const int MaxLexicalCharacters = 384;
    public const int MaxSignificantDigits = 256;
    public const int MaxExponentDigits = 6;
    public const int MaxAbsoluteExponent = 100_000;

    private static readonly Regex Grammar = new(
        "^(?<sign>-)?(?<integer>0|[1-9][0-9]*)(?:\\.(?<fraction>[0-9]+))?(?:[eE](?<exponent>[+-]?[0-9]+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string rawNumber)
    {
        ArgumentNullException.ThrowIfNull(rawNumber);
        if (rawNumber.Length is 0 or > MaxLexicalCharacters)
        {
            throw OutsideDomain();
        }

        var match = Grammar.Match(rawNumber);
        if (!match.Success)
        {
            throw OutsideDomain();
        }

        var exponentText = match.Groups["exponent"].Success
            ? match.Groups["exponent"].Value
            : "0";
        var exponentDigits = exponentText.TrimStart('+', '-');
        if (exponentDigits.Length > MaxExponentDigits ||
            !int.TryParse(exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exponent) ||
            Math.Abs((long)exponent) > MaxAbsoluteExponent)
        {
            throw OutsideDomain();
        }

        var fraction = match.Groups["fraction"].Success
            ? match.Groups["fraction"].Value
            : string.Empty;
        var coefficient = (match.Groups["integer"].Value + fraction).TrimStart('0');
        if (coefficient.Length == 0)
        {
            return "0";
        }

        var trailingZeroCount = coefficient.Length - coefficient.TrimEnd('0').Length;
        var significantDigits = trailingZeroCount == 0
            ? coefficient
            : coefficient[..^trailingZeroCount];
        if (significantDigits.Length > MaxSignificantDigits)
        {
            throw OutsideDomain();
        }

        var effectiveExponent = (long)exponent - fraction.Length + trailingZeroCount;
        if (Math.Abs(effectiveExponent) > MaxAbsoluteExponent)
        {
            throw OutsideDomain();
        }

        var sign = match.Groups["sign"].Success ? "-" : string.Empty;
        if (effectiveExponent == 0)
        {
            return sign + significantDigits;
        }

        // Keep integral values in ordinary JSON integer form so strict int/long
        // consumers and JSON-schema integer validation see the same value. Very
        // large magnitudes retain bounded exponent notation.
        if (effectiveExponent > 0 &&
            significantDigits.Length + effectiveExponent <= MaxSignificantDigits)
        {
            return sign + significantDigits + new string('0', (int)effectiveExponent);
        }

        return $"{sign}{significantDigits}e{effectiveExponent.ToString(CultureInfo.InvariantCulture)}";
    }

    private static JsonException OutsideDomain()
    {
        return new JsonException($"JSON number is outside {PolicyVersion}.");
    }
}
