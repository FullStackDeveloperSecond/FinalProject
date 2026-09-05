/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { CursorPager, FilterBar, PagePager } from '@doselect/web-shared/components'
import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { h, nextTick } from 'vue'
import PrimeVue from 'primevue/config'
import Paginator from 'primevue/paginator'

const withPrimeVue = { global: { plugins: [PrimeVue] } }

// `npm test` runs vitest with cwd = this package root (frontend/customer-web).
const pkgRoot = process.cwd()
const readComponent = (name: string) =>
  readFileSync(resolve(pkgRoot, '../shared/src/components', name), 'utf8')

const sources: Array<[string, string]> = [
  ['FilterBar.vue', readComponent('FilterBar.vue')],
  ['CursorPager.vue', readComponent('CursorPager.vue')],
  ['PagePager.vue', readComponent('PagePager.vue')],
]

// CJK ideographs, CJK punctuation and fullwidth forms — built from escapes so the
// pattern source stays ASCII.
const cjk = new RegExp('[\\u4E00-\\u9FFF\\u3000-\\u303F\\uFF00-\\uFFEF]')

describe('2a components ship no hardcoded design values', () => {
  it.each(sources)('%s contains no user-facing CJK copy', (_name, source) => {
    expect(source).not.toMatch(cjk)
  })

  it.each(sources)('%s hardcodes no colour, !important, .p-* override or :deep()', (_name, source) => {
    const style = source.match(/<style[^>]*>([\s\S]*?)<\/style>/)?.[1] ?? ''
    expect(style).not.toMatch(/#[0-9a-f]{3,8}\b/i)
    expect(style).not.toMatch(/rgba?\(|hsla?\(/i)
    expect(style).not.toMatch(/!important/)
    expect(style).not.toMatch(/\.p-[a-z-]/)
    expect(source).not.toMatch(/:deep\(|::v-deep|\/deep\//)
  })

  it.each(sources)('%s does not import vue-router (stays presentational)', (_name, source) => {
    expect(source).not.toMatch(/from ['"]vue-router['"]/)
  })
})

describe('FilterBar', () => {
  it('renders the default slot (the page\'s own filter controls)', () => {
    const wrapper = mount(FilterBar, {
      ...withPrimeVue,
      props: { clearLabel: 'Clear' },
      slots: { default: () => h('input', { class: 'q-input' }) },
    })
    expect(wrapper.find('.q-input').exists()).toBe(true)
  })

  it('shows the clear button only when filters are active, with the supplied label', async () => {
    const wrapper = mount(FilterBar, { ...withPrimeVue, props: { clearLabel: '清除全部' } })
    expect(wrapper.find('button').exists()).toBe(false)

    await wrapper.setProps({ hasActiveFilters: true })
    const button = wrapper.get('button')
    expect(button.text()).toBe('清除全部')
  })

  it('emits "clear" when the clear button is activated', async () => {
    const wrapper = mount(FilterBar, {
      ...withPrimeVue,
      props: { clearLabel: 'Clear', hasActiveFilters: true },
    })
    await wrapper.get('button').trigger('click')
    expect(wrapper.emitted('clear')).toHaveLength(1)
  })

  it('renders the actions slot', () => {
    const wrapper = mount(FilterBar, {
      ...withPrimeVue,
      props: { clearLabel: 'Clear' },
      slots: { actions: () => h('button', { class: 'export' }, 'Export') },
    })
    expect(wrapper.find('.ds-filter-bar__actions .export').exists()).toBe(true)
  })

  it('is a labelled group only when an ariaLabel is supplied', () => {
    const unnamed = mount(FilterBar, { ...withPrimeVue, props: { clearLabel: 'Clear' } })
    expect(unnamed.find('.ds-filter-bar').attributes('role')).toBeUndefined()
    expect(unnamed.find('.ds-filter-bar').attributes('aria-label')).toBeUndefined()

    const named = mount(FilterBar, {
      ...withPrimeVue,
      props: { clearLabel: 'Clear', ariaLabel: 'Product filters' },
    })
    expect(named.find('.ds-filter-bar').attributes('role')).toBe('group')
    expect(named.find('.ds-filter-bar').attributes('aria-label')).toBe('Product filters')
  })
})

describe('CursorPager', () => {
  const base = {
    hasPrev: true,
    hasNext: true,
    prevLabel: 'Previous',
    nextLabel: 'Next',
    ariaLabel: 'Ticket pages',
  }

  it('names its navigation landmark from the ariaLabel prop', () => {
    const wrapper = mount(CursorPager, { ...withPrimeVue, props: base })
    expect(wrapper.get('nav').attributes('aria-label')).toBe('Ticket pages')
  })

  it('renders both button labels', () => {
    const wrapper = mount(CursorPager, { ...withPrimeVue, props: base })
    expect(wrapper.text()).toContain('Previous')
    expect(wrapper.text()).toContain('Next')
  })

  it('disables prev when there is no earlier page and next when there is no later page', () => {
    const wrapper = mount(CursorPager, {
      ...withPrimeVue,
      props: { ...base, hasPrev: false, hasNext: true },
    })
    const [prev, next] = wrapper.findAll('button')
    expect(prev.attributes('disabled')).toBeDefined()
    expect(next.attributes('disabled')).toBeUndefined()
  })

  it('disables both controls while busy', () => {
    const wrapper = mount(CursorPager, { ...withPrimeVue, props: { ...base, busy: true } })
    for (const button of wrapper.findAll('button')) {
      expect(button.attributes('disabled')).toBeDefined()
    }
  })

  it('emits "prev" / "next" on activation, and nothing while disabled', async () => {
    const wrapper = mount(CursorPager, { ...withPrimeVue, props: base })
    const [prev, next] = wrapper.findAll('button')
    await prev.trigger('click')
    await next.trigger('click')
    expect(wrapper.emitted('prev')).toHaveLength(1)
    expect(wrapper.emitted('next')).toHaveLength(1)

    const blocked = mount(CursorPager, {
      ...withPrimeVue,
      props: { ...base, hasPrev: false, hasNext: false },
    })
    const [bPrev, bNext] = blocked.findAll('button')
    await bPrev.trigger('click')
    await bNext.trigger('click')
    expect(blocked.emitted('prev')).toBeUndefined()
    expect(blocked.emitted('next')).toBeUndefined()
  })

  it('renders the status slot', () => {
    const wrapper = mount(CursorPager, {
      ...withPrimeVue,
      props: base,
      slots: { status: () => 'showing 1–20' },
    })
    expect(wrapper.get('.ds-cursor-pager__status').text()).toBe('showing 1–20')
  })
})

describe('PagePager', () => {
  const mountPager = (props: {
    page: number
    totalRecords: number
    pageSize: number
    ariaLabel?: string
  }) => mount(PagePager, { ...withPrimeVue, props: { ariaLabel: 'Pages', ...props } })

  it('converts the 1-based page into PrimeVue Paginator\'s 0-based `first` offset', () => {
    const wrapper = mountPager({ page: 3, totalRecords: 100, pageSize: 10 })
    // first = (3 - 1) * 10
    expect(wrapper.findComponent(Paginator).props('first')).toBe(20)
    expect(wrapper.findComponent(Paginator).props('rows')).toBe(10)
    expect(wrapper.findComponent(Paginator).props('totalRecords')).toBe(100)
  })

  it('re-emits Paginator page changes as a 1-based `update:page`', async () => {
    const wrapper = mountPager({ page: 1, totalRecords: 100, pageSize: 10 })
    await nextTick()
    // Paginator reports page 2 (0-based) => our consumer sees page 3 (1-based).
    wrapper.findComponent(Paginator).vm.$emit('page', { page: 2, first: 20, rows: 10 })
    expect(wrapper.emitted('update:page')).toEqual([[3]])
  })

  it('does not emit when the Paginator event matches the current page', async () => {
    const wrapper = mountPager({ page: 2, totalRecords: 100, pageSize: 10 })
    await nextTick()
    wrapper.findComponent(Paginator).vm.$emit('page', { page: 1, first: 10, rows: 10 })
    expect(wrapper.emitted('update:page')).toBeUndefined()
  })

  it('corrects an out-of-range page down to the last valid page', async () => {
    const wrapper = mountPager({ page: 5, totalRecords: 10, pageSize: 10 }) // only 1 page
    await nextTick()
    expect(wrapper.emitted('update:page')).toEqual([[1]])
  })

  it('applies the corrected page without emitting again (fixed point, no loop)', async () => {
    const wrapper = mountPager({ page: 9, totalRecords: 30, pageSize: 10 }) // pageCount 3
    await nextTick()
    expect(wrapper.emitted('update:page')).toEqual([[3]])
    await wrapper.setProps({ page: 3 })
    await nextTick()
    expect(wrapper.emitted('update:page')).toEqual([[3]]) // still just the one emit
  })

  it('re-clamps when pageSize grows enough to drop pages', async () => {
    const wrapper = mountPager({ page: 4, totalRecords: 40, pageSize: 10 }) // pageCount 4, page valid
    await nextTick()
    expect(wrapper.emitted('update:page')).toBeUndefined()
    await wrapper.setProps({ pageSize: 20 }) // pageCount 2 now, page 4 invalid
    await nextTick()
    expect(wrapper.emitted('update:page')).toEqual([[2]])
  })

  it('treats an empty result set as a single valid page 1', async () => {
    const wrapper = mountPager({ page: 1, totalRecords: 0, pageSize: 10 })
    await nextTick()
    expect(wrapper.emitted('update:page')).toBeUndefined()
  })

  it('names the paginator navigation landmark from the ariaLabel prop', async () => {
    const wrapper = mountPager({ page: 1, totalRecords: 30, pageSize: 10, ariaLabel: 'Report pages' })
    await nextTick()
    expect(wrapper.get('nav').attributes('aria-label')).toBe('Report pages')
  })
})

describe('PagePager defends against invalid numeric input', () => {
  const mountPager = (props: { page: number, totalRecords: number, pageSize: number }) =>
    mount(PagePager, { ...withPrimeVue, props: { ariaLabel: 'Pages', ...props } })

  // Every combination the component must survive without emitting or handing
  // PrimeVue an unusable number.
  const invalidCases: Array<[string, { page: number, totalRecords: number, pageSize: number }]> = [
    ['pageSize 0', { page: 1, totalRecords: 100, pageSize: 0 }],
    ['pageSize negative', { page: 2, totalRecords: 100, pageSize: -10 }],
    ['pageSize NaN', { page: 1, totalRecords: 100, pageSize: Number.NaN }],
    ['pageSize Infinity', { page: 1, totalRecords: 100, pageSize: Number.POSITIVE_INFINITY }],
    ['pageSize fractional', { page: 1, totalRecords: 100, pageSize: 10.5 }],
    ['totalRecords negative', { page: 1, totalRecords: -1, pageSize: 10 }],
    ['totalRecords NaN', { page: 1, totalRecords: Number.NaN, pageSize: 10 }],
    ['totalRecords Infinity', { page: 1, totalRecords: Number.POSITIVE_INFINITY, pageSize: 10 }],
    ['page NaN', { page: Number.NaN, totalRecords: 100, pageSize: 10 }],
    ['page Infinity', { page: Number.POSITIVE_INFINITY, totalRecords: 100, pageSize: 10 }],
    ['everything invalid', { page: Number.NaN, totalRecords: -5, pageSize: 0 }],
  ]

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllEnvs()
  })

  /** PrimeVue logs an unrelated licence notice through console.warn; ignore it. */
  const pagerWarnings = (warn: { mock: { calls: unknown[][] } }) =>
    warn.mock.calls.map((call) => String(call[0])).filter((m) => m.includes('[PagePager]'))

  it.each(invalidCases)('emits no update:page for %s', async (_name, props) => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const wrapper = mountPager(props)
    await nextTick()
    expect(wrapper.emitted('update:page')).toBeUndefined()
  })

  it.each(invalidCases)('hands Paginator only usable numbers for %s', async (_name, props) => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const paginator = mountPager(props).findComponent(Paginator)
    await nextTick()

    const rows = paginator.props('rows') as number
    const totalRecords = paginator.props('totalRecords') as number
    const first = paginator.props('first') as number

    // No NaN, no Infinity.
    expect(Number.isInteger(rows)).toBe(true)
    expect(Number.isInteger(totalRecords)).toBe(true)
    expect(Number.isInteger(first)).toBe(true)
    // rows drives a division inside Paginator; first is an offset.
    expect(rows).toBeGreaterThan(0)
    expect(totalRecords).toBeGreaterThanOrEqual(0)
    expect(first).toBeGreaterThanOrEqual(0)
  })

  it('falls back to rows 1 when pageSize is unusable', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const paginator = mountPager({ page: 1, totalRecords: 100, pageSize: 0 }).findComponent(Paginator)
    await nextTick()
    expect(paginator.props('rows')).toBe(1)
    expect(paginator.props('totalRecords')).toBe(100)
  })

  it('falls back to totalRecords 0 when totalRecords is unusable', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const paginator = mountPager({ page: 1, totalRecords: -1, pageSize: 10 }).findComponent(Paginator)
    await nextTick()
    expect(paginator.props('totalRecords')).toBe(0)
    expect(paginator.props('rows')).toBe(10)
    expect(paginator.props('first')).toBe(0)
  })

  it('renders page 1 (first = 0) when page itself is unusable', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const paginator = mountPager({ page: Number.NaN, totalRecords: 100, pageSize: 10 })
      .findComponent(Paginator)
    await nextTick()
    expect(paginator.props('first')).toBe(0)
  })

  it('stays silent when Paginator reports a page change while input is invalid', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const wrapper = mountPager({ page: 1, totalRecords: 100, pageSize: 0 })
    await nextTick()
    wrapper.findComponent(Paginator).vm.$emit('page', { page: 4, first: 4, rows: 1 })
    expect(wrapper.emitted('update:page')).toBeUndefined()
  })

  it('resumes emitting once the invalid prop is corrected', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const wrapper = mountPager({ page: 5, totalRecords: 10, pageSize: 0 })
    await nextTick()
    expect(wrapper.emitted('update:page')).toBeUndefined()

    await wrapper.setProps({ pageSize: 10 }) // pageCount 1, page 5 out of range
    await nextTick()
    expect(wrapper.emitted('update:page')).toEqual([[1]])
  })

  it('warns once per invalid prop in development', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    mountPager({ page: 1, totalRecords: -5, pageSize: 0 })
    await nextTick()
    const messages = pagerWarnings(warn)
    expect(messages.some((m) => m.includes('pageSize'))).toBe(true)
    expect(messages.some((m) => m.includes('totalRecords'))).toBe(true)
  })

  it('emits no warning outside development', async () => {
    vi.stubEnv('DEV', false)
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    mountPager({ page: Number.NaN, totalRecords: -5, pageSize: 0 })
    await nextTick()
    expect(pagerWarnings(warn)).toEqual([])
  })

  it('leaves valid input completely untouched', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const paginator = mountPager({ page: 3, totalRecords: 95, pageSize: 10 }).findComponent(Paginator)
    await nextTick()
    expect(paginator.props('rows')).toBe(10)
    expect(paginator.props('totalRecords')).toBe(95)
    expect(paginator.props('first')).toBe(20)
    expect(pagerWarnings(warn)).toEqual([])
  })
})
