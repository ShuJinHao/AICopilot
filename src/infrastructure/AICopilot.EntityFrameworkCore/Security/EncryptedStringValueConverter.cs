using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AICopilot.EntityFrameworkCore.Security;

internal sealed class EncryptedStringValueConverter() : ValueConverter<string?, string?>(
    value => SecretStringEncryptor.Encrypt(value),
    value => SecretStringEncryptor.Decrypt(value))
{
}
