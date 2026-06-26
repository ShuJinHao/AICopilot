using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public static class CloudReadonlyReadinessModes
{
    public const string DryRun = "DryRun";
    public const string FakeEndpoint = "FakeEndpoint";
    public const string RealSandboxSmoke = "RealSandboxSmoke";
}

public static class CloudReadonlyReadinessStatuses
{
    public const string NotConfigured = "NotConfigured";
    public const string ReadyForFake = "ReadyForFake";
    public const string FakePassed = "FakePassed";
    public const string RealSandboxPending = "RealSandboxPending";
    public const string RealSandboxPassed = "RealSandboxPassed";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
}

public sealed record CloudAiReadEndpointCheckDto(
    string EndpointCode,
    string Method,
    string Path,
    string PolicyStatus,
    int? HttpStatus,
    long DurationMs,
    int RowCount,
    bool IsTruncated,
    string? ResultHash,
    string? ErrorCode,
    string Status);

public sealed record CloudReadonlySandboxStatusDto(
    string Status,
    bool SandboxEnabled,
    bool BaseUrlConfigured,
    bool TokenConfigured,
    DateTimeOffset? LastSmokeAt,
    IReadOnlyCollection<CloudAiReadEndpointCheckDto> Checks,
    IReadOnlyCollection<string> Errors,
    IReadOnlyCollection<string> Warnings,
    string Boundary = "SandboxSmokeOnly");

public sealed record CloudReadonlyReadinessDto(
    string Status,
    string Mode,
    bool CloudAiReadEnabled,
    bool RealEnabled,
    bool AllowProductionRead,
    bool BaseUrlConfigured,
    bool TokenConfigured,
    DateTimeOffset? LastCheckedAt,
    IReadOnlyCollection<CloudAiReadEndpointCheckDto> Checks,
    IReadOnlyCollection<string> Errors,
    IReadOnlyCollection<string> Warnings,
    string Boundary = "ReadinessOnly",
    CloudReadonlySandboxStatusDto? SandboxStatus = null);

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlyReadinessQuery : IQuery<Result<CloudReadonlyReadinessDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlyReadinessHistoryQuery : IQuery<Result<IReadOnlyCollection<CloudReadonlyReadinessDto>>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlySandboxStatusQuery : IQuery<Result<CloudReadonlySandboxStatusDto>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetCloudReadonlySandboxSmokeHistoryQuery : IQuery<Result<IReadOnlyCollection<CloudReadonlySandboxStatusDto>>>;

[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record RunCloudReadonlyReadinessCheckCommand(
    string Mode = CloudReadonlyReadinessModes.FakeEndpoint,
    IReadOnlyCollection<string>? EndpointCodes = null,
    int MaxRows = 20,
    int TimeoutMs = 5000) : ICommand<Result<CloudReadonlyReadinessDto>>;

public interface ICloudReadonlyReadinessHistoryStore
{
    void Save(CloudReadonlyReadinessDto report);

    IReadOnlyCollection<CloudReadonlyReadinessDto> List();
}
