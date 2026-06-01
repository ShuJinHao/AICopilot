namespace AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;

public sealed record PilotAuthorizationMachineValidationResult(
    bool IsAccepted,
    IReadOnlyCollection<string> RejectedReasons)
{
    public static PilotAuthorizationMachineValidationResult Accepted() => new(true, []);

    public static PilotAuthorizationMachineValidationResult Rejected(IReadOnlyCollection<string> rejectedReasons) =>
        new(false, rejectedReasons);
}
