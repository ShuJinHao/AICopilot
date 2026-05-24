using System.Text.RegularExpressions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.PromptPolicy;

public sealed class PromptPolicy : IAggregateRoot<PromptPolicyId>
{
    private readonly List<PromptPolicyVersion> _versions = [];

    private PromptPolicy()
    {
    }

    public PromptPolicy(
        string code,
        string name,
        PromptPolicyUsage usage,
        bool isEnabled,
        DateTimeOffset nowUtc)
    {
        Id = PromptPolicyId.New();
        Code = NormalizeCode(code);
        Name = NormalizeRequired(name, nameof(name), 200);
        Usage = ValidateUsage(usage);
        IsEnabled = isEnabled;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public PromptPolicyId Id { get; private set; }

    public uint RowVersion { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public PromptPolicyUsage Usage { get; private set; }

    public bool IsEnabled { get; private set; }

    public int? ActiveVersionNo { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<PromptPolicyVersion> Versions => _versions.AsReadOnly();

    public PromptPolicyVersion? ActiveVersion =>
        ActiveVersionNo.HasValue
            ? _versions.FirstOrDefault(version => version.VersionNo == ActiveVersionNo.Value && version.IsEnabled)
            : null;

    public PromptPolicyVersion AddVersion(
        string systemPrompt,
        string safetyConstraints,
        string contextInjectionRules,
        string outputFormat,
        bool activate,
        DateTimeOffset nowUtc)
    {
        var versionNo = _versions.Count == 0 ? 1 : _versions.Max(version => version.VersionNo) + 1;
        var version = new PromptPolicyVersion(
            Guid.NewGuid(),
            versionNo,
            NormalizeRequired(systemPrompt, nameof(systemPrompt), 12000),
            NormalizeOptional(safetyConstraints, 12000),
            NormalizeOptional(contextInjectionRules, 12000),
            NormalizeOptional(outputFormat, 12000),
            isEnabled: true,
            nowUtc);
        _versions.Add(version);
        if (activate)
        {
            ActivateVersion(versionNo, nowUtc);
        }
        else
        {
            UpdatedAt = nowUtc;
        }

        return version;
    }

    public void UpdateHeader(
        string name,
        PromptPolicyUsage usage,
        bool isEnabled,
        DateTimeOffset nowUtc)
    {
        Name = NormalizeRequired(name, nameof(name), 200);
        Usage = ValidateUsage(usage);
        IsEnabled = isEnabled;
        UpdatedAt = nowUtc;
    }

    public void ActivateVersion(int versionNo, DateTimeOffset nowUtc)
    {
        var version = _versions.FirstOrDefault(item => item.VersionNo == versionNo);
        if (version is null)
        {
            throw new ArgumentException("Prompt policy version does not exist.", nameof(versionNo));
        }

        version.Enable();
        ActiveVersionNo = versionNo;
        UpdatedAt = nowUtc;
    }

    public void DisableVersion(int versionNo, DateTimeOffset nowUtc)
    {
        var version = _versions.FirstOrDefault(item => item.VersionNo == versionNo);
        if (version is null)
        {
            throw new ArgumentException("Prompt policy version does not exist.", nameof(versionNo));
        }

        version.Disable();
        if (ActiveVersionNo == versionNo)
        {
            ActiveVersionNo = null;
        }

        UpdatedAt = nowUtc;
    }

    private static string NormalizeCode(string value)
    {
        var normalized = NormalizeRequired(value, nameof(value), 100);
        if (!Regex.IsMatch(normalized, "^[a-zA-Z0-9_.-]+$"))
        {
            throw new ArgumentException("Prompt policy code can only contain letters, numbers, dot, underscore, and dash.", nameof(value));
        }

        return normalized;
    }

    private static PromptPolicyUsage ValidateUsage(PromptPolicyUsage usage)
    {
        return Enum.IsDefined(typeof(PromptPolicyUsage), usage)
            ? usage
            : throw new ArgumentOutOfRangeException(nameof(usage), usage, "Prompt policy usage is invalid.");
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

public sealed class PromptPolicyVersion
{
    private PromptPolicyVersion()
    {
    }

    internal PromptPolicyVersion(
        Guid id,
        int versionNo,
        string systemPrompt,
        string safetyConstraints,
        string contextInjectionRules,
        string outputFormat,
        bool isEnabled,
        DateTimeOffset createdAt)
    {
        Id = id;
        VersionNo = versionNo;
        SystemPrompt = systemPrompt;
        SafetyConstraints = safetyConstraints;
        ContextInjectionRules = contextInjectionRules;
        OutputFormat = outputFormat;
        IsEnabled = isEnabled;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public int VersionNo { get; private set; }

    public string SystemPrompt { get; private set; } = string.Empty;

    public string SafetyConstraints { get; private set; } = string.Empty;

    public string ContextInjectionRules { get; private set; } = string.Empty;

    public string OutputFormat { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal void Enable()
    {
        IsEnabled = true;
    }

    internal void Disable()
    {
        IsEnabled = false;
    }
}

public enum PromptPolicyUsage
{
    Routing = 1,
    Answer = 2,
    Planner = 3,
    RagRewrite = 4,
    TextToSql = 5,
    AgentTool = 6
}
