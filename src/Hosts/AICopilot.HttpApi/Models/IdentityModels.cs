namespace AICopilot.HttpApi.Models;

public record UserLoginRequest(string Username, string Password);

public record CreateRoleRequest(string RoleName, IReadOnlyCollection<string> Permissions);

public record UpdateRoleRequest(string RoleId, IReadOnlyCollection<string> Permissions);

public record DeleteRoleRequest(string RoleId);

public record CreateUserRequest(string UserName, string Password, string RoleName);

public record UpdateUserRoleRequest(string UserId, string RoleName);

public record DisableUserRequest(string UserId);

public record EnableUserRequest(string UserId);

public record ResetUserPasswordRequest(string UserId, string NewPassword);
