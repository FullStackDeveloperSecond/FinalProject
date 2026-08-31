/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import PrimeVue from 'primevue/config'
import { AppButton, PagePager } from '@doselect/web-shared/components'

/**
 * PrimeVue 4.5.5 遷移的防回歸檢查（組長決策 A1）。
 *
 * 專案固定使用 PrimeVue 4.5.5；不得升回 5.x。5.x 會引入
 * `@primeui/license-manager`，在每個畫面印出授權提示，且其
 * `@primeuix/styled@1.x` 與本專案 theme 套件所需的 0.7.x 不同版。
 */

const PINNED_PRIMEVUE = '4.5.5'
const PINNED_THEMES = '1.2.5'

// `npm test` 以套件根目錄為 cwd 執行 vitest。
const pkgRoot = process.cwd()
const readJson = (relative: string) =>
  JSON.parse(readFileSync(resolve(pkgRoot, relative), 'utf8'))

const customerPkg = readJson('package.json')
const adminPkg = readJson('../admin-web/package.json')
const sharedPkg = readJson('../shared/package.json')
const customerLock = readJson('package-lock.json')
const adminLock = readJson('../admin-web/package-lock.json')

const withPrimeVue = { global: { plugins: [PrimeVue] } }

describe('PrimeVue version is pinned to 4.5.5 everywhere', () => {
  it('pins customer-web to the exact version', () => {
    expect(customerPkg.dependencies.primevue).toBe(PINNED_PRIMEVUE)
  })

  it('pins admin-web to the exact version', () => {
    expect(adminPkg.dependencies.primevue).toBe(PINNED_PRIMEVUE)
  })

  it('pins the shared peer dependency to the exact version', () => {
    expect(sharedPkg.peerDependencies.primevue).toBe(PINNED_PRIMEVUE)
  })

  it('keeps customer-web and admin-web on the same PrimeVue version', () => {
    expect(customerPkg.dependencies.primevue).toBe(adminPkg.dependencies.primevue)
  })

  it('uses no range specifiers for PrimeVue or its theme package', () => {
    const specifiers = [
      customerPkg.dependencies.primevue,
      customerPkg.dependencies['@primeuix/themes'],
      adminPkg.dependencies.primevue,
      adminPkg.dependencies['@primeuix/themes'],
      sharedPkg.peerDependencies.primevue,
      sharedPkg.peerDependencies['@primeuix/themes'],
    ]
    for (const specifier of specifiers) {
      expect(specifier).not.toMatch(/[\^~]/)
    }
  })

  it('keeps the theme package aligned across both apps and the shared peer', () => {
    expect(customerPkg.dependencies['@primeuix/themes']).toBe(PINNED_THEMES)
    expect(adminPkg.dependencies['@primeuix/themes']).toBe(PINNED_THEMES)
    expect(sharedPkg.peerDependencies['@primeuix/themes']).toBe(PINNED_THEMES)
  })
})

describe('lockfiles resolve PrimeVue 4.5.5', () => {
  it.each([
    ['customer-web', customerLock],
    ['admin-web', adminLock],
  ])('%s lockfile installs the pinned version', (_name, lock) => {
    expect(lock.packages['node_modules/primevue'].version).toBe(PINNED_PRIMEVUE)
    expect(lock.packages['node_modules/@primevue/core'].version).toBe(PINNED_PRIMEVUE)
  })

  it.each([
    ['customer-web', customerLock],
    ['admin-web', adminLock],
  ])('%s lockfile carries no PrimeVue 5 packages', (_name, lock) => {
    const names = Object.keys(lock.packages)
    expect(names).not.toContain('node_modules/@primeui/license-manager')
    for (const key of ['node_modules/primevue', 'node_modules/@primevue/core', 'node_modules/@primevue/icons']) {
      expect(lock.packages[key].version.startsWith('5.')).toBe(false)
    }
  })

  it.each([
    ['customer-web', customerLock],
    ['admin-web', adminLock],
  ])('%s resolves a single @primeuix/styled 0.7.x engine', (_name, lock) => {
    const styled = Object.entries(lock.packages)
      .filter(([key]) => key.endsWith('node_modules/@primeuix/styled'))
      .map(([, meta]) => (meta as { version: string }).version)
    expect(styled.length).toBe(1)
    expect(styled[0].startsWith('0.7.')).toBe(true)
  })
})

describe('the shared PrimeVue preset still loads on 4.5.5', () => {
  it('exposes DoSelectPreset built from Aura via definePreset', async () => {
    const { DoSelectPreset } = await import('@doselect/web-shared/theme')
    expect(DoSelectPreset).toBeTruthy()
    // definePreset 的回傳型別是 unknown；斷言前先窄化成本測試需要的形狀。
    const preset = DoSelectPreset as {
      semantic: { colorScheme: { light: { primary: { color: string } } } }
    }
    expect(preset.semantic).toBeTruthy()
    // 主色仍指向共用 token，而不是寫死色碼。
    expect(preset.semantic.colorScheme.light.primary.color).toBe('var(--color-primary)')
  })

  it('imports Aura and definePreset from @primeuix/themes', () => {
    const preset = readFileSync(resolve(pkgRoot, '../shared/src/theme/doselect-preset.ts'), 'utf8')
    expect(preset).toMatch(/from '@primeuix\/themes'/)
    expect(preset).toMatch(/from '@primeuix\/themes\/aura'/)
    expect(preset).not.toMatch(/@primevue\/themes/)
  })
})

describe('both apps keep Light-only PrimeVue registration', () => {
  it.each([
    ['customer-web', 'src/main.ts'],
    ['admin-web', '../admin-web/src/main.ts'],
  ])('%s registers PrimeVue with darkModeSelector: false', (_name, relative) => {
    const main = readFileSync(resolve(pkgRoot, relative), 'utf8')
    expect(main).toMatch(/app\.use\(PrimeVue/)
    expect(main).toMatch(/darkModeSelector:\s*false/)
  })
})

describe('a single Design Token source still feeds both apps', () => {
  it.each([
    ['customer-web', 'src/main.ts'],
    ['admin-web', '../admin-web/src/main.ts'],
  ])('%s imports the shared token stylesheet', (_name, relative) => {
    const main = readFileSync(resolve(pkgRoot, relative), 'utf8')
    expect(main).toMatch(/@doselect\/web-shared\/styles\/design-tokens\.css/)
  })

  it.each([
    ['customer-web', 'src/style.css'],
    ['admin-web', '../admin-web/src/style.css'],
  ])('%s declares no --color-primary of its own, so one edit changes both', (_name, relative) => {
    const css = readFileSync(resolve(pkgRoot, relative), 'utf8')
    expect(css).not.toMatch(/--color-primary\s*:/)
  })

  it('defines --color-primary exactly once, in the shared token file', () => {
    const tokens = readFileSync(resolve(pkgRoot, '../shared/src/styles/design-tokens.css'), 'utf8')
      .replace(/\/\*[\s\S]*?\*\//g, '')
    const lightBlock = tokens.slice(tokens.indexOf(':root'), tokens.indexOf(':root[data-theme="dark"]'))
    expect(lightBlock.match(/--color-primary\s*:/g)).toHaveLength(1)
  })
})

describe('the wrapped PrimeVue components still work on 4.5.5', () => {
  it('AppButton renders a real button and emits click', async () => {
    const wrapper = mount(AppButton, { ...withPrimeVue, slots: { default: () => 'ok' } })
    expect(wrapper.find('button').exists()).toBe(true)
    expect(wrapper.text()).toContain('ok')
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('click')).toHaveLength(1)
  })

  it('AppButton still blocks activation while loading', async () => {
    const wrapper = mount(AppButton, { ...withPrimeVue, props: { loading: true } })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('click')).toBeUndefined()
  })

  it('PagePager keeps the 1-based to 0-based conversion', () => {
    const wrapper = mount(PagePager, {
      ...withPrimeVue,
      props: { page: 3, totalRecords: 100, pageSize: 10, ariaLabel: 'Pages' },
    })
    expect(wrapper.findComponent({ name: 'Paginator' }).props('first')).toBe(20)
  })

  it('PagePager still clamps an out-of-range page', async () => {
    const wrapper = mount(PagePager, {
      ...withPrimeVue,
      props: { page: 9, totalRecords: 30, pageSize: 10, ariaLabel: 'Pages' },
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('update:page')).toEqual([[3]])
  })

  it('PagePager still withholds updates for invalid numeric input', async () => {
    const wrapper = mount(PagePager, {
      ...withPrimeVue,
      props: { page: 1, totalRecords: 100, pageSize: 0, ariaLabel: 'Pages' },
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('update:page')).toBeUndefined()
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

  const readConfig = (relative: string) => readFileSync(resolve(pkgRoot, relative), 'utf8')
  const parseExcludes = (source: string) => {
    const block = source.match(/exclude:\s*\[([\s\S]*?)\]/)
    return block
      ? block[1].split(',').map((entry) => entry.trim().replace(/^'|'$/g, '')).filter(Boolean)
      : []
  }

  const configs: Array<[string, string]> = [
    ['customer-web', readConfig('vite.config.ts')],
    ['admin-web', readConfig('../admin-web/vite.config.ts')],
  ]

  it.each(configs)('%s keeps resolve.preserveSymlinks', (_name, source) => {
    expect(source).toMatch(/preserveSymlinks:\s*true/)
  })

  it.each(configs)('%s excludes every package of the PrimeVue runtime from pre-bundling', (_name, source) => {
    const excludes = parseExcludes(source)
    for (const required of REQUIRED_EXCLUDES) {
      expect(excludes).toContain(required)
    }
  })

  it('uses an identical exclude list in both apps', () => {
    expect(parseExcludes(configs[0][1])).toEqual(parseExcludes(configs[1][1]))
  })

  it.each(configs)('%s adds no resolve.dedupe', (_name, source) => {
    expect(source).not.toMatch(/dedupe/)
  })

  it.each(configs)('%s adds no component-level optimizeDeps.include', (_name, source) => {
    expect(source).not.toMatch(/optimizeDeps[\s\S]*include/)
    expect(source).not.toMatch(/primevue\/(button|paginator|dialog|toast|datatable)/)
  })
})

describe('PrimeVue component tokens generate under the real preset (Button / Paginator harness)', () => {
  /**
   * 掛載時帶入正式 DoSelectPreset，重現 dev server 的主題註冊路徑，
   * 藉此確認元件層 design token 會被產生 —— 這正是 prebundled/raw 雙實例時失效的地方。
   * 僅在測試內掛載，不新增任何正式路由或頁面。
   */
  const themedPlugins = async () => {
    const { DoSelectPreset } = await import('@doselect/web-shared/theme')
    return [[PrimeVue, {
      theme: { preset: DoSelectPreset, options: { darkModeSelector: false } },
    }]] as never
  }

  const styleIds = () =>
    [...document.querySelectorAll('style')]
      .map((el) => el.getAttribute('data-primevue-style-id'))
      .filter((id): id is string => Boolean(id))

  const blockFor = (id: string) =>
    [...document.querySelectorAll('style')]
      .filter((el) => el.getAttribute('data-primevue-style-id') === id)
      .map((el) => el.textContent ?? '')
      .join('')

  const mountPager = async (page: number, totalRecords: number, pageSize: number) =>
    mount(PagePager, {
      props: { page, totalRecords, pageSize, ariaLabel: 'Pages' },
      global: { plugins: await themedPlugins() },
    })

  it('generates the button variables block, not just the semantic one', async () => {
    const wrapper = mount(AppButton, { global: { plugins: await themedPlugins() } })
    expect(wrapper.find('button').exists()).toBe(true)
    expect(styleIds()).toContain('button-variables')
  })

  it('generates the paginator variables block when PagePager mounts', async () => {
    const wrapper = await mountPager(2, 100, 10)
    expect(wrapper.find('nav').exists()).toBe(true)
    expect(styleIds()).toContain('paginator-variables')
  })

  it('resolves the primary token chain down to the shared --color-primary variable', async () => {
    mount(AppButton, { global: { plugins: await themedPlugins() } })
    // 元件層 -> 語意層 -> 專案 token，三段都要接得上。
    expect(blockFor('button-variables'))
      .toMatch(/--p-button-primary-background:\s*var\(--p-primary-color\)/)
    expect(blockFor('semantic-variables'))
      .toMatch(/--p-primary-color:\s*var\(--color-primary\)/)
  })

  it('keeps the 1-based conversion, clamping and safe fallback under the real preset', async () => {
    const converted = await mountPager(3, 100, 10)
    expect(converted.findComponent({ name: 'Paginator' }).props('first')).toBe(20)

    const clamped = await mountPager(9, 30, 10)
    await clamped.vm.$nextTick()
    expect(clamped.emitted('update:page')).toEqual([[3]])

    const invalid = await mountPager(1, 100, 0)
    await invalid.vm.$nextTick()
    expect(invalid.emitted('update:page')).toBeUndefined()
  })
})
