namespace AICopilot.HttpApi.Models;

public sealed record SystemBuildIdentityResponse(
    string SchemaVersion,
    string ServiceName,
    string? ReleaseTag,
    string? SourceCommit,
    bool Available);
