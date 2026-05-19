using AICopilot.EntityFrameworkCore.Security;
using AICopilot.Services.Contracts;

namespace AICopilot.Infrastructure.Security;

internal sealed class SecretProtector : ISecretProtector
{
    public string? Protect(string? plaintext)
    {
        return SecretStringEncryptor.Encrypt(plaintext);
    }

    public string? Unprotect(string? storedValue)
    {
        return SecretStringEncryptor.Decrypt(storedValue);
    }

    public bool IsProtected(string? storedValue)
    {
        return SecretStringEncryptor.IsEncrypted(storedValue);
    }

    public void EnsureConfigured()
    {
        SecretStringEncryptor.EnsureConfigured();
    }
}
