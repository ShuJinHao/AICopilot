using System;
using System.Collections.Generic;
using System.Text;
using UglyToad.PdfPig;

namespace AICopilot.Infrastructure.Rag.Parsers;

public class PdfDocumentParser : IDocumentParser
{
    public string[] SupportedExtensions => [".pdf"];

    public Task<string> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var sb = new StringBuilder();

            try
            {
                using var buffer = stream.CanSeek ? null : CopyToSeekableStream(stream, cancellationToken);
                var pdfStream = buffer ?? stream;
                if (pdfStream.CanSeek)
                {
                    pdfStream.Position = 0;
                }

                using var pdfDocument = PdfDocument.Open(pdfStream);

                foreach (var page in pdfDocument.GetPages())
                {
                    // 提取每一页的文本，并用换行符分隔
                    // 实际生产中可能需要更复杂的版面分析算法来处理多栏排版
                    var text = page.Text;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("PDF 解析失败，文件可能已损坏或加密。", ex);
            }

            return sb.ToString();
        }, cancellationToken);
    }

    private static MemoryStream CopyToSeekableStream(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        try
        {
            stream.CopyTo(buffer);
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
