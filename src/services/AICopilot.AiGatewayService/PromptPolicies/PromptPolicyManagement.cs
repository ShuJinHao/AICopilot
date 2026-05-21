using System.Security.Cryptography;
using System.Text;
using AICopilot.Core.AiGateway.Aggregates.PromptPolicy;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.PromptPolicy;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.PromptPolicies;

public sealed record PromptPolicyVersionDto(
    Guid Id,
    int VersionNo,
    bool IsEnabled,
    string SystemPrompt,
    string SafetyConstraints,
    string ContextInjectionRules,
    string OutputFormat,
    DateTimeOffset CreatedAt);

public sealed record PromptPolicyDto(
    Guid Id,
    string Code,
    string Name,
    PromptPolicyUsage Usage,
    bool IsEnabled,
    int? ActiveVersionNo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PromptPolicyVersionDto> Versions);

[AuthorizeRequirement("AiGateway.ManagePromptPolicy")]
public sealed record UpsertPromptPolicyCommand(
    string Code,
    string Name,
    PromptPolicyUsage Usage,
    string SystemPrompt,
    string SafetyConstraints,
    string ContextInjectionRules,
    string OutputFormat,
    bool IsEnabled = true,
    bool Activate = true) : ICommand<Result<PromptPolicyDto>>;

[AuthorizeRequirement("AiGateway.ManagePromptPolicy")]
public sealed record ActivatePromptPolicyVersionCommand(
    Guid Id,
    int VersionNo) : ICommand<Result<PromptPolicyDto>>;

[AuthorizeRequirement("AiGateway.GetPromptPolicy")]
public sealed record GetPromptPolicyQuery(
    Guid? Id = null,
    string? Code = null) : IQuery<Result<PromptPolicyDto>>;

[AuthorizeRequirement("AiGateway.GetPromptPolicy")]
public sealed record GetActivePromptPolicyQuery(
    PromptPolicyUsage Usage) : IQuery<Result<PromptPolicyDto>>;

[AuthorizeRequirement("AiGateway.GetPromptPolicy")]
public sealed record GetListPromptPoliciesQuery : IQuery<Result<IList<PromptPolicyDto>>>;

public interface IPromptPolicyProvider
{
    Task<PromptPolicyDto?> GetActiveAsync(
        PromptPolicyUsage usage,
        CancellationToken cancellationToken = default);
}

public sealed class PromptPolicyProvider(IReadRepository<PromptPolicy> repository) : IPromptPolicyProvider
{
    public async Task<PromptPolicyDto?> GetActiveAsync(
        PromptPolicyUsage usage,
        CancellationToken cancellationToken = default)
    {
        var policy = await repository.FirstOrDefaultAsync(
            new ActivePromptPolicyByUsageSpec(usage),
            cancellationToken);
        return policy is null ? null : PromptPolicyMapper.Map(policy);
    }
}

public sealed class UpsertPromptPolicyCommandHandler(
    IRepository<PromptPolicy> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpsertPromptPolicyCommand, Result<PromptPolicyDto>>
{
    public async Task<Result<PromptPolicyDto>> Handle(
        UpsertPromptPolicyCommand request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var policy = await repository.FirstOrDefaultAsync(
            new PromptPolicyByCodeSpec(request.Code.Trim()),
            cancellationToken);
        var created = false;
        if (policy is null)
        {
            policy = new PromptPolicy(request.Code, request.Name, request.Usage, request.IsEnabled, now);
            repository.Add(policy);
            created = true;
        }
        else
        {
            policy.UpdateHeader(request.Name, request.Usage, request.IsEnabled, now);
            repository.Update(policy);
        }

        var version = policy.AddVersion(
            request.SystemPrompt,
            request.SafetyConstraints,
            request.ContextInjectionRules,
            request.OutputFormat,
            request.Activate,
            now);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                created ? "AiGateway.CreatePromptPolicy" : "AiGateway.UpdatePromptPolicy",
                "PromptPolicy",
                policy.Id.ToString(),
                policy.Code,
                AuditResults.Succeeded,
                $"Prompt policy {(created ? "created" : "updated")}. code={policy.Code}; usage={policy.Usage}; version={version.VersionNo}; active={policy.ActiveVersionNo}.",
                ["code", "name", "usage", "isEnabled", "version"],
                new Dictionary<string, string>
                {
                    ["systemPromptHash"] = ComputeHash(request.SystemPrompt),
                    ["safetyConstraintsHash"] = ComputeHash(request.SafetyConstraints),
                    ["contextInjectionRulesHash"] = ComputeHash(request.ContextInjectionRules),
                    ["outputFormatHash"] = ComputeHash(request.OutputFormat),
                    ["versionNo"] = version.VersionNo.ToString()
                }),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(PromptPolicyMapper.Map(policy));
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))
            .ToLowerInvariant();
    }
}

public sealed class ActivatePromptPolicyVersionCommandHandler(
    IRepository<PromptPolicy> repository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<ActivatePromptPolicyVersionCommand, Result<PromptPolicyDto>>
{
    public async Task<Result<PromptPolicyDto>> Handle(
        ActivatePromptPolicyVersionCommand request,
        CancellationToken cancellationToken)
    {
        var policy = await repository.FirstOrDefaultAsync(
            new PromptPolicyByIdSpec(new PromptPolicyId(request.Id)),
            cancellationToken);
        if (policy is null)
        {
            return Result.NotFound();
        }

        policy.ActivateVersion(request.VersionNo, DateTimeOffset.UtcNow);
        repository.Update(policy);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Config,
                "AiGateway.ActivatePromptPolicyVersion",
                "PromptPolicy",
                policy.Id.ToString(),
                policy.Code,
                AuditResults.Succeeded,
                $"Activated prompt policy version. code={policy.Code}; version={request.VersionNo}.",
                ["activeVersionNo"],
                new Dictionary<string, string> { ["versionNo"] = request.VersionNo.ToString() }),
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(PromptPolicyMapper.Map(policy));
    }
}

public sealed class GetPromptPolicyQueryHandler(IReadRepository<PromptPolicy> repository)
    : IQueryHandler<GetPromptPolicyQuery, Result<PromptPolicyDto>>
{
    public async Task<Result<PromptPolicyDto>> Handle(
        GetPromptPolicyQuery request,
        CancellationToken cancellationToken)
    {
        PromptPolicy? policy = null;
        if (request.Id.HasValue)
        {
            policy = await repository.FirstOrDefaultAsync(
                new PromptPolicyByIdSpec(new PromptPolicyId(request.Id.Value)),
                cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.Code))
        {
            policy = await repository.FirstOrDefaultAsync(
                new PromptPolicyByCodeSpec(request.Code.Trim()),
                cancellationToken);
        }

        return policy is null ? Result.NotFound() : Result.Success(PromptPolicyMapper.Map(policy));
    }
}

public sealed class GetActivePromptPolicyQueryHandler(IPromptPolicyProvider provider)
    : IQueryHandler<GetActivePromptPolicyQuery, Result<PromptPolicyDto>>
{
    public async Task<Result<PromptPolicyDto>> Handle(
        GetActivePromptPolicyQuery request,
        CancellationToken cancellationToken)
    {
        var policy = await provider.GetActiveAsync(request.Usage, cancellationToken);
        return policy is null ? Result.NotFound() : Result.Success(policy);
    }
}

public sealed class GetListPromptPoliciesQueryHandler(IReadRepository<PromptPolicy> repository)
    : IQueryHandler<GetListPromptPoliciesQuery, Result<IList<PromptPolicyDto>>>
{
    public async Task<Result<IList<PromptPolicyDto>>> Handle(
        GetListPromptPoliciesQuery request,
        CancellationToken cancellationToken)
    {
        var policies = await repository.ListAsync(new PromptPoliciesOrderedSpec(), cancellationToken);
        IList<PromptPolicyDto> result = policies.Select(PromptPolicyMapper.Map).ToList();
        return Result.Success(result);
    }
}

internal static class PromptPolicyMapper
{
    public static PromptPolicyDto Map(PromptPolicy policy)
    {
        return new PromptPolicyDto(
            policy.Id,
            policy.Code,
            policy.Name,
            policy.Usage,
            policy.IsEnabled,
            policy.ActiveVersionNo,
            policy.CreatedAt,
            policy.UpdatedAt,
            policy.Versions
                .OrderByDescending(version => version.VersionNo)
                .Select(version => new PromptPolicyVersionDto(
                    version.Id,
                    version.VersionNo,
                    version.IsEnabled,
                    version.SystemPrompt,
                    version.SafetyConstraints,
                    version.ContextInjectionRules,
                    version.OutputFormat,
                    version.CreatedAt))
                .ToArray());
    }
}
