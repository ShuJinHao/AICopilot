using System.Text;

namespace AICopilot.Services.Contracts;

public static class UploadFileNamePolicy
{
    public const int MaximumUtf8Bytes = 180;

    private static readonly HashSet<char> InvalidFileNameCharacters =
    [
        .. Path.GetInvalidFileNameChars(),
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    ];

    public static string Normalize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var baseName = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        if (baseName is "." or ".." || baseName.Any(char.IsControl))
        {
            return string.Empty;
        }

        var normalized = new string(baseName
            .Select(character => InvalidFileNameCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(normalized) || normalized is "." or "..")
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetByteCount(normalized) <= MaximumUtf8Bytes
            ? normalized
            : TruncatePreservingExtension(normalized);
    }

    public static string NormalizeForAudit(string? fileName)
    {
        var normalized = Normalize(fileName);
        return string.IsNullOrEmpty(normalized) ? "invalid-upload-name" : normalized;
    }

    private static string TruncatePreservingExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var extensionBytes = Encoding.UTF8.GetByteCount(extension);
        if (string.IsNullOrEmpty(extension) || extensionBytes >= MaximumUtf8Bytes - 1)
        {
            return TruncateUtf8(fileName, MaximumUtf8Bytes);
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var truncatedStem = TruncateUtf8(stem, MaximumUtf8Bytes - extensionBytes);
        return string.IsNullOrEmpty(truncatedStem)
            ? string.Empty
            : truncatedStem + extension;
    }

    private static string TruncateUtf8(string value, int maximumBytes)
    {
        var builder = new StringBuilder(value.Length);
        var byteCount = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (byteCount + runeBytes > maximumBytes)
            {
                break;
            }

            builder.Append(rune);
            byteCount += runeBytes;
        }

        return builder.ToString();
    }
}
