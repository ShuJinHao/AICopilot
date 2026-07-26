using AICopilot.HttpApi.Infrastructure;
using AICopilot.HttpApi.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/system")]
public sealed class SystemController(
    ISender sender,
    IConfiguration configuration) : ApiControllerBase(sender)
{
    [HttpGet("build-identity")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(SystemBuildIdentityResponse), StatusCodes.Status200OK)]
    public IActionResult GetBuildIdentity()
    {
        return Ok(SystemBuildIdentityResolver.Resolve(configuration));
    }
}
