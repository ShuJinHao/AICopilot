namespace AICopilot.SharedKernel.Domain;

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class StronglyTypedGuidIdAttribute(string requiredMessage) : Attribute
{
    public string RequiredMessage { get; } = string.IsNullOrWhiteSpace(requiredMessage)
        ? "Strongly typed Guid id is required."
        : requiredMessage;
}
