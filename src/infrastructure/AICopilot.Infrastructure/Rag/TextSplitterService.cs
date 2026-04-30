using AICopilot.Infrastructure.Rag.TokenCounter;
using AICopilot.Services.Contracts;
using Microsoft.SemanticKernel.Text;

#pragma warning disable SKEXP0050

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

        var cleanText = Preprocess(text);
        var lines = TextChunker.SplitPlainTextLines(
            cleanText,
            maxTokensPerLine: DefaultMaxTokensPerLine,
            tokenCounter: tokenCounter.CountTokens);

        return TextChunker.SplitPlainTextParagraphs(
            lines,
            maxTokensPerParagraph: DefaultMaxTokensPerParagraph,
            overlapTokens: DefaultOverlapTokens,
            tokenCounter: tokenCounter.CountTokens);
    }

    private static string Preprocess(string text)
    {
        return text.Replace("\r\n", "\n").Trim();
    }
}
