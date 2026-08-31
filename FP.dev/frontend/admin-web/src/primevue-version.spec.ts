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
