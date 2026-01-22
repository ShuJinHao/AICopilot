using AICopilot.Services.Common.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.IdentityService.Commands;

public record CreatedRoleDto(string Id, string RoleName);

[AuthorizeRequirement("Identity.CreateRole")]
public record CreateRoleCommand(string RoleName) : ICommand<Result<CreatedRoleDto>>;

public class CreateRoleCommandHandler(
    RoleManager<IdentityRole> roleManager) : ICommandHandler<CreateRoleCommand, Result<CreatedRoleDto>>
{
    public async Task<Result<CreatedRoleDto>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var role = new IdentityRole
        {
            Name = command.RoleName
        };

        var result = await roleManager.CreateAsync(role);

        return !result.Succeeded ? Result.Failure(result.Errors) : Result.Success(new CreatedRoleDto(role.Id, role.Name));
    }
}