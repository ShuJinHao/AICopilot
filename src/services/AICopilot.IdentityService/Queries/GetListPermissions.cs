using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.IdentityService.Queries;

public record PermissionDefinitionDto(
    string Code,
    string Group,
    string DisplayName,
    string Description);

[AuthorizeRequirement("Identity.GetListPermissions")]
public record GetListPermissionsQuery : IQuery<Result<IReadOnlyCollection<PermissionDefinitionDto>>>;

public sealed class GetListPermissionsQueryHandler(IPermissionCatalog permissionCatalog)
    : IQueryHandler<GetListPermissionsQuery, Result<IReadOnlyCollection<PermissionDefinitionDto>>>
{
    public Task<Result<IReadOnlyCollection<PermissionDefinitionDto>>> Handle(
        GetListPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        var result = permissionCatalog.GetAll()
            .OrderBy(item => item.Group, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .Select(item => new PermissionDefinitionDto(
                item.Code,
                item.Group,
                item.DisplayName,
                item.Description))
            .ToArray();

        return Task.FromResult(Result.Success<IReadOnlyCollection<PermissionDefinitionDto>>(result));
    }
}
