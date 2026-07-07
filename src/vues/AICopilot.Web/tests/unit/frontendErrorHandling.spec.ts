import { describe, expect, it } from 'vitest'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { join, relative } from 'node:path'

const sourceRoot = fileURLToPath(new URL('../../src', import.meta.url))

function collectSourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry)
    const stat = statSync(path)
    if (stat.isDirectory()) {
      return collectSourceFiles(path)
    }

    return /\.(ts|vue)$/.test(entry) ? [path] : []
  })
}

describe('frontend error handling contracts', () => {
  it('does not contain bare catch blocks in source files', () => {
    const matches = collectSourceFiles(sourceRoot)
      .flatMap((path) => {
        const source = readFileSync(path, 'utf8')
        return /catch\s*\{/.test(source) ? [relative(sourceRoot, path)] : []
      })

    expect(matches).toEqual([])
  })
})
