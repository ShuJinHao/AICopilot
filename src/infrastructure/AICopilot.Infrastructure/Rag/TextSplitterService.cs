using AICopilot.Infrastructure.Rag.TokenCounter;
using AICopilot.Services.Contracts;

namespace AICopilot.Infrastructure.Rag;

public class TextSplitterService(ITokenCounter tokenCounter) : IDocumentTextSplitter
{
    private const int DefaultMaxTokensPerParagraph = 500;
    private const int DefaultMaxTokensPerLine = 120;
    private const int DefaultOverlapTokens = 50;

    public IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var lines = SplitLines(Preprocess(text), DefaultMaxTokensPerLine);
        return SplitParagraphs(lines, DefaultMaxTokensPerParagraph, DefaultOverlapTokens);
    }

    private static string Preprocess(string text)
    {
        return text.Replace("\r\n", "\n").Trim();
    }

    private IReadOnlyList<string> SplitLines(string text, int maxTokensPerLine)
    {
        var result = new List<string>();
        foreach (var paragraphLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddTokenLimitedSegments(paragraphLine, maxTokensPerLine, result);
        }

        return result;
    }

    private void AddTokenLimitedSegments(string text, int maxTokens, List<string> output)
    {
        if (tokenCounter.CountTokens(text) <= maxTokens)
        {
            output.Add(text);
            return;
        }

        var current = new List<string>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = current.Count == 0 ? word : $"{string.Join(' ', current)} {word}";
            if (tokenCounter.CountTokens(candidate) <= maxTokens)
            {
                current.Add(word);
                continue;
            }

            FlushCurrent();

            if (tokenCounter.CountTokens(word) <= maxTokens)
            {
                current.Add(word);
            }
            else
            {
                output.Add(word);
            }
        }

        FlushCurrent();

        void FlushCurrent()
        {
            if (current.Count == 0)
            {
                return;
            }

            output.Add(string.Join(' ', current));
            current.Clear();
        }
    }

    private IReadOnlyList<string> SplitParagraphs(
        IReadOnlyList<string> lines,
        int maxTokensPerParagraph,
        int overlapTokens)
    {
        var result = new List<string>();
        var current = new List<string>();

        foreach (var line in lines)
        {
            var candidate = current.Count == 0 ? line : $"{string.Join('\n', current)}\n{line}";
            if (current.Count == 0 || tokenCounter.CountTokens(candidate) <= maxTokensPerParagraph)
            {
                current.Add(line);
                continue;
            }

            result.Add(string.Join('\n', current));
            current = BuildOverlapLines(current, overlapTokens);
            current.Add(line);
        }

        if (current.Count > 0)
        {
            result.Add(string.Join('\n', current));
        }

        return result;
    }

    private List<string> BuildOverlapLines(IReadOnlyList<string> lines, int overlapTokens)
    {
        var overlap = new List<string>();
        var tokenCount = 0;

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var lineTokens = tokenCounter.CountTokens(lines[i]);
            if (tokenCount > 0 && tokenCount + lineTokens > overlapTokens)
            {
                break;
            }

            overlap.Insert(0, lines[i]);
            tokenCount += lineTokens;
        }

        return overlap;
    }
}
