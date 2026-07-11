using AICopilot.Services.Contracts;

namespace AICopilot.RagService.Commands.Documents;

public sealed record RagDocumentUploadValidationResult(
    bool IsValid,
    FileUploadStream? File,
    string? ErrorMessage);

public static class RagDocumentUploadSecurityPolicy
{
    private const long TextMaxBytes = 10_000_000;
    private const long DocumentMaxBytes = 50_000_000;

    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".bat",
        ".cmd",
        ".ps1",
        ".sh",
        ".dll",
        ".so",
        ".jar",
        ".zip",
        ".rar",
        ".7z",
        ".sql",
        ".js",
        ".html"
    };

    private static readonly IReadOnlyDictionary<string, DocumentRule> Rules =
        new Dictionary<string, DocumentRule>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = new(TextMaxBytes, ["text/plain"]),
            [".md"] = new(TextMaxBytes, ["text/markdown", "text/plain"]),
            [".pdf"] = new(DocumentMaxBytes, ["application/pdf"]),
            [".docx"] = new(DocumentMaxBytes, ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"]),
            [".xlsx"] = new(DocumentMaxBytes, ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]),
            [".csv"] = new(TextMaxBytes, ["text/csv", "text/plain", "application/vnd.ms-excel"]),
            [".json"] = new(TextMaxBytes, ["application/json", "text/json", "text/plain"])
        };

    public static async Task<FileUploadStream> NormalizeStreamAsync(
        FileUploadStream file,
        CancellationToken cancellationToken)
    {
        if (file.Stream.CanSeek)
        {
            file.Stream.Position = 0;
            return file with { FileSize = file.FileSize ?? file.Stream.Length };
        }

        var memory = new MemoryStream();
        await file.Stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return file with { Stream = memory, FileSize = memory.Length };
    }

    public static async Task<RagDocumentUploadValidationResult> ValidateAndNormalizeAsync(
        FileUploadStream file,
        IDocumentFormatPolicy documentFormatPolicy,
        CancellationToken cancellationToken)
    {
        var safeFileName = UploadFileNamePolicy.Normalize(file.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return Invalid("RAG document file name is invalid.");
        }

        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            return Invalid("RAG document extension is required.");
        }

        if (DangerousExtensions.Contains(extension))
        {
            return Invalid($"RAG document type {extension} is not allowed.");
        }

        if (!Rules.TryGetValue(extension, out var rule) || !documentFormatPolicy.IsSupported(extension))
        {
            var supported = string.Join(", ", documentFormatPolicy.SupportedExtensions);
            return Invalid($"Unsupported RAG document format: {extension}. Supported formats: {supported}");
        }

        var fileSize = file.FileSize ?? 0;
        if (fileSize <= 0)
        {
            return Invalid("RAG document is empty.");
        }

        if (fileSize > rule.MaxBytes)
        {
            return Invalid($"RAG document exceeds the {rule.MaxBytes} byte limit for {extension} files.");
        }

        var contentType = NormalizeContentType(file.ContentType);
        if (!IsAllowedContentType(contentType, rule))
        {
            return Invalid($"RAG document content type {contentType} is not allowed for {extension} files.");
        }

        var header = await ReadHeaderAsync(file.Stream, cancellationToken);
        if (LooksLikeExecutable(header))
        {
            return Invalid("RAG document content looks executable and is not allowed.");
        }

        if (!HeaderMatchesExtension(extension, header))
        {
            return Invalid($"RAG document content does not match {extension}.");
        }

        return new RagDocumentUploadValidationResult(
            true,
            file with
            {
                FileName = safeFileName,
                ContentType = contentType == "application/octet-stream" ? null : contentType
            },
            null);
    }

    private static RagDocumentUploadValidationResult Invalid(string message)
    {
        return new RagDocumentUploadValidationResult(false, null, message);
    }

    private static string NormalizeContentType(string? contentType)
    {
        return string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
    }

    private static bool IsAllowedContentType(string contentType, DocumentRule rule)
    {
        return contentType == "application/octet-stream" ||
               rule.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
        {
            throw new InvalidOperationException("RAG document stream must be seekable before validation.");
        }

        var originalPosition = stream.Position;
        stream.Position = 0;
        var buffer = new byte[512];
        var read = await stream.ReadAsync(buffer, cancellationToken);
        stream.Position = originalPosition;
        return buffer[..read];
    }

    private static bool LooksLikeExecutable(ReadOnlySpan<byte> header)
    {
        return header.Length >= 2 && header[0] == 0x4D && header[1] == 0x5A;
    }

    private static bool HeaderMatchesExtension(string extension, ReadOnlySpan<byte> header)
    {
        return extension switch
        {
            ".pdf" => StartsWith(header, "%PDF"u8),
            ".docx" or ".xlsx" => StartsWith(header, "PK"u8),
            ".txt" or ".md" or ".csv" or ".json" => !header.Contains((byte)0x00),
            _ => false
        };
    }

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> expected)
    {
        return header.Length >= expected.Length && header[..expected.Length].SequenceEqual(expected);
    }

    private sealed record DocumentRule(long MaxBytes, IReadOnlyCollection<string> AllowedContentTypes);
}
