namespace AICopilot.Core.AiGateway.Aggregates.LanguageModel;

[Flags]
public enum LanguageModelUsage
{
    None = 0,
    Chat = 1,
    Routing = 2,
    Planner = 4
}
