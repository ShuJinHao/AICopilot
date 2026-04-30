using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Specifications.ApprovalPolicy;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.ApprovalPolicies;

public record ApprovalPolicyDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required ApprovalTargetType TargetType { get; init; }
    public required string TargetName { get; init; }
    public required IReadOnlyCollection<string> ToolNames { get; init; }
    public bool IsEnabled { get; init; }
    public bool RequiresOnsiteAttestation { get; init; }
}

public record CreatedApprovalPolicyDto(Guid Id, string Name);

[AuthorizeRequirement("AiGateway.CreateApprovalPolicy")]
public record CreateApprovalPolicyCommand(
    string Name,
    string? Description,
    ApprovalTargetType TargetType,
    string TargetName,
    IReadOnlyCollection<string> ToolNames,
    bool IsEnabled = true,
    bool RequiresOnsiteAttestation = false) : ICommand<Result<CreatedApprovalPolicyDto>>;

public class CreateApprovalPolicyCommandHandler(
    IRepository<ApprovalPolicy> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreateApprovalPolicyCommand, Result<CreatedApprovalPolicyDto>>
{
    public async Task<Result<CreatedApprovalPolicyDto>> Handle(
        CreateApprovalPolicyCommand request,
        CancellationToken cancellationToken)
    {
        var entity = new ApprovalPolicy(
            request.Name,
            request.Description,
            request.TargetType,
            request.TargetName,
            request.ToolNames,
            request.IsEnabled,
            request.RequiresOnsiteAttestation);

        repository.Add(entity);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.CreateApprovalPolicy",
                "ApprovalPolicy",
                entity.Id.ToString(),
                entity.Name,
                AuditResults.Succeeded,
                $"Created approval policy: {entity.Name}; target={entity.TargetType}/{entity.TargetName}.",
                ["name", "description", "targetType", "targetName", "toolNames", "isEnabled", "requiresOnsiteAttestation"]),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreatedApprovalPolicyDto(entity.Id, entity.Name));
    }
}

[AuthorizeRequirement("AiGateway.UpdateApprovalPolicy")]
public record UpdateApprovalPolicyCommand(
    Guid Id,
    string Name,
    string? Description,
    ApprovalTargetType TargetType,
    string TargetName,
    IReadOnlyCollection<string> ToolNames,
    bool IsEnabled,
    bool RequiresOnsiteAttestation) : ICommand<Result>;

public class UpdateApprovalPolicyCommandHandler(
    IRepository<ApprovalPolicy> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdateApprovalPolicyCommand, Result>
{
    public async Task<Result> Handle(UpdateApprovalPolicyCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity == null)
        {
            return Result.NotFound();
        }

        var changedFields = new List<string>();
        if (!string.Equals(entity.Name, request.Name, StringComparison.Ordinal))
        {
            changedFields.Add("name");
        }

        if (!string.Equals(entity.Description, request.Description, StringComparison.Ordinal))
        {
            changedFields.Add("description");
        }

        if (entity.TargetType != request.TargetType)
        {
            changedFields.Add("targetType");
        }

        if (!string.Equals(entity.TargetName, request.TargetName, StringComparison.Ordinal))
        {
            changedFields.Add("targetName");
        }

        if (entity.IsEnabled != request.IsEnabled)
        {
            changedFields.Add("isEnabled");
        }

        if (entity.RequiresOnsiteAttestation != request.RequiresOnsiteAttestation)
        {
            changedFields.Add("requiresOnsiteAttestation");
        }

        if (!new HashSet<string>(entity.ToolNames, StringComparer.OrdinalIgnoreCase)
                .SetEquals(request.ToolNames.Where(item => !string.IsNullOrWhiteSpace(item))))
        {
            changedFields.Add("toolNames");
        }

        entity.Update(
            request.Name,
            request.Description,
            request.TargetType,
            request.TargetName,
            request.ToolNames,
            request.IsEnabled,
            request.RequiresOnsiteAttestation);

        repository.Update(entity);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.UpdateApprovalPolicy",
                "ApprovalPolicy",
                entity.Id.ToString(),
                entity.Name,
                AuditResults.Succeeded,
                $"Updated approval policy: {entity.Name}; changed={(changedFields.Count == 0 ? "none" : string.Join(", ", changedFields))}.",
                changedFields),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

[AuthorizeRequirement("AiGateway.DeleteApprovalPolicy")]
public record DeleteApprovalPolicyCommand(Guid Id) : ICommand<Result>;

public class DeleteApprovalPolicyCommandHandler(
    IRepository<ApprovalPolicy> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<DeleteApprovalPolicyCommand, Result>
{
    public async Task<Result> Handle(DeleteApprovalPolicyCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity == null)
        {
            return Result.Success();
        }

        var targetName = entity.Name;

        repository.Delete(entity);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.DeleteApprovalPolicy",
                "ApprovalPolicy",
                request.Id.ToString(),
                targetName,
                AuditResults.Succeeded,
                $"Deleted approval policy: {targetName}."),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

[AuthorizeRequirement("AiGateway.GetApprovalPolicy")]
public record GetApprovalPolicyQuery(Guid Id) : IQuery<Result<ApprovalPolicyDto>>;

public class GetApprovalPolicyQueryHandler(IReadRepository<ApprovalPolicy> repository)
    : IQueryHandler<GetApprovalPolicyQuery, Result<ApprovalPolicyDto>>
{
    public async Task<Result<ApprovalPolicyDto>> Handle(GetApprovalPolicyQuery request, CancellationToken cancellationToken)
    {
        var result = await repository.FirstOrDefaultAsync(new ApprovalPolicyByIdSpec(request.Id), cancellationToken);
        return result == null ? Result.NotFound() : Result.Success(ApprovalPolicyDtoMapper.Map(result));
    }
}

[AuthorizeRequirement("AiGateway.GetListApprovalPolicies")]
public record GetListApprovalPoliciesQuery : IQuery<Result<IList<ApprovalPolicyDto>>>;

public class GetListApprovalPoliciesQueryHandler(IReadRepository<ApprovalPolicy> repository)
    : IQueryHandler<GetListApprovalPoliciesQuery, Result<IList<ApprovalPolicyDto>>>
{
    public async Task<Result<IList<ApprovalPolicyDto>>> Handle(
        GetListApprovalPoliciesQuery request,
        CancellationToken cancellationToken)
    {
        var policies = await repository.ListAsync(new ApprovalPoliciesOrderedSpec(), cancellationToken);
        IList<ApprovalPolicyDto> result = policies.Select(ApprovalPolicyDtoMapper.Map).ToList();
        return Result.Success(result);
    }
}

internal static class ApprovalPolicyDtoMapper
{
    public static ApprovalPolicyDto Map(ApprovalPolicy policy)
    {
        return new ApprovalPolicyDto
        {
            Id = policy.Id,
            Name = policy.Name,
            Description = policy.Description,
            TargetType = policy.TargetType,
            TargetName = policy.TargetName,
            ToolNames = policy.ToolNames.ToArray(),
            IsEnabled = policy.IsEnabled,
            RequiresOnsiteAttestation = policy.RequiresOnsiteAttestation
        };
    }
}
