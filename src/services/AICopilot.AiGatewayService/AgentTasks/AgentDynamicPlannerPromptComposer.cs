namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentDynamicPlannerPromptComposer
{
    internal static string ComposeInstructions(string basePrompt)
    {
        return string.Join(
            "\n\n",
            basePrompt,
            "You are a backend-controlled planning component. Return JSON only.",
            "Return exactly one JSON object: {\"steps\":[...]} with no Markdown and no explanation.",
            "Each step may contain only title, description, stepType, toolCode, requiresApproval, inputJson.",
            "Use only toolCode values supplied by the backend plannerToolCatalog. Do not invent tools, shell commands, paths, SQL, Cloud writes, or Cloud intent.",
            "Use each tool's inputSchema summary to create inputJson when arguments are required.",
            "inputJson must be a JSON object string or object matching the supplied registry schema summary. Omit it when no arguments are needed.");
    }
}
