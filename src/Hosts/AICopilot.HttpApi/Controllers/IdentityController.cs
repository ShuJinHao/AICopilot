using AICopilot.HttpApi.Infrastructure;
using AICopilot.HttpApi.Models;
using AICopilot.IdentityService.Commands;
using AICopilot.IdentityService.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/identity")]
public class IdentityController(ISender sender) : ApiControllerBase(sender)
{
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(UserLoginRequest request)
    {
        var result = await Sender.Send(new LoginUserCommand(request.Username, request.Password));
        return ReturnResult(result);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUserProfile()
    {
        return ReturnResult(await Sender.Send(new GetCurrentUserProfileQuery()));
    }

    [Authorize]
    [HttpGet("permission/list")]
    public async Task<IActionResult> GetPermissions()
    {
        return ReturnResult(await Sender.Send(new GetListPermissionsQuery()));
    }

    [Authorize]
    [HttpGet("audit-log/list")]
    public async Task<IActionResult> GetAuditLogs([FromQuery] GetListAuditLogsQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [Authorize]
    [HttpGet("role/list")]
    public async Task<IActionResult> GetRoles()
    {
        return ReturnResult(await Sender.Send(new GetListRolesQuery()));
    }

    [Authorize]
    [EnableRateLimiting("identity-management")]
    [HttpPost("role")]
    public async Task<IActionResult> CreateRole(CreateRoleRequest request)
    {
        var result = await Sender.Send(new CreateRoleCommand(request.RoleName, request.Permissions));
        return ReturnResult(result);
    }

    [Authorize]
    [EnableRateLimiting("identity-management")]
    [HttpPut("role")]
    public async Task<IActionResult> UpdateRole(UpdateRoleRequest request)
    {
        return ReturnResult(await Sender.Send(new UpdateRoleCommand(request.RoleId, request.Permissions)));
    }

    [Authorize]
    [EnableRateLimiting("identity-management")]
    [HttpDelete("role")]
    public async Task<IActionResult> DeleteRole(DeleteRoleRequest request)
    {
        return ReturnResult(await Sender.Send(new DeleteRoleCommand(request.RoleId)));
    }

    [Authorize]
    [HttpGet("user/list")]
    public async Task<IActionResult> GetUsers()
    {
        return ReturnResult(await Sender.Send(new GetListUsersQuery()));
    }

    [Authorize]
    [EnableRateLimiting("identity-management")]
    [HttpPost("user")]
    public async Task<IActionResult> CreateUser(CreateUserRequest request)
    {
        var result = await Sender.Send(new CreateUserCommand(request.UserName, request.Password, request.RoleName));
        return ReturnResult(result);
    }

    [Authorize]
    [EnableRateLimiting("identity-management")]
    [HttpPut("user/role")]
    public async Task<IActionResult> UpdateUserRole(UpdateUserRoleRequest request)
    {
        return ReturnResult(await Sender.Send(new UpdateUserRoleCommand(request.UserId, request.RoleName)));
    }

    [Authorize]
    [EnableRateLimiting("identity-management")]
    [HttpPut("user/disable")]
    public async Task<IActionResult> DisableUser(DisableUserRequest request)
    {
        return ReturnResult(await Sender.Send(new DisableUserCommand(request.UserId)));
    }

    [Authorize]
    [EnableRateLimiting("identity-management")]
    [HttpPut("user/enable")]
    public async Task<IActionResult> EnableUser(EnableUserRequest request)
    {
        return ReturnResult(await Sender.Send(new EnableUserCommand(request.UserId)));
    }

    [Authorize]
    [EnableRateLimiting("identity-management")]
    [HttpPut("user/password/reset")]
    public async Task<IActionResult> ResetUserPassword(ResetUserPasswordRequest request)
    {
        return ReturnResult(await Sender.Send(new ResetUserPasswordCommand(request.UserId, request.NewPassword)));
    }

    [HttpGet("initialization-status")]
    public async Task<IActionResult> GetInitializationStatus()
    {
        return ReturnResult(await Sender.Send(new GetInitializationStatusQuery()));
    }
}
