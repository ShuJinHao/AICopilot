namespace AICopilot.Services.Contracts.Uploads;

public static class DocumentUploadRequestPolicy
{
    public const long MaxUploadBytes = 50_000_000;

    public static string? Validate(long? contentLength)
    {
        if (contentLength is null or 0)
        {
            return "File is required.";
        }

        return contentLength > MaxUploadBytes
            ? "File exceeds the 50 MB upload limit."
            : null;
    }
}
