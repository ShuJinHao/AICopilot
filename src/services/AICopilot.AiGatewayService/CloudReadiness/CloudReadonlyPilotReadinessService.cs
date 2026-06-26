using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.TrialOperations;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class CloudReadonlyPilotReadinessService(
    IOptions<CloudReadonlyOptions> cloudReadonlyOptions,
    IOptions<CloudAiReadOptions> cloudAiReadOptions,
    IOptions<CloudReadonlyPilotReadinessOptions> pilotOptions,
    ICloudReadonlyPilotReadinessStore store)
{
    public CloudReadonlyPilotReadinessStatusDto BuildStatus(
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations = null)
    {
        var package = store.LatestPackage();
        return BuildStatus(package, null, persistedToolRegistrations);
    }

    public CloudReadonlyPilotReadinessStatusDto EvaluateGate(
        PilotReadinessAssessmentDto p10Assessment,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations = null)
    {
        var package = store.LatestPackage();
        return BuildStatus(package, p10Assessment, persistedToolRegistrations);
    }

    public Result<CloudReadonlyPilotConfigPackageDto> CreatePackage(
        CreateCloudReadonlyPilotConfigPackageCommand request,
        TrialEvidencePackageDto evidencePackage,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations = null)
    {
        var status = BuildStatus(persistedToolRegistrations);
        if (status.Status == CloudReadonlyPilotReadinessStatuses.Blocked)
        {
            return Result.Invalid($"CloudReadonlyPilotReadiness gate is blocked: {string.Join("; ", status.Blockers)}");
        }

        var options = pilotOptions.Value;
        var allowedEndpoints = CloudReadonlyPilotReadinessPolicy.NormalizeAllowedEndpointCodes(
            request.AllowedEndpointCodes ?? options.AllowedEndpointCodes);
        if (allowedEndpoints.Length == 0)
        {
            return Result.Invalid("CloudReadonlyPilotReadiness config package must contain at least one allowed endpoint.");
        }

        var maxTimeRangeDays = Math.Clamp(request.MaxTimeRangeDays ?? options.MaxTimeRangeDays, 1, options.MaxTimeRangeDays);
        var maxRows = Math.Clamp(request.MaxRows ?? options.MaxRows, 1, options.MaxRows);
        var timeoutMs = Math.Clamp(request.TimeoutMs ?? options.TimeoutMs, 500, options.TimeoutMs);
        var packageId = $"p11pkg_{CloudReadonlyPilotReadinessContractRehearsal.ComputeHash($"{request.CampaignId}|{DateTimeOffset.UtcNow:O}|{string.Join(",", allowedEndpoints)}")[..20]}";
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
            CloudReadonlyPilotReadinessPolicy.NormalizeText(request.ApprovalPolicy, options.ApprovalPolicy, 120),
            CloudReadonlyPilotReadinessPolicy.NormalizeText(request.RollbackPolicy, options.RollbackPolicy, 160),
            CloudReadonlyPilotReadinessPolicy.NormalizeText(request.OwnerDepartment, options.OwnerDepartment, 120),
            evidenceRefs,
            CloudReadonlyPilotReadinessStatuses.RehearsalReady);
        store.SavePackage(package);

        return Result.Success(package);
    }

    public Result<PilotApprovalRehearsalDto> RunApprovalRehearsal(
        string packageId,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations = null)
    {
        var package = store.GetPackage(packageId);
        if (package is null)
        {
            return Result.Invalid("CloudReadonlyPilotReadiness config package was not found.");
        }

        var status = BuildStatus(package, null, persistedToolRegistrations);
        if (status.Status == CloudReadonlyPilotReadinessStatuses.Blocked)
        {
            return Result.Invalid($"CloudReadonlyPilotReadiness approval rehearsal is blocked: {string.Join("; ", status.Blockers)}");
        }

        var now = DateTimeOffset.UtcNow;
        var steps = new[]
        {
            CloudReadonlyPilotReadinessContractRehearsal.BuildApprovalStep("pilot_open_request", "生产 Pilot 开启申请", package.PackageId, now, isBlocking: true),
            CloudReadonlyPilotReadinessContractRehearsal.BuildApprovalStep("data_boundary_confirm", "数据边界确认", package.PackageId, now, isBlocking: true),
            CloudReadonlyPilotReadinessContractRehearsal.BuildApprovalStep("tool_approval", "工具审批演练", package.PackageId, now, isBlocking: true),
            CloudReadonlyPilotReadinessContractRehearsal.BuildApprovalStep("final_output_approval", "final 审批演练", package.PackageId, now, isBlocking: true),
            CloudReadonlyPilotReadinessContractRehearsal.BuildApprovalStep("emergency_disable", "紧急停用确认", package.PackageId, now, isBlocking: true)
        };
        var rehearsal = new PilotApprovalRehearsalDto(
            $"p11ar_{CloudReadonlyPilotReadinessContractRehearsal.ComputeHash($"{package.PackageId}|{now:O}")[..20]}",
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
        int? timeoutMs,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations = null)
    {
        var package = store.GetPackage(packageId);
        if (package is null)
        {
            return Result.Invalid("CloudReadonlyPilotReadiness config package was not found.");
        }

        var status = BuildStatus(package, null, persistedToolRegistrations);
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
            .Select(code => CloudReadonlyPilotReadinessContractRehearsal.BuildFakeContractCheck(
                code,
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
        PilotReadinessAssessmentDto? p10Assessment,
        IReadOnlyCollection<ToolRegistration>? persistedToolRegistrations)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        CloudReadonlyPilotReadinessPolicy.ValidateProductionBoundary(
            cloudReadonlyOptions.Value,
            cloudAiReadOptions.Value,
            blockers,
            warnings,
            persistedToolRegistrations);

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
        var contractSummary = CloudReadonlyPilotReadinessContractRehearsal.BuildContractSummary(latestContract);
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
}
