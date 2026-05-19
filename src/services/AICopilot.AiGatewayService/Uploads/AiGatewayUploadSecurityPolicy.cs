namespace AICopilot.AiGatewayService.Uploads;

public sealed record AiGatewayUploadValidationResult(
    bool IsValid,
    AiGatewayUploadStream? File,
    string? ErrorMessage);

public static class AiGatewayUploadSecurityPolicy
{
    private const long TextMaxBytes = 10_000_000;
    private const long ImageMaxBytes = 15_000_000;
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

    private static readonly IReadOnlyDictionary<string, UploadRule> Rules =
        new Dictionary<string, UploadRule>(StringComparer.OrdinalIgnoreCase)
        {
            [".csv"] = new("text/csv", TextMaxBytes, ["text/csv", "text/plain", "application/vnd.ms-excel"]),
            [".json"] = new("application/json", TextMaxBytes, ["application/json", "text/json", "text/plain"]),
            [".txt"] = new("text/plain", TextMaxBytes, ["text/plain"]),
            [".md"] = new("text/markdown", TextMaxBytes, ["text/markdown", "text/plain"]),
            [".pdf"] = new("application/pdf", DocumentMaxBytes, ["application/pdf"]),
            [".docx"] = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", DocumentMaxBytes, ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"]),
            [".xlsx"] = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", DocumentMaxBytes, ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]),
            [".png"] = new("image/png", ImageMaxBytes, ["image/png"]),
            [".jpg"] = new("image/jpeg", ImageMaxBytes, ["image/jpeg"]),
            [".jpeg"] = new("image/jpeg", ImageMaxBytes, ["image/jpeg"]),
            [".webp"] = new("image/webp", ImageMaxBytes, ["image/webp"])
        };

    public static async Task<AiGatewayUploadValidationResult> ValidateAndNormalizeAsync(
        AiGatewayUploadStream file,
        CancellationToken cancellationToken)
    {
        var safeFileName = SanitizeFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return Invalid("Uploaded file name is invalid.");
        }

        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            return Invalid("Uploaded file extension is required.");
        }

        if (DangerousExtensions.Contains(extension))
        {
            return Invalid($"Uploaded file type {extension} is not allowed.");
        }

        if (!Rules.TryGetValue(extension, out var rule))
        {
            return Invalid($"Uploaded file type {extension} is not supported.");
        }

        if (file.FileSize <= 0)
        {
            return Invalid("Uploaded file is empty.");
        }

        if (file.FileSize > rule.MaxBytes)
        {
            return Invalid($"Uploaded file exceeds the {rule.MaxBytes} byte limit for {extension} files.");
        }

        var contentType = NormalizeContentType(file.ContentType);
        if (!IsAllowedContentType(contentType, rule))
        {
            return Invalid($"Uploaded content type {contentType} is not allowed for {extension} files.");
        }

        var header = await ReadHeaderAsync(file.Stream, cancellationToken);
        if (LooksLikeExecutable(header))
        {
            return Invalid("Uploaded file content looks executable and is not allowed.");
        }

        if (!HeaderMatchesExtension(extension, header))
        {
            return Invalid($"Uploaded file content does not match {extension}.");
        }

        var normalizedContentType = contentType == "application/octet-stream"
            ? rule.CanonicalContentType
            : contentType;
        return new AiGatewayUploadValidationResult(
            true,
            file with
            {
                FileName = safeFileName,
                ContentType = normalizedContentType
            },
            null);
    }

    private static AiGatewayUploadValidationResult Invalid(string message)
    {
        return new AiGatewayUploadValidationResult(false, null, message);
    }

    private static string SanitizeFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return string.Empty;
        }

        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(safeName
            .Trim()
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());
        if (sanitized.Length > 180)
        {
            var extension = Path.GetExtension(sanitized);
            var stemLength = Math.Max(1, 180 - extension.Length);
            sanitized = sanitized[..stemLength] + extension;
        }

        return sanitized;
    }

    private static string NormalizeContentType(string? contentType)
    {
        return string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
    }

    private static bool IsAllowedContentType(string contentType, UploadRule rule)
    {
        return contentType == "application/octet-stream" ||
               rule.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
        {
            throw new InvalidOperationException("Upload stream must be seekable before validation.");
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
            ".png" => StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            ".jpg" or ".jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".webp" => header.Length >= 12 && StartsWith(header, "RIFF"u8) && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50,
            ".docx" or ".xlsx" => StartsWith(header, "PK"u8),
            ".csv" or ".json" or ".txt" or ".md" => !ContainsNullByte(header),
            _ => false
        };
    }

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> expected)
    {
        return header.Length >= expected.Length && header[..expected.Length].SequenceEqual(expected);
    }

    private static bool ContainsNullByte(ReadOnlySpan<byte> header)
    {
        return header.Contains((byte)0x00);
    }

    private sealed record UploadRule(
        string CanonicalContentType,
        long MaxBytes,
        IReadOnlyCollection<string> AllowedContentTypes);
}
