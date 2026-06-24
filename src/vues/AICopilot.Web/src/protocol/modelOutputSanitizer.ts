const completeThinkTagPattern = /<\s*(?:mm:)?think\s*>[\s\S]*?<\s*\/\s*(?:mm:)?think\s*>/gi
const leadingCloseTagPattern = /^[\s\S]*?<\s*\/\s*(?:mm:)?think\s*>/i
const trailingOpenTagPattern = /<\s*(?:mm:)?think\s*>[\s\S]*$/i
const nakedThinkLinePattern = /^\s*(?:\/?mm:think|\/?think)\b.*(?:\r?\n|$)/gim
const residualTagPattern = /<\s*\/?\s*(?:mm:)?think\s*\/?\s*>/gi

export function stripThinkingTags(content: string) {
  if (!content) {
    return ''
  }

  return content
    .replace(completeThinkTagPattern, '')
    .replace(leadingCloseTagPattern, '')
    .replace(trailingOpenTagPattern, '')
    .replace(nakedThinkLinePattern, '')
    .replace(residualTagPattern, '')
}
