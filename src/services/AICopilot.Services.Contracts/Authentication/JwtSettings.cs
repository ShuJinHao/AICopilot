namespace AICopilot.Services.Contracts.Authentication;

public record JwtSettings
{
    public const int MinimumSecretKeyLength = 64;

    public required string Issuer { get; set; }

    public required string Audience { get; set; }

    public required string SecretKey { get; set; }

    public int AccessTokenExpirationMinutes { get; set; } = 30;

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("JwtSettings:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("JwtSettings:Audience is required.");
        }

        if (string.IsNullOrWhiteSpace(SecretKey) || SecretKey.Length < MinimumSecretKeyLength)
        {
            throw new InvalidOperationException(
                $"JwtSettings:SecretKey must be at least {MinimumSecretKeyLength} characters.");
        }

        if (AccessTokenExpirationMinutes <= 0)
        {
            throw new InvalidOperationException("JwtSettings:AccessTokenExpirationMinutes must be greater than zero.");
        }
    }
}
