namespace AICopilot.SharedKernel.Ai;

public sealed class AiToolExecutionTimeoutException()
    : Exception("Tool execution exceeded its governed timeout.");
