/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import PrimeVue from 'primevue/config'
import { AppButton } from '@doselect/web-shared/components'

/**
 * PrimeVue 4.5.5 遷移的防回歸檢查（admin 側，組長決策 A1）。
 * 與 customer-web 同名的 spec 互為對照：任一邊版本漂移都會讓該 App 的測試失敗。
 */

const PINNED_PRIMEVUE = '4.5.5'
const PINNED_THEMES = '1.2.5'

const pkgRoot = process.cwd()
const readJson = (relative: string) =>
  JSON.parse(readFileSync(resolve(pkgRoot, relative), 'utf8'))

const adminPkg = readJson('package.json')
const customerPkg = readJson('../customer-web/package.json')
const sharedPkg = readJson('../shared/package.json')
const adminLock = readJson('package-lock.json')

describe('admin-web is pinned to PrimeVue 4.5.5', () => {
  it('pins the exact version with no range specifier', () => {
    expect(adminPkg.dependencies.primevue).toBe(PINNED_PRIMEVUE)
    expect(adminPkg.dependencies.primevue).not.toMatch(/[\^~]/)
  })

  it('pins the theme package to the version that shares PrimeVue 4 styled engine', () => {
    expect(adminPkg.dependencies['@primeuix/themes']).toBe(PINNED_THEMES)
  })

  it('matches customer-web and the shared peer dependency exactly', () => {
    expect(adminPkg.dependencies.primevue).toBe(customerPkg.dependencies.primevue)
    expect(adminPkg.dependencies.primevue).toBe(sharedPkg.peerDependencies.primevue)
  })

  it('resolves PrimeVue 4.5.5 in the lockfile with no PrimeVue 5 packages', () => {
    expect(adminLock.packages['node_modules/primevue'].version).toBe(PINNED_PRIMEVUE)
    expect(adminLock.packages['node_modules/@primevue/core'].version).toBe(PINNED_PRIMEVUE)
    expect(Object.keys(adminLock.packages)).not.toContain('node_modules/@primeui/license-manager')
  })

  it('keeps the Light-only PrimeVue registration and the shared token import', () => {
    const main = readFileSync(resolve(pkgRoot, 'src/main.ts'), 'utf8')
    expect(main).toMatch(/darkModeSelector:\s*false/)
    expect(main).toMatch(/@doselect\/web-shared\/styles\/design-tokens\.css/)
  })

  it('declares no --color-primary of its own', () => {
    const css = readFileSync(resolve(pkgRoot, 'src/style.css'), 'utf8')
    expect(css).not.toMatch(/--color-primary\s*:/)
  })

  it('renders the shared AppButton on PrimeVue 4', async () => {
    const wrapper = mount(AppButton, {
      global: { plugins: [PrimeVue] },
      slots: { default: () => 'ok' },
    })
    expect(wrapper.find('button').exists()).toBe(true)
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('click')).toHaveLength(1)
  })
})

describe('dev-mode module duplication guard (vite config)', () => {
  const REQUIRED_EXCLUDES = [
    '@doselect/web-shared',
    'primevue',
    '@primevue/core',
    '@primevue/icons',
    '@primeuix/themes',
    '@primeuix/styled',
    '@primeuix/styles',
  ]

  const config = readFileSync(resolve(pkgRoot, 'vite.config.ts'), 'utf8')
  const customerConfig = readFileSync(resolve(pkgRoot, '../customer-web/vite.config.ts'), 'utf8')
  const parseExcludes = (source: string) => {
    const block = source.match(/exclude:\s*\[([\s\S]*?)\]/)
    return block
      ? block[1].split(',').map((entry) => entry.trim().replace(/^'|'$/g, '')).filter(Boolean)
      : []
  }

  it('keeps resolve.preserveSymlinks', () => {
    expect(config).toMatch(/preserveSymlinks:\s*true/)
  })

  it('excludes every package of the PrimeVue runtime from pre-bundling', () => {
    const excludes = parseExcludes(config)
    for (const required of REQUIRED_EXCLUDES) {
      expect(excludes).toContain(required)
    }
  })

  it('matches the customer-web exclude list exactly', () => {
    expect(parseExcludes(config)).toEqual(parseExcludes(customerConfig))
  })

  it('adds no resolve.dedupe and no component-level optimizeDeps.include', () => {
    expect(config).not.toMatch(/dedupe/)
    expect(config).not.toMatch(/optimizeDeps[\s\S]*include/)
    expect(config).not.toMatch(/primevue\/(button|paginator|dialog|toast|datatable)/)
  })
})

describe('PrimeVue component tokens generate under the real preset (admin harness)', () => {
  it('generates the button variables block when AppButton mounts with DoSelectPreset', async () => {
    const { DoSelectPreset } = await import('@doselect/web-shared/theme')
    const wrapper = mount(AppButton, {
      global: {
        plugins: [[PrimeVue, {
          theme: { preset: DoSelectPreset, options: { darkModeSelector: false } },
        }] as never],
      },
    })
    expect(wrapper.find('button').exists()).toBe(true)
    const ids = [...document.querySelectorAll('style')]
      .map((el) => el.getAttribute('data-primevue-style-id'))
      .filter((id): id is string => Boolean(id))
    expect(ids).toContain('button-variables')
  })
})
