namespace AICopilot.Services.Contracts;

public interface IDocumentFormatPolicy
{
    IReadOnlyCollection<string> SupportedExtensions { get; }

    bool IsSupported(string extension);
}
