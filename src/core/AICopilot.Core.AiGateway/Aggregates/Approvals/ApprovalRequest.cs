using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.Approvals;

public enum AgentApprovalType
{
    Plan = 0,
    ToolCall = 1,
    Artifact = 2,
    FinalOutput = 3
}

public sealed class ApprovalRequest : BaseEntity<ApprovalRequestId>, IAggregateRoot<ApprovalRequestId>
{
    public const string FinalOutputProofVersionV1 = "final-output-approval-v1";
    public const string FinalOutputDecisionProofVersionV1 = "final-output-decision-v1";

    private ApprovalRequest()
    {
    }

    public ApprovalRequest(
        AgentTaskId taskId,
        AgentApprovalType approvalType,
        string targetId,
        Guid requestedBy,
        DateTimeOffset nowUtc)
    {
        if (approvalType == AgentApprovalType.FinalOutput)
        {
            throw new ArgumentException(
                "Final-output approvals require an immutable approval proof.",
                nameof(approvalType));
        }

        Initialize(taskId, approvalType, targetId, requestedBy, nowUtc);
    }

    private void Initialize(
        AgentTaskId taskId,
        AgentApprovalType approvalType,
        string targetId,
        Guid requestedBy,
        DateTimeOffset nowUtc)
    {
        if (requestedBy == Guid.Empty)
        {
            throw new ArgumentException("Approval requester id is required.", nameof(requestedBy));
        }

        Id = ApprovalRequestId.New();
        TaskId = taskId;
        ApprovalType = approvalType;
        TargetId = NormalizeRequired(targetId, nameof(targetId), 200);
        Status = AgentApprovalStatus.Pending;
        RequestedBy = requestedBy;
        CreatedAt = nowUtc;
    }

    public static ApprovalRequest CreateFinalOutput(
        AgentTaskId taskId,
        Guid requestedBy,
        DateTimeOffset nowUtc,
        FinalOutputApprovalProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        proof.Validate();

        var approval = new ApprovalRequest();
        approval.Initialize(
            taskId,
            AgentApprovalType.FinalOutput,
            proof.WorkspaceCode,
            requestedBy,
            nowUtc);
        approval.FinalOutputProofVersion = FinalOutputProofVersionV1;
        approval.FinalOutputWorkspaceId = proof.WorkspaceId;
        approval.FinalOutputFinalStepId = proof.FinalStepId;
        approval.FinalOutputRunAttemptId = proof.ActiveRunAttemptId;
        approval.FinalOutputNodeRunId = proof.FinalNodeRunId;
        approval.FinalOutputTaskFencingToken = proof.TaskFencingToken;
        approval.FinalOutputNodeFencingToken = proof.NodeFencingToken;
        approval.FinalOutputEvidenceSetDigest = proof.EvidenceSetDigest;
        approval.FinalOutputManifestDigest = proof.ManifestDigest;
        approval.FinalOutputArtifactBindingsJson = proof.ArtifactBindingsJson;
        approval.FinalOutputArtifactBindingDigest = proof.ArtifactBindingDigest;
        approval.FinalOutputProofDigest = approval.ComputeFinalOutputProofDigest();
        return approval;
    }

    public AgentTaskId TaskId { get; private set; }

    public AgentApprovalType ApprovalType { get; private set; }

    public string TargetId { get; private set; } = string.Empty;

    public AgentApprovalStatus Status { get; private set; }

    public Guid RequestedBy { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public string? ApprovalComment { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public string? FinalOutputProofVersion { get; private set; }

    public ArtifactWorkspaceId? FinalOutputWorkspaceId { get; private set; }

    public AgentStepId? FinalOutputFinalStepId { get; private set; }

    public AgentTaskRunAttemptId? FinalOutputRunAttemptId { get; private set; }

    public AgentNodeRunId? FinalOutputNodeRunId { get; private set; }

    public long? FinalOutputTaskFencingToken { get; private set; }

    public long? FinalOutputNodeFencingToken { get; private set; }

    public string? FinalOutputEvidenceSetDigest { get; private set; }

    public string? FinalOutputManifestDigest { get; private set; }

    public string? FinalOutputArtifactBindingsJson { get; private set; }

    public string? FinalOutputArtifactBindingDigest { get; private set; }

    public string? FinalOutputProofDigest { get; private set; }

    public string? FinalOutputDecisionProofDigest { get; private set; }

    public bool HasValidFinalOutputProof()
    {
        if (ApprovalType != AgentApprovalType.FinalOutput)
        {
            return false;
        }

        try
        {
            var proof = GetFinalOutputProof();
            proof.Validate();
            return string.Equals(TargetId, proof.WorkspaceCode, StringComparison.Ordinal) &&
                   string.Equals(FinalOutputProofVersion, FinalOutputProofVersionV1, StringComparison.Ordinal) &&
                   IsSha256(FinalOutputProofDigest) &&
                   string.Equals(
                       FinalOutputProofDigest,
                       ComputeFinalOutputProofDigest(),
                       StringComparison.Ordinal);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public bool MatchesFinalOutputProof(FinalOutputApprovalProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        return HasValidFinalOutputProof() &&
               GetFinalOutputProof() == proof;
    }

    public bool HasValidFinalOutputDecisionProof()
    {
        if (ApprovalType != AgentApprovalType.FinalOutput ||
            Status is not (AgentApprovalStatus.Approved or AgentApprovalStatus.Rejected) ||
            ApprovedBy is null ||
            ApprovedBy == Guid.Empty ||
            ApprovedAt is null ||
            ApprovedAt < CreatedAt ||
            !HasValidFinalOutputProof() ||
            !IsSha256(FinalOutputDecisionProofDigest))
        {
            return false;
        }

        return string.Equals(
            FinalOutputDecisionProofDigest,
            ComputeFinalOutputDecisionProofDigest(),
            StringComparison.Ordinal);
    }

    public FinalOutputApprovalProof GetFinalOutputProof()
    {
        if (ApprovalType != AgentApprovalType.FinalOutput ||
            FinalOutputWorkspaceId is null ||
            FinalOutputFinalStepId is null ||
            FinalOutputRunAttemptId is null ||
            FinalOutputNodeRunId is null ||
            FinalOutputTaskFencingToken is null ||
            FinalOutputNodeFencingToken is null ||
            FinalOutputEvidenceSetDigest is null ||
            FinalOutputManifestDigest is null ||
            FinalOutputArtifactBindingsJson is null ||
            FinalOutputArtifactBindingDigest is null)
        {
            throw new InvalidOperationException(
                "Final-output approval proof is missing or incomplete.");
        }

        return new FinalOutputApprovalProof(
            FinalOutputWorkspaceId.Value,
            TargetId,
            FinalOutputFinalStepId.Value,
            FinalOutputRunAttemptId.Value,
            FinalOutputNodeRunId.Value,
            FinalOutputTaskFencingToken.Value,
            FinalOutputNodeFencingToken.Value,
            FinalOutputEvidenceSetDigest,
            FinalOutputManifestDigest,
            AgentCanonicalJsonV1.Canonicalize(FinalOutputArtifactBindingsJson),
            FinalOutputArtifactBindingDigest);
    }

    public void Approve(Guid approvedBy, string? comment, DateTimeOffset nowUtc)
    {
        Complete(AgentApprovalStatus.Approved, approvedBy, comment, nowUtc);
    }

    public void Reject(Guid approvedBy, string? comment, DateTimeOffset nowUtc)
    {
        Complete(AgentApprovalStatus.Rejected, approvedBy, comment, nowUtc);
    }

    public void Cancel(DateTimeOffset nowUtc)
    {
        if (Status != AgentApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Only pending approval requests can be cancelled.");
        }

        Status = AgentApprovalStatus.Cancelled;
        ApprovedAt = nowUtc;
    }

    public void Expire(DateTimeOffset nowUtc)
    {
        if (Status != AgentApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Only pending approval requests can be expired.");
        }

        Status = AgentApprovalStatus.Expired;
        ApprovedAt = nowUtc;
    }

    private void Complete(AgentApprovalStatus status, Guid approvedBy, string? comment, DateTimeOffset nowUtc)
    {
        if (Status != AgentApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Only pending approval requests can be completed.");
        }

        if (approvedBy == Guid.Empty)
        {
            throw new ArgumentException("Approval operator id is required.", nameof(approvedBy));
        }

        if (nowUtc < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowUtc),
                "Approval decision time cannot precede approval creation.");
        }

        if (ApprovalType == AgentApprovalType.FinalOutput && !HasValidFinalOutputProof())
        {
            throw new InvalidOperationException(
                "Final-output approval cannot be decided without a valid immutable proof.");
        }

        Status = status;
        ApprovedBy = approvedBy;
        ApprovalComment = NormalizeOptional(comment, 2000);
        ApprovedAt = nowUtc;
        if (ApprovalType == AgentApprovalType.FinalOutput)
        {
            FinalOutputDecisionProofDigest = ComputeFinalOutputDecisionProofDigest();
        }
    }

    private string ComputeFinalOutputProofDigest()
    {
        var proof = GetFinalOutputProof();
        var canonical = JsonSerializer.Serialize(new FinalOutputApprovalProofDigestDocument(
            FinalOutputProofVersionV1,
            Id.Value,
            TaskId.Value,
            RequestedBy,
            NormalizeProofTimestamp(CreatedAt),
            proof.WorkspaceId.Value,
            proof.WorkspaceCode,
            proof.FinalStepId.Value,
            proof.ActiveRunAttemptId.Value,
            proof.FinalNodeRunId.Value,
            proof.TaskFencingToken,
            proof.NodeFencingToken,
            proof.EvidenceSetDigest,
            proof.ManifestDigest,
            proof.ArtifactBindingDigest));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private string ComputeFinalOutputDecisionProofDigest()
    {
        if (Status is not (AgentApprovalStatus.Approved or AgentApprovalStatus.Rejected) ||
            ApprovedBy is null ||
            ApprovedAt is null ||
            !IsSha256(FinalOutputProofDigest))
        {
            throw new InvalidOperationException(
                "Final-output decision proof cannot be computed from incomplete state.");
        }

        var canonical = JsonSerializer.Serialize(new FinalOutputApprovalDecisionProofDocument(
            FinalOutputDecisionProofVersionV1,
            Id.Value,
            FinalOutputProofDigest!,
            NormalizeProofTimestamp(CreatedAt),
            TargetId,
            Status.ToString(),
            ApprovedBy.Value,
            NormalizeProofTimestamp(ApprovedAt.Value),
            ApprovalComment));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static DateTimeOffset NormalizeProofTimestamp(DateTimeOffset value)
    {
        const long ticksPerPostgresMicrosecond = 10;
        var utcTicks = value.UtcDateTime.Ticks;
        return new DateTimeOffset(
            utcTicks - utcTicks % ticksPerPostgresMicrosecond,
            TimeSpan.Zero);
    }

    private static string NormalizeRequired(string value, string paramName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }

    private sealed record FinalOutputApprovalProofDigestDocument(
        string Version,
        Guid ApprovalId,
        Guid TaskId,
        Guid RequestedBy,
        DateTimeOffset CreatedAt,
        Guid WorkspaceId,
        string WorkspaceCode,
        Guid FinalStepId,
        Guid ActiveRunAttemptId,
        Guid FinalNodeRunId,
        long TaskFencingToken,
        long NodeFencingToken,
        string EvidenceSetDigest,
        string ManifestDigest,
        string ArtifactBindingDigest);

    private sealed record FinalOutputApprovalDecisionProofDocument(
        string Version,
        Guid ApprovalId,
        string ApprovalProofDigest,
        DateTimeOffset CreatedAt,
        string TargetWorkspace,
        string Decision,
        Guid DecidedBy,
        DateTimeOffset DecidedAt,
        string? Comment);
}

public sealed record FinalOutputApprovalProof(
    ArtifactWorkspaceId WorkspaceId,
    string WorkspaceCode,
    AgentStepId FinalStepId,
    AgentTaskRunAttemptId ActiveRunAttemptId,
    AgentNodeRunId FinalNodeRunId,
    long TaskFencingToken,
    long NodeFencingToken,
    string EvidenceSetDigest,
    string ManifestDigest,
    string ArtifactBindingsJson,
    string ArtifactBindingDigest)
{
    private static readonly JsonSerializerOptions BindingSerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

    public void Validate()
    {
        if (WorkspaceId.Value == Guid.Empty ||
            FinalStepId.Value == Guid.Empty ||
            ActiveRunAttemptId.Value == Guid.Empty ||
            FinalNodeRunId.Value == Guid.Empty ||
            string.IsNullOrWhiteSpace(WorkspaceCode) ||
            !WorkspaceCode.StartsWith("ws_", StringComparison.OrdinalIgnoreCase) ||
            WorkspaceCode.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '_' or '-')) ||
            TaskFencingToken <= 0 ||
            NodeFencingToken <= 0 ||
            !IsSha256(EvidenceSetDigest) ||
            !IsSha256(ManifestDigest) ||
            !IsSha256(ArtifactBindingDigest) ||
            string.IsNullOrWhiteSpace(ArtifactBindingsJson) ||
            ArtifactBindingsJson.Length > 262_144)
        {
            throw new ArgumentException("Final-output approval proof is invalid.");
        }

        string canonicalBindings;
        FinalOutputApprovalArtifactBinding[]? bindings;
        try
        {
            canonicalBindings = AgentCanonicalJsonV1.Canonicalize(ArtifactBindingsJson);
            bindings = JsonSerializer.Deserialize<FinalOutputApprovalArtifactBinding[]>(
                canonicalBindings,
                BindingSerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Final-output approval artifact bindings are invalid.",
                nameof(ArtifactBindingsJson),
                exception);
        }

        if (bindings is null ||
            bindings.Length == 0 ||
            bindings.Select(binding => binding.ArtifactId).Distinct().Count() != bindings.Length ||
            bindings.Select(binding => binding.SourceRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != bindings.Length ||
            !bindings.Select(binding => binding.ArtifactId)
                .SequenceEqual(bindings.Select(binding => binding.ArtifactId).OrderBy(id => id)) ||
            bindings.Any(binding =>
                binding.ArtifactId == Guid.Empty ||
                binding.CreatedByStepId == Guid.Empty ||
                binding.Version <= 0 ||
                binding.FileSize < 0 ||
                string.IsNullOrWhiteSpace(binding.MimeType) ||
                binding.MimeType.Length > 160 ||
                !string.Equals(
                    ArtifactPathGuard.NormalizeRelativePath(binding.SourceRelativePath),
                    binding.SourceRelativePath,
                    StringComparison.Ordinal) ||
                binding.SourceRelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase) ||
                !IsSha256(binding.Sha256)) ||
            !string.Equals(
                ArtifactBindingDigest,
                Hash(canonicalBindings),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Final-output approval artifact bindings are invalid.");
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed record FinalOutputApprovalArtifactBinding(
    Guid ArtifactId,
    Guid CreatedByStepId,
    int Version,
    string SourceRelativePath,
    long FileSize,
    string MimeType,
    string Sha256);
