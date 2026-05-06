using System;
using System.Collections.Generic;
using System.Text;
using UglyToad.PdfPig;

namespace AICopilot.Infrastructure.Rag.Parsers;

public class PdfDocumentParser : IDocumentParser
{
    public string[] SupportedExtensions => [".pdf"];

    public async Task<string> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            using var buffer = stream.CanSeek ? null : await CopyToSeekableStreamAsync(stream, cancellationToken);
            var pdfStream = buffer ?? stream;
            if (pdfStream.CanSeek)
            {
                pdfStream.Position = 0;
            }

            return await Task.Run(() => ExtractText(pdfStream, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("PDF 解析失败，文件可能已损坏或加密。", ex);
        }
    }

    private static string ExtractText(Stream stream, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        using var pdfDocument = PdfDocument.Open(stream);

        foreach (var page in pdfDocument.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
            }
        }

        return sb.ToString();
    }

    private static async Task<MemoryStream> CopyToSeekableStreamAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        try
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Position = 0;
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }
}
