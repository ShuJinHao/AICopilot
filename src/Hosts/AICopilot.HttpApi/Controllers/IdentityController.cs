using AICopilot.HttpApi.Infrastructure;
using AICopilot.HttpApi.Models;
using AICopilot.IdentityService.Commands;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/identity")]
public class IdentityController : ApiControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(UserRegisterRequest request)
    {
        var result = await Sender.Send(new CreateUserCommand(request.Username, request.Password));

        return ReturnResult(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(UserLoginRequest request)
    {
        var result = await Sender.Send(new LoginUserCommand(request.Username, request.Password));
        return ReturnResult(result);
    }

    [HttpPost("role")]
    public async Task<IActionResult> CreateRole(CreateRoleRequest request)
    {
        var result = await Sender.Send(new CreateRoleCommand(request.RoleName));
        return ReturnResult(result);
    }

    [HttpPost("test")]
    public IActionResult Test()
    {
        return Ok(new
        {
            IsAuthenticated = User.Identity?.IsAuthenticated,
            Username = User.FindFirstValue(ClaimTypes.Name)
        });
    }
}