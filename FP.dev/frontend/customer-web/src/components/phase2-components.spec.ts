/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { AppButton, FormField, PageHeader, StatusBadge } from '@doselect/web-shared/components'
import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { h } from 'vue'
import PrimeVue from 'primevue/config'
import Button from 'primevue/button'

const withPrimeVue = { global: { plugins: [PrimeVue] } }

// `npm test` runs vitest with cwd = this package root (frontend/customer-web).
const pkgRoot = process.cwd()
const readShared = (relative: string) =>
  readFileSync(resolve(pkgRoot, '../shared/src', relative), 'utf8')

const appButtonSource = readShared('components/AppButton.vue')
const statusBadgeSource = readShared('components/StatusBadge.vue')
const pageHeaderSource = readShared('components/PageHeader.vue')
const formFieldSource = readShared('components/FormField.vue')
const tokensCss = readShared('styles/design-tokens.css').replace(/\/\*[\s\S]*?\*\//g, '')

/** The <style> block of a SFC, with CSS comments stripped. */
function styleBlock(source: string): string {
  const match = source.match(/<style[^>]*>([\s\S]*?)<\/style>/)
  if (!match) {
    throw new Error('component has no <style> block')
  }
  return match[1].replace(/\/\*[\s\S]*?\*\//g, '')
}

/** The declarations of a single CSS rule, by selector. */
function ruleBody(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const match = css.match(new RegExp(`${escaped}\\s*\\{([^}]*)\\}`))
  if (!match) {
    throw new Error(`rule "${selector}" not found`)
  }
  return match[1]
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('AppButton', () => {
  const variants = ['primary', 'secondary', 'ghost', 'danger'] as const

  it('renders a real <button> and its content for every variant', () => {
    for (const variant of variants) {
      const wrapper = mount(AppButton, {
        ...withPrimeVue,
        props: { variant },
        slots: { default: () => '送出' },
      })
      expect(wrapper.find('button').exists()).toBe(true)
      expect(wrapper.text()).toContain('送出')
      wrapper.unmount()
    }
  })

  it.each(variants)('exposes the public ds-app-button classes for variant "%s"', (variant) => {
    const classes = mount(AppButton, { ...withPrimeVue, props: { variant } })
      .find('button')
      .classes()
    expect(classes).toContain('ds-app-button')
    expect(classes).toContain(`ds-app-button--${variant}`)
  })

  it('maps ghost and danger onto PrimeVue modifier props', () => {
    const ghost = mount(AppButton, { ...withPrimeVue, props: { variant: 'ghost' } })
    expect(ghost.findComponent(Button).props('text')).toBe(true)

    const danger = mount(AppButton, { ...withPrimeVue, props: { variant: 'danger' } })
    expect(danger.findComponent(Button).props('severity')).toBe('danger')

    const primary = mount(AppButton, { ...withPrimeVue, props: { variant: 'primary' } })
    expect(primary.findComponent(Button).props('outlined')).toBeFalsy()
    expect(primary.findComponent(Button).props('text')).toBeFalsy()
    expect(primary.findComponent(Button).props('severity')).toBeFalsy()
  })

  it('never hands secondary to PrimeVue\'s emerald outlined variant', () => {
    const secondary = mount(AppButton, { ...withPrimeVue, props: { variant: 'secondary' } })
    expect(secondary.findComponent(Button).props('outlined')).toBeFalsy()
    expect(secondary.findComponent(Button).props('text')).toBeFalsy()
    expect(secondary.findComponent(Button).props('severity')).toBeFalsy()
  })

  it('emits click once when enabled', async () => {
    const wrapper = mount(AppButton, withPrimeVue)
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('click')).toHaveLength(1)
  })

  it('blocks repeated activation while loading (no click emitted, button disabled)', async () => {
    const wrapper = mount(AppButton, { ...withPrimeVue, props: { loading: true } })
    await wrapper.find('button').trigger('click')
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('click')).toBeUndefined()
    expect(wrapper.find('button').attributes('disabled')).toBeDefined()
  })

  it('blocks activation while disabled', async () => {
    const wrapper = mount(AppButton, { ...withPrimeVue, props: { disabled: true } })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('click')).toBeUndefined()
  })

  it('supports an icon slot alongside a text slot', () => {
    const wrapper = mount(AppButton, {
      ...withPrimeVue,
      slots: {
        icon: () => h('span', { class: 'test-icon' }, '★'),
        default: () => '儲存',
      },
    })
    expect(wrapper.find('.test-icon').exists()).toBe(true)
    expect(wrapper.text()).toContain('儲存')
  })

  it('forwards the native button type', () => {
    const wrapper = mount(AppButton, { ...withPrimeVue, props: { type: 'submit' } })
    expect(wrapper.find('button').attributes('type')).toBe('submit')
  })

  it('keeps the control keyboard-focusable (real button, no tabindex removal)', () => {
    const button = mount(AppButton, withPrimeVue).find('button')
    expect(button.element.tagName).toBe('BUTTON')
    expect(button.attributes('tabindex')).toBeUndefined()
  })
})

describe('AppButton secondary is Navy, defined on its own class with tokens', () => {
  const css = styleBlock(appButtonSource)

  it('defines .ds-app-button--secondary in the component stylesheet', () => {
    expect(css).toContain('.ds-app-button--secondary')
  })

  it.each([
    ['background', '--color-surface'],
    ['border', '--color-navy'],
    ['color', '--color-navy'],
  ])('paints secondary %s from var(%s)', (property, token) => {
    const body = ruleBody(css, '.ds-app-button--secondary')
    expect(body).toMatch(new RegExp(`${property}:\\s*[^;]*var\\(${token}\\)`))
  })

  it('uses the Navy hover pair on hover', () => {
    const body = ruleBody(css, '.ds-app-button--secondary:not(:disabled):hover')
    expect(body).toMatch(/background:\s*var\(--color-navy-hover\)/)
    expect(body).toMatch(/border-color:\s*var\(--color-navy-hover\)/)
    expect(body).toMatch(/color:\s*var\(--color-on-navy\)/)
  })

  it('overrides no PrimeVue internal .p-* class and hardcodes nothing', () => {
    expect(css).not.toMatch(/\.p-[a-z-]/)
    expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i)
    expect(css).not.toMatch(/!important/)
  })
})

describe('PageHeader', () => {
  it('renders the title and description', () => {
    const wrapper = mount(PageHeader, {
      props: { title: '退貨案件', description: '處理中的退貨申請' },
    })
    expect(wrapper.get('h1').text()).toBe('退貨案件')
    expect(wrapper.text()).toContain('處理中的退貨申請')
  })

  it('omits the description node when not provided', () => {
    const wrapper = mount(PageHeader, { props: { title: '退貨案件' } })
    expect(wrapper.find('.ds-page-header__description').exists()).toBe(false)
  })

  it('labels the breadcrumb nav from the consumer-supplied breadcrumbAriaLabel', () => {
    const wrapper = mount(PageHeader, {
      props: {
        title: '案件詳情',
        breadcrumbAriaLabel: '麵包屑導覽',
        breadcrumbs: [
          { label: '售後', href: '/admin/returns' },
          { label: '退貨', href: '/admin/returns' },
          { label: 'RMA-260826-014' },
        ],
      },
    })
    const nav = wrapper.get('nav[aria-label="麵包屑導覽"]')
    expect(nav.findAll('a')).toHaveLength(2)
    expect(nav.get('[aria-current="page"]').text()).toBe('RMA-260826-014')
  })

  it('renders no unnamed <nav> when breadcrumbs are given without an aria label', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const wrapper = mount(PageHeader, {
      props: { title: 'X', breadcrumbs: [{ label: 'A', href: '/a' }, { label: 'B' }] },
    })
    expect(wrapper.find('nav').exists()).toBe(false)
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('[PageHeader]'))
  })

  it('needs no aria label and renders no <nav> when there are no breadcrumbs', () => {
    const wrapper = mount(PageHeader, { props: { title: 'X' } })
    expect(wrapper.find('nav').exists()).toBe(false)
  })

  it('renders the actions slot', () => {
    const wrapper = mount(PageHeader, {
      props: { title: '退貨案件' },
      slots: { actions: () => h('button', {}, '新增退貨') },
    })
    expect(wrapper.get('.ds-page-header__actions').text()).toContain('新增退貨')
  })

  it('keeps breadcrumb links as focusable anchors', () => {
    const wrapper = mount(PageHeader, {
      props: {
        title: 'X',
        breadcrumbAriaLabel: '導覽',
        breadcrumbs: [{ label: 'A', href: '/a' }, { label: 'B' }],
      },
    })
    const anchor = wrapper.get('nav a')
    expect(anchor.element.tagName).toBe('A')
    expect(anchor.attributes('href')).toBe('/a')
    expect(anchor.attributes('tabindex')).toBeUndefined()
  })
})

describe('StatusBadge', () => {
  const cases: Array<['in-progress' | 'waiting' | 'complete' | 'failed' | 'stopped', string]> = [
    ['in-progress', 'ds-status-badge--in-progress'],
    ['waiting', 'ds-status-badge--waiting'],
    ['complete', 'ds-status-badge--complete'],
    ['failed', 'ds-status-badge--failed'],
    ['stopped', 'ds-status-badge--stopped'],
  ]

  it.each(cases)('maps semantic status "%s" to its modifier class', (status, modifierClass) => {
    const wrapper = mount(StatusBadge, { props: { status, label: '狀態' } })
    expect(wrapper.classes()).toContain('ds-status-badge')
    expect(wrapper.classes()).toContain(modifierClass)
  })

  it('renders the required label prop', () => {
    expect(
      mount(StatusBadge, { props: { status: 'complete', label: '已退款' } }).text(),
    ).toBe('已退款')
  })

  it('lets the default slot override the label', () => {
    expect(
      mount(StatusBadge, {
        props: { status: 'waiting', label: '等待中' },
        slots: { default: () => '待客戶回覆' },
      }).text(),
    ).toBe('待客戶回覆')
  })

  it('carries no inline colour styles (all styling is class + token based)', () => {
    const wrapper = mount(StatusBadge, { props: { status: 'failed', label: '失敗' } })
    expect(wrapper.attributes('style')).toBeUndefined()
  })
})

describe('StatusBadge borders map one semantic token per state', () => {
  const css = styleBlock(statusBadgeSource)

  const borders: Array<[string, string]> = [
    ['in-progress', '--color-info-border'],
    ['waiting', '--color-butter-line'],
    ['complete', '--color-success-border'],
    ['failed', '--color-danger-border'],
    ['stopped', '--color-border'],
  ]

  it.each(borders)('gives "%s" border-color var(%s)', (status, token) => {
    const body = ruleBody(css, `.ds-status-badge--${status}`)
    expect(body).toMatch(new RegExp(`border-color:\\s*var\\(${token}\\)`))
  })

  it('no longer derives any border from color-mix()', () => {
    expect(css).not.toMatch(/color-mix/)
  })

  it('hardcodes no colour and needs no !important', () => {
    expect(css).not.toMatch(/#[0-9a-f]{3,8}\b/i)
    expect(css).not.toMatch(/!important/)
  })
})

describe('design tokens', () => {
  it('defines --color-info-border in the Light palette', () => {
    expect(ruleBody(tokensCss, ':root')).toMatch(/--color-info-border:\s*var\(--cyan-200\)/)
  })

  it('defines --color-info-border in the opt-in Dark block too', () => {
    expect(ruleBody(tokensCss, ':root[data-theme="dark"]')).toMatch(/--color-info-border:\s*#/)
  })

  it('resolves --color-success-border and --color-success-bg to different primitives', () => {
    const light = ruleBody(tokensCss, ':root')
    const bgPrimitive = light.match(/--color-success-bg:\s*var\((--[a-z0-9-]+)\)/)?.[1]
    const borderPrimitive = light.match(/--color-success-border:\s*var\((--[a-z0-9-]+)\)/)?.[1]
    expect(bgPrimitive).toBeTruthy()
    expect(borderPrimitive).toBeTruthy()
    expect(borderPrimitive).not.toBe(bgPrimitive)
  })

  it('defines --color-success-border in the opt-in Dark block too', () => {
    const dark = ruleBody(tokensCss, ':root[data-theme="dark"]')
    expect(dark).toMatch(/--color-success-border:\s*#[0-9a-f]{3,8}/i)
    const bg = dark.match(/--color-success-bg:\s*(#[0-9a-f]{3,8})/i)?.[1]
    const border = dark.match(/--color-success-border:\s*(#[0-9a-f]{3,8})/i)?.[1]
    expect(border).toBeTruthy()
    expect(border).not.toBe(bg)
  })
})

describe('shared components ship no user-facing copy', () => {
  // CJK ideographs, CJK punctuation and fullwidth forms. Built from escapes so the
  // pattern source stays ASCII (a literal fullwidth space is irregular whitespace).
  const cjk = new RegExp('[\\u4E00-\\u9FFF\\u3000-\\u303F\\uFF00-\\uFFEF]')

  const sources: Array<[string, string]> = [
    ['AppButton.vue', appButtonSource],
    ['StatusBadge.vue', statusBadgeSource],
    ['PageHeader.vue', pageHeaderSource],
    ['FormField.vue', formFieldSource],
  ]

  it.each(sources)('%s contains no hardcoded CJK text', (_name, source) => {
    expect(source).not.toMatch(cjk)
  })
})

describe('FormField', () => {
  const mountField = (props: {
    label: string
    required?: boolean
    description?: string
    error?: string
    id?: string
  }) =>
    mount(FormField, {
      props,
      slots: {
        default: (slotProps) => h('input', { ...slotProps, type: 'text' }),
      },
    })

  it('associates the label with the control via matching for/id', () => {
    const wrapper = mountField({ label: '電子郵件' })
    const id = wrapper.get('input').attributes('id')
    expect(id).toBeTruthy()
    expect(wrapper.get('label').attributes('for')).toBe(id)
  })

  it('respects an explicit id', () => {
    const wrapper = mountField({ label: 'X', id: 'contact-email' })
    expect(wrapper.get('input').attributes('id')).toBe('contact-email')
    expect(wrapper.get('label').attributes('for')).toBe('contact-email')
  })

  it('links the description through aria-describedby', () => {
    const wrapper = mountField({ label: 'X', description: '不會對外公開' })
    const descriptionId = wrapper.get('.ds-form-field__description').attributes('id')
    expect(descriptionId).toBeTruthy()
    expect(wrapper.get('input').attributes('aria-describedby')).toBe(descriptionId)
  })

  it('adds the error to aria-describedby and sets aria-invalid when errored', () => {
    const wrapper = mountField({ label: 'X', description: '說明', error: '此欄位必填' })
    const input = wrapper.get('input')
    const descriptionId = wrapper.get('.ds-form-field__description').attributes('id')
    const errorId = wrapper.get('.ds-form-field__error').attributes('id')
    expect(input.attributes('aria-describedby')).toBe(`${descriptionId} ${errorId}`)
    expect(input.attributes('aria-invalid')).toBe('true')
    expect(wrapper.get('.ds-form-field__error').attributes('role')).toBe('alert')
  })

  it('omits aria-invalid when there is no error', () => {
    const wrapper = mountField({ label: 'X' })
    expect(wrapper.get('input').attributes('aria-invalid')).toBeUndefined()
  })

  it('gives a required control the native required attribute', () => {
    const input = mountField({ label: '姓名', required: true }).get('input')
    expect(input.attributes('required')).toBeDefined()
  })

  it('gives a required control aria-required="true"', () => {
    const input = mountField({ label: '姓名', required: true }).get('input')
    expect(input.attributes('aria-required')).toBe('true')
  })

  it('emits neither attribute when the field is not required', () => {
    const input = mountField({ label: '姓名' }).get('input')
    expect(input.attributes('required')).toBeUndefined()
    expect(input.attributes('aria-required')).toBeUndefined()
  })

  it('keeps the visible asterisk decorative and ships no "(required)" copy', () => {
    const wrapper = mountField({ label: '姓名', required: true })
    const mark = wrapper.get('.ds-form-field__required-mark')
    expect(mark.text()).toBe('*')
    expect(mark.attributes('aria-hidden')).toBe('true')
    expect(wrapper.text()).not.toContain('必填')
  })
})
