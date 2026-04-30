namespace AICopilot.Services.CrossCutting.Serialization;

public static class SensitiveValueMasker
{
    public static string? Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return "******";
    }
}
