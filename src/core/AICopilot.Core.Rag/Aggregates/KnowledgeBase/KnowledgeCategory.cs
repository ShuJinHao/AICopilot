using AICopilot.Core.Rag.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.Rag.Aggregates.KnowledgeBase;

public class KnowledgeCategory : IAggregateRoot<KnowledgeCategoryId>
{
    protected KnowledgeCategory()
    {
    }

    public KnowledgeCategory(
        string name,
        string businessDomain,
        string visibility,
        string department,
        int priority,
        bool isEnabled = true)
    {
        Validate(name, priority);

        Id = KnowledgeCategoryId.New();
        Name = name.Trim();
        BusinessDomain = NormalizeOptionalText(businessDomain);
        Visibility = NormalizeOptionalText(visibility, "AuthenticatedUsers");
        Department = NormalizeOptionalText(department);
        Priority = priority;
        IsEnabled = isEnabled;
        CreatedAt = DateTime.UtcNow;
    }

    public KnowledgeCategoryId Id { get; private set; }

    public uint RowVersion { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string BusinessDomain { get; private set; } = string.Empty;

    public string Visibility { get; private set; } = "AuthenticatedUsers";

    public string Department { get; private set; } = string.Empty;

    public int Priority { get; private set; }

    public bool IsEnabled { get; private set; } = true;

    public DateTime CreatedAt { get; private set; }

    public void Update(
        string name,
        string businessDomain,
        string visibility,
        string department,
        int priority,
        bool isEnabled)
    {
        Validate(name, priority);

        Name = name.Trim();
        BusinessDomain = NormalizeOptionalText(businessDomain);
        Visibility = NormalizeOptionalText(visibility, "AuthenticatedUsers");
        Department = NormalizeOptionalText(department);
        Priority = priority;
        IsEnabled = isEnabled;
    }

    private static void Validate(string name, int priority)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Knowledge category name is required.", nameof(name));
        }

        if (priority < 0 || priority > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(priority), "Knowledge category priority must be between 0 and 1000.");
        }
    }

    private static string NormalizeOptionalText(string? value, string defaultValue = "")
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }
}
