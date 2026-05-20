using System.Net;
using System.Security.Cryptography;
using System.Text;
using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.TrialOperations;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.TrialOperations;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlyPilotReadinessStatuses
{
    public const string NotConfigured = "NotConfigured";
    public const string CollectingEvidence = "CollectingEvidence";
    public const string RehearsalReady = "RehearsalReady";
    public const string RehearsalPassed = "RehearsalPassed";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
}

public sealed record CloudReadonlyPilotContractCheckSummaryDto(
    int Total,
    int Passed,
    int BlockedByPolicy,
    int Failed,
    DateTimeOffset? LastCheckedAt);

public sealed record CloudReadonlyPilotReadinessStatusDto(
    string Status,
    bool Enabled,
    string? EvidencePackageId,
    CloudReadonlyPilotConfigPackageDto? ConfigSummary,
    string ApprovalRehearsalStatus,
    CloudReadonlyPilotContractCheckSummaryDto ContractCheckSummary,
    IReadOnlyCollection<string> Blockers,
    IReadOnlyCollection<string> Warnings,
    DateTimeOffset? LastCheckedAt);

public sealed record CloudReadonlyPilotConfigPackageDto(
    string PackageId,
    IReadOnlyCollection<string> AllowedEndpointCodes,
    int MaxTimeRangeDays,
    int MaxRows,
    int TimeoutMs,
    string ApprovalPolicy,
    string RollbackPolicy,
    string OwnerDepartment,
    IReadOnlyCollection<string> EvidenceRefs,
    string Status);

public sealed record PilotApprovalRehearsalStepDto(
    string Code,
    string Label,
    string Status,
    bool IsBlocking,
    string AuditRef);

public sealed record PilotApprovalRehearsalDto(
    string RehearsalId,
    string PackageId,
    IReadOnlyCollection<PilotApprovalRehearsalStepDto> Steps,
    string Status,
    IReadOnlyCollection<string> Approvers,
    IReadOnlyCollection<string> AuditRefs,
    DateTimeOffset GeneratedAt);

public sealed record CloudReadonlyPilotContractRehearsalDto(
    string PackageId,
    string SourceMode,
    string Boundary,
    bool IsProductionData,
    IReadOnlyCollection<CloudAiReadEndpointCheckDto> Checks,
    IReadOnlyCollection<string> BlockedSamples,
    DateTimeOffset GeneratedAt);

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetCloudReadonlyPilotReadinessQuery
    : IQuery<Result<CloudReadonlyPilotReadinessStatusDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record CreateCloudReadonlyPilotConfigPackageCommand(
    Guid CampaignId,
    IReadOnlyCollection<string>? AllowedEndpointCodes = null,
    int? MaxTimeRangeDays = null,
    int? MaxRows = null,
    int? TimeoutMs = null,
    string? ApprovalPolicy = null,
    string? RollbackPolicy = null,
    string? OwnerDepartment = null) : ICommand<Result<CloudReadonlyPilotConfigPackageDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record RunCloudReadonlyPilotGateEvaluationCommand(
    Guid CampaignId) : ICommand<Result<CloudReadonlyPilotReadinessStatusDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record RunCloudReadonlyPilotApprovalRehearsalCommand(
    string PackageId) : ICommand<Result<PilotApprovalRehearsalDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record RunCloudReadonlyPilotContractRehearsalCommand(
    string PackageId,
    IReadOnlyCollection<string>? EndpointCodes = null,
    int? MaxRows = null,
    int? TimeoutMs = null) : ICommand<Result<CloudReadonlyPilotContractRehearsalDto>>;

public sealed class GetCloudReadonlyPilotReadinessQueryHandler(
    CloudReadonlyPilotReadinessService pilotReadinessService)
    : IQueryHandler<GetCloudReadonlyPilotReadinessQuery, Result<CloudReadonlyPilotReadinessStatusDto>>
{
    public Task<Result<CloudReadonlyPilotReadinessStatusDto>> Handle(
        GetCloudReadonlyPilotReadinessQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(pilotReadinessService.BuildStatus()));
    }
}

public sealed class CreateCloudReadonlyPilotConfigPackageCommandHandler(
    IReadRepository<TrialCampaign> campaignRepository,
    IReadRepository<ToolRegistration> toolRepository,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreateCloudReadonlyPilotConfigPackageCommand, Result<CloudReadonlyPilotConfigPackageDto>>
{
    public async Task<Result<CloudReadonlyPilotConfigPackageDto>> Handle(
        CreateCloudReadonlyPilotConfigPackageCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.FirstOrDefaultAsync(
            new TrialCampaignByIdSpec(new TrialCampaignId(request.CampaignId), includeDetails: true),
            cancellationToken);
        if (campaign is null)
        {
            return Result.NotFound();
        }

        var productionTool = await toolRepository.GetAsync(
            tool => tool.ToolCode == "query_cloud_data_readonly",
            cancellationToken: cancellationToken);
        var assessment = PilotReadinessEvaluator.Evaluate(campaign, productionTool);
        if (assessment.Status != PilotReadinessStatus.ReadyForP11Planning.ToString())
        {
            return Result.Invalid($"P10 evidence package is not ready for P11 planning. Status={assessment.Status}; blockers={string.Join("; ", assessment.Blockers)}");
        }

        var evidencePackage = TrialEvidencePackageBuilder.Build(campaign, assessment);
        var result = pilotReadinessService.CreatePackage(request, evidencePackage);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.CreateCloudReadonlyPilotConfigPackage",
                "CloudReadonlyPilotReadiness",
                result.Value.PackageId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Created P11 Pilot readiness config package; campaignId={campaign.Id.Value}; packageId={result.Value.PackageId}; endpoints={string.Join(",", result.Value.AllowedEndpointCodes)}.",
                ["campaignId", "packageId", "allowedEndpointCodes"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}

public sealed class RunCloudReadonlyPilotGateEvaluationCommandHandler(
    IReadRepository<TrialCampaign> campaignRepository,
    IReadRepository<ToolRegistration> toolRepository,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyPilotGateEvaluationCommand, Result<CloudReadonlyPilotReadinessStatusDto>>
{
    public async Task<Result<CloudReadonlyPilotReadinessStatusDto>> Handle(
        RunCloudReadonlyPilotGateEvaluationCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.FirstOrDefaultAsync(
            new TrialCampaignByIdSpec(new TrialCampaignId(request.CampaignId), includeDetails: true),
            cancellationToken);
        if (campaign is null)
        {
            return Result.NotFound();
        }

        var productionTool = await toolRepository.GetAsync(
            tool => tool.ToolCode == "query_cloud_data_readonly",
            cancellationToken: cancellationToken);
        var assessment = PilotReadinessEvaluator.Evaluate(campaign, productionTool);
        var status = pilotReadinessService.EvaluateGate(assessment);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunCloudReadonlyPilotGateEvaluation",
                "CloudReadonlyPilotReadiness",
                request.CampaignId.ToString(),
                status.Status,
                status.Status == CloudReadonlyPilotReadinessStatuses.Blocked ? AuditResults.Rejected : AuditResults.Succeeded,
                $"Ran P11 Pilot readiness gate; campaignId={request.CampaignId}; status={status.Status}; blockers={status.Blockers.Count}.",
                ["campaignId", "status", "blockers"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return Result.Success(status);
    }
}

public sealed class RunCloudReadonlyPilotApprovalRehearsalCommandHandler(
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyPilotApprovalRehearsalCommand, Result<PilotApprovalRehearsalDto>>
{
    public async Task<Result<PilotApprovalRehearsalDto>> Handle(
        RunCloudReadonlyPilotApprovalRehearsalCommand request,
        CancellationToken cancellationToken)
    {
        var result = pilotReadinessService.RunApprovalRehearsal(request.PackageId);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunCloudReadonlyPilotApprovalRehearsal",
                "CloudReadonlyPilotReadiness",
                result.Value.PackageId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Ran P11 Pilot approval rehearsal; packageId={result.Value.PackageId}; rehearsalId={result.Value.RehearsalId}; steps={result.Value.Steps.Count}.",
                ["packageId", "rehearsalId", "status"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}

public sealed class RunCloudReadonlyPilotContractRehearsalCommandHandler(
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyPilotContractRehearsalCommand, Result<CloudReadonlyPilotContractRehearsalDto>>
{
    public async Task<Result<CloudReadonlyPilotContractRehearsalDto>> Handle(
        RunCloudReadonlyPilotContractRehearsalCommand request,
        CancellationToken cancellationToken)
    {
        var result = pilotReadinessService.RunContractRehearsal(
            request.PackageId,
            request.EndpointCodes,
            request.MaxRows,
            request.TimeoutMs);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunCloudReadonlyPilotContractRehearsal",
                "CloudReadonlyPilotReadiness",
                result.Value.PackageId,
                CloudReadonlyPilotReadinessMarkers.Boundary,
                result.Value.Checks.Any(check => check.Status is "Failed" or "Timeout" or "SchemaMismatch")
                    ? AuditResults.Rejected
                    : AuditResults.Succeeded,
                $"Ran P11 fake production contract rehearsal; packageId={result.Value.PackageId}; checks={result.Value.Checks.Count}; blocked={result.Value.BlockedSamples.Count}.",
                ["packageId", "endpointCodes", "resultHash"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}

public interface ICloudReadonlyPilotReadinessStore
{
    void SavePackage(CloudReadonlyPilotConfigPackageDto package);

    CloudReadonlyPilotConfigPackageDto? GetPackage(string packageId);

    CloudReadonlyPilotConfigPackageDto? LatestPackage();

    void SaveApprovalRehearsal(PilotApprovalRehearsalDto rehearsal);

    PilotApprovalRehearsalDto? LatestApprovalRehearsal();

    void SaveContractRehearsal(CloudReadonlyPilotContractRehearsalDto rehearsal);

    CloudReadonlyPilotContractRehearsalDto? LatestContractRehearsal();
}

internal sealed class InMemoryCloudReadonlyPilotReadinessStore : ICloudReadonlyPilotReadinessStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, CloudReadonlyPilotConfigPackageDto> packages = new(StringComparer.OrdinalIgnoreCase);
    private PilotApprovalRehearsalDto? latestApprovalRehearsal;
    private CloudReadonlyPilotContractRehearsalDto? latestContractRehearsal;

    public void SavePackage(CloudReadonlyPilotConfigPackageDto package)
    {
        lock (sync)
        {
            packages[package.PackageId] = package;
            if (packages.Count <= 20)
            {
                return;
            }

            foreach (var key in packages.Keys.Take(packages.Count - 20).ToArray())
            {
                packages.Remove(key);
            }
        }
    }

    public CloudReadonlyPilotConfigPackageDto? GetPackage(string packageId)
    {
        lock (sync)
        {
            return string.IsNullOrWhiteSpace(packageId)
                ? null
                : packages.GetValueOrDefault(packageId);
        }
    }

    public CloudReadonlyPilotConfigPackageDto? LatestPackage()
    {
        lock (sync)
        {
            return packages.Values.LastOrDefault();
        }
    }

    public void SaveApprovalRehearsal(PilotApprovalRehearsalDto rehearsal)
    {
        lock (sync)
        {
            latestApprovalRehearsal = rehearsal;
        }
    }

    public PilotApprovalRehearsalDto? LatestApprovalRehearsal()
    {
        lock (sync)
        {
            return latestApprovalRehearsal;
        }
    }

    public void SaveContractRehearsal(CloudReadonlyPilotContractRehearsalDto rehearsal)
    {
        lock (sync)
        {
            latestContractRehearsal = rehearsal;
        }
    }

    public CloudReadonlyPilotContractRehearsalDto? LatestContractRehearsal()
    {
        lock (sync)
        {
            return latestContractRehearsal;
        }
    }
}

public sealed class CloudReadonlyPilotReadinessService(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    IOptions<CloudReadonlyPilotReadinessOptions> pilotOptions,
    ICloudReadonlyPilotReadinessStore store)
{
    private static readonly IReadOnlyDictionary<string, EndpointSpec> EndpointSpecs =
        new[]
        {
            new EndpointSpec("devices", HttpMethod.Get, "/api/v1/ai/read/devices", 3),
            new EndpointSpec("capacity_summary", HttpMethod.Get, "/api/v1/ai/read/capacity/summary", 2),
            new EndpointSpec("device_logs", HttpMethod.Get, "/api/v1/ai/read/device-logs", 4),
            new EndpointSpec("pass_station_records", HttpMethod.Get, "/api/v1/ai/read/pass-stations/default", 2),
            new EndpointSpec("recipe", HttpMethod.Get, "/api/v1/ai/read/recipes", 0, IsBlockedByPolicy: true),
            new EndpointSpec("recipe_versions", HttpMethod.Get, "/api/v1/ai/read/recipes/versions", 0, IsBlockedByPolicy: true),
            new EndpointSpec("write_path", HttpMethod.Post, "/api/v1/ai/read/devices/update", 0, IsBlockedByPolicy: true),
            new EndpointSpec("unknown_endpoint", HttpMethod.Get, "/api/v1/ai/read/unknown", 0, IsBlockedByPolicy: true),
            new EndpointSpec("timeout", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("http_500", HttpMethod.Get, "/api/v1/ai/read/devices", 0),
            new EndpointSpec("invalid_json", HttpMethod.Get, "/api/v1/ai/read/devices", 0)
        }.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

    public CloudReadonlyPilotReadinessStatusDto BuildStatus()
    {
        var package = store.LatestPackage();
        return BuildStatus(package, null);
    }

    public CloudReadonlyPilotReadinessStatusDto EvaluateGate(PilotReadinessAssessmentDto p10Assessment)
    {
        var package = store.LatestPackage();
        return BuildStatus(package, p10Assessment);
    }

    public Result<CloudReadonlyPilotConfigPackageDto> CreatePackage(
        CreateCloudReadonlyPilotConfigPackageCommand request,
        TrialEvidencePackageDto evidencePackage)
    {
        var status = BuildStatus();
        if (status.Status == CloudReadonlyPilotReadinessStatuses.Blocked)
        {
            return Result.Invalid($"CloudReadonlyPilotReadiness gate is blocked: {string.Join("; ", status.Blockers)}");
        }

        var options = pilotOptions.Value;
        var allowedEndpoints = NormalizeAllowedEndpointCodes(request.AllowedEndpointCodes ?? options.AllowedEndpointCodes);
        if (allowedEndpoints.Length == 0)
        {
            return Result.Invalid("CloudReadonlyPilotReadiness config package must contain at least one allowed endpoint.");
        }

        var maxTimeRangeDays = Math.Clamp(request.MaxTimeRangeDays ?? options.MaxTimeRangeDays, 1, options.MaxTimeRangeDays);
        var maxRows = Math.Clamp(request.MaxRows ?? options.MaxRows, 1, options.MaxRows);
        var timeoutMs = Math.Clamp(request.TimeoutMs ?? options.TimeoutMs, 500, options.TimeoutMs);
        var packageId = $"p11pkg_{ComputeHash($"{request.CampaignId}|{DateTimeOffset.UtcNow:O}|{string.Join(",", allowedEndpoints)}")[..20]}";
        var evidenceRefs = evidencePackage.EvidenceItems
            .Select(item => $"{item.EvidenceType}:{item.ReferenceId}:{string.Join(",", item.HashSamples.Take(2))}")
            .Concat([$"campaign:{request.CampaignId}", $"readiness:{evidencePackage.ReadinessStatus}"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        var package = new CloudReadonlyPilotConfigPackageDto(
            packageId,
            allowedEndpoints,
            maxTimeRangeDays,
            maxRows,
            timeoutMs,
            NormalizeText(request.ApprovalPolicy, options.ApprovalPolicy, 120),
            NormalizeText(request.RollbackPolicy, options.RollbackPolicy, 160),
            NormalizeText(request.OwnerDepartment, options.OwnerDepartment, 120),
            evidenceRefs,
            CloudReadonlyPilotReadinessStatuses.RehearsalReady);
        store.SavePackage(package);

        return Result.Success(package);
    }

    public Result<PilotApprovalRehearsalDto> RunApprovalRehearsal(string packageId)
    {
        var package = store.GetPackage(packageId);
        if (package is null)
        {
            return Result.Invalid("CloudReadonlyPilotReadiness config package was not found.");
        }

        var status = BuildStatus(package, null);
        if (status.Status == CloudReadonlyPilotReadinessStatuses.Blocked)
        {
            return Result.Invalid($"CloudReadonlyPilotReadiness approval rehearsal is blocked: {string.Join("; ", status.Blockers)}");
        }

        var now = DateTimeOffset.UtcNow;
        var steps = new[]
        {
            BuildApprovalStep("pilot_open_request", "生产 Pilot 开启申请", package.PackageId, now, isBlocking: true),
            BuildApprovalStep("data_boundary_confirm", "数据边界确认", package.PackageId, now, isBlocking: true),
            BuildApprovalStep("tool_approval", "工具审批演练", package.PackageId, now, isBlocking: true),
            BuildApprovalStep("final_output_approval", "final 审批演练", package.PackageId, now, isBlocking: true),
            BuildApprovalStep("emergency_disable", "紧急停用确认", package.PackageId, now, isBlocking: true)
        };
        var rehearsal = new PilotApprovalRehearsalDto(
            $"p11ar_{ComputeHash($"{package.PackageId}|{now:O}")[..20]}",
            package.PackageId,
            steps,
            "Passed",
            ["AI Platform Owner", "Data Boundary Owner", "Security Reviewer"],
            steps.Select(step => step.AuditRef).ToArray(),
            now);
        store.SaveApprovalRehearsal(rehearsal);

        return Result.Success(rehearsal);
    }

    public Result<CloudReadonlyPilotContractRehearsalDto> RunContractRehearsal(
        string packageId,
        IReadOnlyCollection<string>? endpointCodes,
        int? maxRows,
        int? timeoutMs)
    {
        var package = store.GetPackage(packageId);
        if (package is null)
        {
            return Result.Invalid("CloudReadonlyPilotReadiness config package was not found.");
        }

        var status = BuildStatus(package, null);
        if (status.Status == CloudReadonlyPilotReadinessStatuses.Blocked)
        {
            return Result.Invalid($"CloudReadonlyPilotReadiness contract rehearsal is blocked: {string.Join("; ", status.Blockers)}");
        }

        var requestedCodes = endpointCodes is { Count: > 0 }
            ? endpointCodes
            : package.AllowedEndpointCodes;
        var effectiveMaxRows = Math.Clamp(maxRows ?? package.MaxRows, 1, package.MaxRows);
        var effectiveTimeoutMs = Math.Clamp(timeoutMs ?? package.TimeoutMs, 500, package.TimeoutMs);
        var checks = requestedCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => BuildFakeContractCheck(
                ResolveEndpointSpec(code),
                package,
                effectiveMaxRows,
                effectiveTimeoutMs))
            .ToArray();
        var rehearsal = new CloudReadonlyPilotContractRehearsalDto(
            package.PackageId,
            CloudReadonlyPilotReadinessMarkers.SourceMode,
            CloudReadonlyPilotReadinessMarkers.Boundary,
            IsProductionData: false,
            checks,
            checks.Where(check => check.Status == "BlockedByPolicy")
                .Select(check => $"{check.EndpointCode}:{check.PolicyStatus}")
                .ToArray(),
            DateTimeOffset.UtcNow);
        store.SaveContractRehearsal(rehearsal);

        return Result.Success(rehearsal);
    }

    private CloudReadonlyPilotReadinessStatusDto BuildStatus(
        CloudReadonlyPilotConfigPackageDto? package,
        PilotReadinessAssessmentDto? p10Assessment)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        ValidateProductionBoundary(blockers, warnings);

        if (p10Assessment is not null)
        {
            if (p10Assessment.Status != PilotReadinessStatus.ReadyForP11Planning.ToString())
            {
                blockers.Add($"P10 evidence status is {p10Assessment.Status}; ReadyForP11Planning is required.");
            }

            blockers.AddRange(p10Assessment.Blockers);
            warnings.AddRange(p10Assessment.Warnings);
        }

        if (!pilotOptions.Value.Enabled)
        {
            warnings.Add("CloudReadonlyPilotReadiness.Enabled is false; P11 remains a disabled readiness rehearsal by default.");
        }

        var latestApproval = store.LatestApprovalRehearsal();
        var latestContract = store.LatestContractRehearsal();
        var contractSummary = BuildContractSummary(latestContract);
        var status = blockers.Count > 0
            ? CloudReadonlyPilotReadinessStatuses.Blocked
            : package is null
                ? CloudReadonlyPilotReadinessStatuses.NotConfigured
                : latestApproval?.Status == "Passed" &&
                  latestContract is not null &&
                  contractSummary.Failed == 0
                    ? CloudReadonlyPilotReadinessStatuses.RehearsalPassed
                    : package.Status == CloudReadonlyPilotReadinessStatuses.RehearsalReady
                        ? CloudReadonlyPilotReadinessStatuses.RehearsalReady
                        : CloudReadonlyPilotReadinessStatuses.CollectingEvidence;

        return new CloudReadonlyPilotReadinessStatusDto(
            status,
            pilotOptions.Value.Enabled,
            package?.EvidenceRefs.FirstOrDefault(item => item.StartsWith("campaign:", StringComparison.OrdinalIgnoreCase)),
            package,
            latestApproval?.Status ?? "NotRun",
            contractSummary,
            blockers,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            latestContract?.GeneratedAt ?? latestApproval?.GeneratedAt);
    }

    private void ValidateProductionBoundary(ICollection<string> blockers, ICollection<string> warnings)
    {
        var cloudReadonly = cloudReadonlyOptions.Value;
        var cloudAiRead = cloudAiReadOptions.Value;

        if (cloudReadonly.Mode != CloudReadonlyDataSourceMode.Disabled)
        {
            blockers.Add("CloudReadonly.Mode must remain Disabled during P11 Pilot readiness rehearsal.");
        }

        if (cloudReadonly.Real.Enabled)
        {
            blockers.Add("CloudReadonly.Real.Enabled must remain false during P11 Pilot readiness rehearsal.");
        }

        if (cloudReadonly.Real.AllowProductionRead)
        {
            blockers.Add("CloudReadonly.Real.AllowProductionRead must remain false during P11 Pilot readiness rehearsal.");
        }

        if (cloudAiRead.Enabled)
        {
            blockers.Add("CloudAiRead.Enabled must remain false during P11 Pilot readiness rehearsal.");
        }

        if (!string.IsNullOrWhiteSpace(cloudAiRead.ServiceAccountToken))
        {
            warnings.Add("CloudAiRead token is configured but P11 readiness APIs never return token values and never execute real production reads.");
        }

        var productionTool = BuiltInToolRegistrations.AgentRuntimeTools
            .FirstOrDefault(tool => tool.ToolCode == "query_cloud_data_readonly");
        if (productionTool is null)
        {
            blockers.Add("Tool Registry is missing query_cloud_data_readonly.");
        }
        else if (productionTool.IsEnabled || productionTool.IsVisibleToPlanner || productionTool.IsExecutableByAgent)
        {
            blockers.Add("query_cloud_data_readonly must remain disabled, hidden, and non-executable during P11.");
        }

        var pilotTool = BuiltInToolRegistrations.AgentRuntimeTools
            .FirstOrDefault(tool => tool.ToolCode == CloudReadonlyPilotReadinessMarkers.ToolCode);
        if (pilotTool is null)
        {
            blockers.Add($"Tool Registry is missing {CloudReadonlyPilotReadinessMarkers.ToolCode}.");
        }
        else if (pilotTool.IsEnabled || pilotTool.IsVisibleToPlanner || pilotTool.IsExecutableByAgent)
        {
            blockers.Add($"{CloudReadonlyPilotReadinessMarkers.ToolCode} must remain disabled, hidden, and non-executable.");
        }
        else if (pilotTool.DataBoundary != ToolDataBoundary.CloudReadonlyPilotReadinessOnly ||
                 pilotTool.ApprovalPolicy != "PilotReadinessRehearsalOnly")
        {
            blockers.Add($"{CloudReadonlyPilotReadinessMarkers.ToolCode} must use the PilotReadinessRehearsalOnly boundary descriptor.");
        }
    }

    private static CloudReadonlyPilotContractCheckSummaryDto BuildContractSummary(CloudReadonlyPilotContractRehearsalDto? rehearsal)
    {
        if (rehearsal is null)
        {
            return new CloudReadonlyPilotContractCheckSummaryDto(0, 0, 0, 0, null);
        }

        return new CloudReadonlyPilotContractCheckSummaryDto(
            rehearsal.Checks.Count,
            rehearsal.Checks.Count(check => check.Status == "Passed"),
            rehearsal.Checks.Count(check => check.Status == "BlockedByPolicy"),
            rehearsal.Checks.Count(check => check.Status is "Failed" or "Timeout" or "SchemaMismatch"),
            rehearsal.GeneratedAt);
    }

    private static PilotApprovalRehearsalStepDto BuildApprovalStep(
        string code,
        string label,
        string packageId,
        DateTimeOffset now,
        bool isBlocking)
    {
        var auditRef = $"audit:p11:{code}:{ComputeHash($"{packageId}|{code}|{now:O}")[..12]}";
        return new PilotApprovalRehearsalStepDto(code, label, "Passed", isBlocking, auditRef);
    }

    private static EndpointSpec ResolveEndpointSpec(string endpointCode)
    {
        var code = endpointCode.Trim();
        return EndpointSpecs.TryGetValue(code, out var spec)
            ? spec
            : new EndpointSpec(code, HttpMethod.Get, $"/api/v1/ai/read/{code}", 0, IsBlockedByPolicy: true);
    }

    private static CloudAiReadEndpointCheckDto BuildFakeContractCheck(
        EndpointSpec spec,
        CloudReadonlyPilotConfigPackageDto package,
        int maxRows,
        int timeoutMs)
    {
        var decision = CloudAiReadEndpointPolicy.Evaluate(spec.Method, spec.Path);
        var endpointAllowedByPackage = package.AllowedEndpointCodes.Contains(spec.Code, StringComparer.OrdinalIgnoreCase);
        if (spec.IsBlockedByPolicy || !decision.IsAllowed || !endpointAllowedByPackage)
        {
            return new CloudAiReadEndpointCheckDto(
                spec.Code,
                spec.Method.Method,
                spec.Path,
                "Blocked",
                (int)HttpStatusCode.Forbidden,
                1,
                0,
                false,
                null,
                CloudAiReadProblemCodes.RequestBlocked,
                "BlockedByPolicy");
        }

        var code = spec.Code.ToLowerInvariant();
        if (code is "timeout")
        {
            return FailedFakeCheck(spec, timeoutMs, null, CloudAiReadProblemCodes.Unavailable, "Timeout");
        }

        if (code is "http_500")
        {
            return FailedFakeCheck(spec, 2, (int)HttpStatusCode.InternalServerError, CloudAiReadProblemCodes.Unavailable, "Failed");
        }

        if (code is "invalid_json")
        {
            return FailedFakeCheck(spec, 2, (int)HttpStatusCode.OK, CloudAiReadProblemCodes.Unavailable, "SchemaMismatch");
        }

        var rows = Math.Min(maxRows, spec.FakeRows);
        var isTruncated = spec.FakeRows > maxRows;
        var hash = ComputeHash($"{package.PackageId}|{spec.Code}|{rows}|{isTruncated}|{CloudReadonlyPilotReadinessMarkers.Boundary}");
        return new CloudAiReadEndpointCheckDto(
            spec.Code,
            spec.Method.Method,
            spec.Path,
            "Allowed",
            (int)HttpStatusCode.OK,
            Math.Max(1, spec.Code.Length),
            rows,
            isTruncated,
            hash,
            null,
            "Passed");
    }

    private static CloudAiReadEndpointCheckDto FailedFakeCheck(
        EndpointSpec spec,
        long durationMs,
        int? httpStatus,
        string errorCode,
        string status)
    {
        return new CloudAiReadEndpointCheckDto(
            spec.Code,
            spec.Method.Method,
            spec.Path,
            "Allowed",
            httpStatus,
            durationMs,
            0,
            false,
            null,
            errorCode,
            status);
    }

    private static string[] NormalizeAllowedEndpointCodes(IEnumerable<string> endpointCodes)
    {
        return endpointCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Where(code =>
                EndpointSpecs.TryGetValue(code, out var spec) &&
                !spec.IsBlockedByPolicy &&
                CloudAiReadEndpointPolicy.IsSafeRouteSegment(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeText(string? value, string fallback, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed record EndpointSpec(
        string Code,
        HttpMethod Method,
        string Path,
        int FakeRows,
        bool IsBlockedByPolicy = false);
}
