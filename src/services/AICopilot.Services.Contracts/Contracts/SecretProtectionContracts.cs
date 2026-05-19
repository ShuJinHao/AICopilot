namespace AICopilot.Services.Contracts;

public interface ISecretProtector
{
    string? Protect(string? plaintext);

    string? Unprotect(string? storedValue);

    bool IsProtected(string? storedValue);

    void EnsureConfigured();
}
