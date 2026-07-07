using AICopilot.Services.Contracts;

namespace AICopilot.BackendTests;

internal sealed class EndpointPoolSecretProtector : ISecretProtector
{
    public string? Protect(string? plaintext)
    {
        return string.IsNullOrEmpty(plaintext) ? plaintext : $"encv2:{plaintext}";
    }

    public string? Unprotect(string? storedValue)
    {
        if (string.IsNullOrEmpty(storedValue))
        {
            return storedValue;
        }

        if (!IsProtected(storedValue))
        {
            throw new InvalidOperationException("Stored secret must be encrypted with 'encv2:'.");
        }

        return storedValue["encv2:".Length..];
    }

    public bool IsProtected(string? storedValue)
    {
        return storedValue?.StartsWith("encv2:", StringComparison.Ordinal) == true;
    }

    public void EnsureConfigured()
    {
    }
}
