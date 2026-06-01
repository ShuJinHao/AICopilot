using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

internal static class ArtifactTextDiffer
{
    public static Result<ArtifactTextDiffDto> Diff(
        Guid artifactId,
        int fromVersion,
        string fromText,
        int toVersion,
        string toText)
    {
        var oldLines = SplitLines(fromText);
        var newLines = SplitLines(toText);
        if (oldLines.Length > ArtifactVersioningPolicy.MaxDiffLines ||
            newLines.Length > ArtifactVersioningPolicy.MaxDiffLines)
        {
            return Result.Invalid($"Artifact text diff exceeds the {ArtifactVersioningPolicy.MaxDiffLines} line limit.");
        }

        var rawEntries = BuildRawDiff(oldLines, newLines);
        var entries = CoalesceModifiedEntries(rawEntries);
        return Result.Success(new ArtifactTextDiffDto(
            artifactId,
            fromVersion,
            toVersion,
            oldLines.Length,
            newLines.Length,
            entries,
            false));
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static IReadOnlyList<ArtifactTextDiffEntryDto> BuildRawDiff(string[] oldLines, string[] newLines)
    {
        var lcs = new int[oldLines.Length + 1, newLines.Length + 1];
        for (var i = oldLines.Length - 1; i >= 0; i--)
        {
            for (var j = newLines.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = oldLines[i] == newLines[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var entries = new List<ArtifactTextDiffEntryDto>();
        var oldIndex = 0;
        var newIndex = 0;
        while (oldIndex < oldLines.Length && newIndex < newLines.Length)
        {
            if (oldLines[oldIndex] == newLines[newIndex])
            {
                entries.Add(new ArtifactTextDiffEntryDto(
                    "unchanged",
                    oldIndex + 1,
                    newIndex + 1,
                    oldLines[oldIndex],
                    newLines[newIndex]));
                oldIndex++;
                newIndex++;
            }
            else if (lcs[oldIndex + 1, newIndex] >= lcs[oldIndex, newIndex + 1])
            {
                entries.Add(new ArtifactTextDiffEntryDto("removed", oldIndex + 1, null, oldLines[oldIndex], null));
                oldIndex++;
            }
            else
            {
                entries.Add(new ArtifactTextDiffEntryDto("added", null, newIndex + 1, null, newLines[newIndex]));
                newIndex++;
            }
        }

        while (oldIndex < oldLines.Length)
        {
            entries.Add(new ArtifactTextDiffEntryDto("removed", oldIndex + 1, null, oldLines[oldIndex], null));
            oldIndex++;
        }

        while (newIndex < newLines.Length)
        {
            entries.Add(new ArtifactTextDiffEntryDto("added", null, newIndex + 1, null, newLines[newIndex]));
            newIndex++;
        }

        return entries;
    }

    private static IReadOnlyCollection<ArtifactTextDiffEntryDto> CoalesceModifiedEntries(
        IReadOnlyList<ArtifactTextDiffEntryDto> entries)
    {
        var result = new List<ArtifactTextDiffEntryDto>();
        var index = 0;
        while (index < entries.Count)
        {
            var removed = new List<ArtifactTextDiffEntryDto>();
            while (index < entries.Count && entries[index].Kind == "removed")
            {
                removed.Add(entries[index]);
                index++;
            }

            var added = new List<ArtifactTextDiffEntryDto>();
            while (index < entries.Count && entries[index].Kind == "added")
            {
                added.Add(entries[index]);
                index++;
            }

            var pairCount = Math.Min(removed.Count, added.Count);
            for (var pairIndex = 0; pairIndex < pairCount; pairIndex++)
            {
                result.Add(new ArtifactTextDiffEntryDto(
                    "modified",
                    removed[pairIndex].OldLine,
                    added[pairIndex].NewLine,
                    removed[pairIndex].OldText,
                    added[pairIndex].NewText));
            }

            result.AddRange(removed.Skip(pairCount));
            result.AddRange(added.Skip(pairCount));

            if (index < entries.Count && entries[index].Kind == "unchanged")
            {
                result.Add(entries[index]);
                index++;
            }
        }

        return result;
    }
}
