namespace AICopilot.Services.Contracts;

public interface ICurrentUser
{
    Guid? Id { get; }

    string? UserName { get; }

    string? Role { get; }

    bool IsAuthenticated { get; }
}
