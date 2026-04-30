namespace AICopilot.Services.Contracts;

public interface ISqlGuardrail
{
    (bool IsSafe, string? ErrorMessage) Validate(string sql, DatabaseProviderType provider);
}
