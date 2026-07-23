namespace AICopilot.Services.Contracts;

public sealed class CloudReadOnlyTextToSqlOptions
{
    public const string SectionName = "DataAnalysis:CloudReadOnlyTextToSql";
    public const int DefaultMaxRepairAttempts = 3;
    public const int AbsoluteMaxRepairAttempts = 5;

    public bool Enabled { get; set; } = true;

    public string TemplateName { get; set; } = "business_readonly_text_to_sql";

    public int MaxRepairAttempts { get; set; } = DefaultMaxRepairAttempts;

    public int MaxRows { get; set; } = 200;

    public int CommandTimeoutSeconds { get; set; } = 15;

    public int MaxOutputTokens { get; set; } = 1024;

    public int ResolveMaxRepairAttempts()
    {
        return Math.Clamp(MaxRepairAttempts, 0, AbsoluteMaxRepairAttempts);
    }

    public DatabaseQueryOptions ResolveQueryOptions()
    {
        return new DatabaseQueryOptions(
            MaxRows: Math.Clamp(MaxRows, 1, 1000),
            CommandTimeoutSeconds: Math.Clamp(CommandTimeoutSeconds, 1, 120));
    }

    public int ResolveMaxOutputTokens()
    {
        return Math.Clamp(MaxOutputTokens, 256, 4096);
    }
}
