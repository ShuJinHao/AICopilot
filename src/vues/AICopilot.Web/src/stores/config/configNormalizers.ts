import type { McpAllowedTool } from '@/types/app'

export function normalizeToolNames(toolNames: string[]) {
  return [...new Set(toolNames.map((item) => item.trim()).filter(Boolean))]
}

export function normalizeMcpAllowedTools(tools: McpAllowedTool[]) {
  const normalized = new Map<string, McpAllowedTool>()

  for (const tool of tools) {
    const toolName = tool.toolName.trim()
    if (!toolName) {
      continue
    }

    const key = toolName.toLowerCase()
    if (!normalized.has(key)) {
      normalized.set(key, {
        toolName,
        externalSystemType: tool.externalSystemType ?? null,
        capabilityKind: tool.capabilityKind ?? null,
        riskLevel: tool.riskLevel ?? null,
        readOnlyDeclared: Boolean(tool.readOnlyDeclared),
        mcpReadOnlyHint: tool.mcpReadOnlyHint ?? null,
        mcpDestructiveHint: tool.mcpDestructiveHint ?? null,
        mcpIdempotentHint: tool.mcpIdempotentHint ?? null
      })
    }
  }

  return [...normalized.values()]
}
