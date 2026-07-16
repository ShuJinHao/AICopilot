using AICopilot.HttpApi.Infrastructure;
using AICopilot.HttpApi.Models;
using AICopilot.IdentityService.Commands;
using AICopilot.IdentityService.Queries;
using AICopilot.SharedKernel.Result;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/identity")]
public class IdentityController(
    ISender sender,
    IAuthenticationSchemeProvider authenticationSchemeProvider,
    IOptions<CloudOidcOptions> cloudOidcOptions) : ApiControllerBase(sender)
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(UserLoginRequest request)
    {
        var result = await Sender.Send(new LoginUserCommand(request.Username, request.Password));
        return ReturnResult(result);
    }

    [HttpGet("cloud-oidc/status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCloudOidcStatus()
    {
        return Ok(new CloudOidcStatusResponse(await IsCloudOidcEnabledAsync()));
    }

    [HttpGet("cloud-oidc/challenge")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> CloudOidcChallenge()
    {
        if (!await IsCloudOidcEnabledAsync())
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiProblemDetailsFactory.Create(
                    StatusCodes.Status503ServiceUnavailable,
                    new ApiProblemDescriptor(
                        AuthProblemCodes.CloudOidcNotConfigured,
                        "Cloud OIDC 登录尚未配置。"),
                    traceIdentifier: HttpContext.TraceIdentifier));
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = cloudOidcOptions.Value.FrontendCompletionPath
        };

        return Challenge(properties, CloudOidcAuthenticationDefaults.AuthenticationScheme);
    }

    [HttpPost("cloud-oidc/finalize")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> FinalizeCloudOidcLogin()
    {
        var result = await CloudOidcFinalizationWorkflow.ExecuteAsync(
            async _ =>
            {
                var authentication = await HttpContext.AuthenticateAsync(
                    CloudOidcAuthenticationDefaults.ExternalCookieScheme);
                return authentication.Succeeded ? authentication.Principal : null;
            },
            cloudOidcOptions.Value.Issuer,
            (profile, cancellationToken) => Sender.Send(
                new FinalizeCloudOidcLoginCommand(profile),
                cancellationToken),
            _ => HttpContext.SignOutAsync(CloudOidcAuthenticationDefaults.ExternalCookieScheme),
            HttpContext.RequestAborted);

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
    [AllowAnonymous]
    public async Task<IActionResult> GetInitializationStatus()
    {
        return ReturnResult(await Sender.Send(new GetInitializationStatusQuery()));
    }

    private async Task<bool> IsCloudOidcEnabledAsync()
    {
        return cloudOidcOptions.Value.IsConfigured()
            && await authenticationSchemeProvider.GetSchemeAsync(CloudOidcAuthenticationDefaults.AuthenticationScheme) is not null;
    }
}
