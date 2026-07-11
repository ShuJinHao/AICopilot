namespace AICopilot.Services.Contracts;

public interface IFileStorageService
{
    Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
