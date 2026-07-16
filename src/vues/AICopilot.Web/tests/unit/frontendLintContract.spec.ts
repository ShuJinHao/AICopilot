import { ESLint } from 'eslint'
import { describe, expect, it } from 'vitest'

describe('frontend lint contract', () => {
  it('rejects bare catch clauses in TypeScript and Vue while accepting named catches', async () => {
    const eslint = new ESLint({ cwd: process.cwd() })
    const [bareTypeScript, namedTypeScript, bareVue, namedVue] = await Promise.all([
      eslint.lintText('try { throw new Error("boom") } catch { void 0 }', {
        filePath: 'src/fixtures/bare-catch.ts',
      }),
      eslint.lintText('try { throw new Error("boom") } catch (error) { throw error }', {
        filePath: 'src/fixtures/named-catch.ts',
      }),
      eslint.lintText('<script setup lang="ts">try { throw new Error("boom") } catch {}</script>', {
        filePath: 'src/fixtures/BareCatch.vue',
      }),
      eslint.lintText(
        '<script setup lang="ts">try { throw new Error("boom") } catch (error) { throw error }</script>',
        { filePath: 'src/fixtures/NamedCatch.vue' },
      ),
    ])

    const restrictedSyntaxMessages = (result: Awaited<ReturnType<ESLint['lintText']>>) =>
      result.flatMap((entry) =>
        entry.messages.filter((message) => message.ruleId === 'no-restricted-syntax'),
      )

    expect(restrictedSyntaxMessages(bareTypeScript)).toHaveLength(1)
    expect(restrictedSyntaxMessages(bareVue)).toHaveLength(1)
    expect(restrictedSyntaxMessages(namedTypeScript)).toHaveLength(0)
    expect(restrictedSyntaxMessages(namedVue)).toHaveLength(0)
  })
})
