export type DeviceLogAnswerSectionKey =
  | 'conclusion'
  | 'metrics'
  | 'records'
  | 'explanation'
  | 'rootCause'
  | 'actions'
  | 'blockedActions'
  | 'scope'

export interface DeviceLogAnswerSection {
  key: DeviceLogAnswerSectionKey
  title: string
  content: string
  items: string[]
}

export interface DeviceLogAnswerView {
  sections: DeviceLogAnswerSection[]
}

const sectionDefinitions: Array<{
  key: DeviceLogAnswerSectionKey
  title: string
  aliases: string[]
}> = [
  { key: 'conclusion', title: '结论', aliases: ['结论'] },
  { key: 'metrics', title: '关键指标', aliases: ['关键指标'] },
  { key: 'records', title: '关键记录', aliases: ['关键记录'] },
  { key: 'explanation', title: '关键解释', aliases: ['关键解释'] },
  { key: 'rootCause', title: '可能原因', aliases: ['可能原因', '根因分析'] },
  { key: 'actions', title: '建议动作', aliases: ['建议动作'] },
  { key: 'blockedActions', title: '不能直接执行的动作', aliases: ['不能直接执行的动作'] },
  { key: 'scope', title: '查询范围', aliases: ['查询范围', '查询范围说明'] }
]

const aliasToDefinition = new Map(
  sectionDefinitions.flatMap((definition) =>
    definition.aliases.map((alias) => [alias, definition] as const)
  ).sort(([left], [right]) => right.length - left.length)
)
const sectionOrder = sectionDefinitions.map((definition) => definition.key)
const listItemPattern = /^(?:[-*•]\s*|\d+[.、)]\s*|（\d+）\s*)/

function stripHeadingMarkdown(line: string) {
  return line
    .trim()
    .replace(/^#{1,6}\s*/, '')
    .replace(/^\*\*/, '')
    .replace(/\*\*$/, '')
    .trim()
}

function matchSectionHeading(line: string) {
  const normalized = stripHeadingMarkdown(line)

  for (const [alias, definition] of aliasToDefinition) {
    const escaped = alias.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    const match = normalized.match(new RegExp(`^${escaped}\\s*(?:\\*\\*)?\\s*(?:[：:]\\s*(.*)|$)`))
    if (!match) {
      continue
    }

    const rest = (match[1] ?? '').trim()
    return {
      key: definition.key,
      title: definition.title,
      rest
    }
  }

  return null
}

function splitItems(content: string) {
  const lines = content
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean)

  if (lines.length === 0 || !lines.every((line) => listItemPattern.test(line))) {
    return []
  }

  return lines.map((line) => line.replace(listItemPattern, '').trim()).filter(Boolean)
}

function hasSection(sections: DeviceLogAnswerSection[], key: DeviceLogAnswerSectionKey) {
  return sections.some((section) => section.key === key && section.content.trim())
}

export function parseDeviceLogAnswerSections(content: string): DeviceLogAnswerView | null {
  const sectionLines = new Map<DeviceLogAnswerSectionKey, string[]>()
  const sectionTitles = new Map<DeviceLogAnswerSectionKey, string>()
  let currentKey: DeviceLogAnswerSectionKey | null = null

  for (const rawLine of content.replace(/\r\n/g, '\n').split('\n')) {
    const heading = matchSectionHeading(rawLine)
    if (heading) {
      currentKey = heading.key
      sectionTitles.set(heading.key, heading.title)
      if (!sectionLines.has(heading.key)) {
        sectionLines.set(heading.key, [])
      }

      if (heading.rest) {
        sectionLines.get(heading.key)?.push(heading.rest)
      }
      continue
    }

    if (currentKey) {
      sectionLines.get(currentKey)?.push(rawLine)
    }
  }

  const sections = sectionOrder
    .map((key) => {
      const rawLines = sectionLines.get(key)
      if (!rawLines) {
        return null
      }

      const content = rawLines.join('\n').trim()
      if (!content) {
        return null
      }

      return {
        key,
        title: sectionTitles.get(key) ?? sectionDefinitions.find((section) => section.key === key)?.title ?? key,
        content,
        items: splitItems(content)
      } satisfies DeviceLogAnswerSection
    })
    .filter((section): section is DeviceLogAnswerSection => section !== null)

  const deviceLogSignalCount = [
    'metrics',
    'records',
    'rootCause',
    'actions',
    'blockedActions',
    'scope'
  ].filter((key) => hasSection(sections, key as DeviceLogAnswerSectionKey)).length

  if (!hasSection(sections, 'conclusion') || deviceLogSignalCount < 3) {
    return null
  }

  return { sections }
}
