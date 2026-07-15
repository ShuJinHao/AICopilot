namespace AICopilot.SharedKernel.Ai;

/// <summary>
/// Derives the minimum trust classification from target identity and transport metadata.  A caller
/// supplied enum can tighten this classification, but it cannot relabel a Cloud endpoint as non-Cloud.
/// </summary>
public static class McpTargetTrustPolicy
{
    private static readonly string[] CloudTokens = ["cloud", "cloudplatform", "iiotcloud"];

    public static bool RequiresCloudReadOnly(params string?[] targetMetadata)
    {
        foreach (var value in targetMetadata.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (SplitIdentifier(value!).Any(token => CloudTokens.Contains(token, StringComparer.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
                && SplitIdentifier(endpoint.Host).Any(token => CloudTokens.Contains(token, StringComparer.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SplitIdentifier(string value)
    {
        var current = new List<char>();
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                if (current.Count != 0)
                {
                    yield return new string(current.ToArray()).ToLowerInvariant();
                    current.Clear();
                }

                continue;
            }

            if (char.IsUpper(character) && current.Count != 0)
            {
                yield return new string(current.ToArray()).ToLowerInvariant();
                current.Clear();
            }

            current.Add(character);
        }

        if (current.Count != 0)
        {
            yield return new string(current.ToArray()).ToLowerInvariant();
        }
    }
}
