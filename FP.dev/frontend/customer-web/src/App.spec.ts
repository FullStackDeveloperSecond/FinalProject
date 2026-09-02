import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
import App from './App.vue'
import { useSessionStore } from './stores/session'

async function mountAppAt(path: string) {
  const page = { template: '<div>頁面內容</div>' }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: page },
      { path: '/products', component: page },
      { path: '/support', component: page },
      { path: '/support/tickets', component: page },
      { path: '/support/tickets/:ticketId', component: page },
    ],
  })
  const pinia = createPinia()
  setActivePinia(pinia)
  const sessionStore = useSessionStore()
  sessionStore.status = 'anonymous'
  sessionStore.refresh = vi.fn().mockResolvedValue(undefined)

  await router.push(path)
  await router.isReady()

  // App.vue calls useCartIdentityCacheCleanup() (組長 PR #29 round-6 review, P1) — it needs a
  // real QueryClient in context, same as any other page that touches TanStack Query.
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return {
    router,
    wrapper: mount(App, { global: { plugins: [pinia, router, [VueQueryPlugin, { queryClient }]] } }),
  }
}

describe('App support navigation', () => {
  it.each([
    '/support',
    '/support/tickets',
    '/support/tickets/018f2e6a-0000-7000-8000-000000000001',
  ])('marks the support link as the current page at %s', async (path) => {
    const { wrapper } = await mountAppAt(path)
    const supportLink = wrapper.get('a[href="/support"]')

    expect(supportLink.attributes('aria-current')).toBe('page')
    expect(supportLink.classes()).toContain('router-link-active')
  })

  it('clears the support current-page semantics outside the support section', async () => {
    const { router, wrapper } = await mountAppAt('/support/tickets')

    await router.push('/')
    await flushPromises()

    const supportLink = wrapper.get('a[href="/support"]')
    expect(supportLink.attributes('aria-current')).toBeUndefined()
    expect(supportLink.classes()).not.toContain('router-link-active')
  })
})

describe('App header identity state (組長 PR #29 round 7 review, P1)', () => {
  /**
   * A failed session refresh (status 'error') must not silently render the same as a confirmed
   * guest — showing "登入／註冊" there would look like a normal signed-out visitor when the app
   * actually has no idea whether they're signed in or not (a real member Cookie may still be
   * valid). 'anonymous' keeps the normal login/register link; 'error' gets its own distinct
   * retry affordance instead.
   */
  it('shows 登入／註冊 for a confirmed anonymous session', async () => {
    const { wrapper } = await mountAppAt('/')
    useSessionStore().status = 'anonymous'
    await flushPromises()

    expect(wrapper.text()).toContain('登入／註冊')
    expect(wrapper.text()).not.toContain('無法確認登入狀態')
  })

  it('shows a distinct retry affordance, not 登入／註冊, when the session failed to resolve', async () => {
    const { wrapper } = await mountAppAt('/')
    useSessionStore().status = 'error'
    await flushPromises()

    expect(wrapper.text()).not.toContain('登入／註冊')
    expect(wrapper.text()).toContain('無法確認登入狀態')
  })

  it('retrying calls sessionStore.refresh() again', async () => {
    const { wrapper } = await mountAppAt('/')
    const sessionStore = useSessionStore()
    sessionStore.status = 'error'
    await flushPromises()
    const callsBeforeRetry = (sessionStore.refresh as ReturnType<typeof vi.fn>).mock.calls.length

    const retryButton = wrapper.findAll('button').find((button) => button.text().includes('重試'))!
    await retryButton.trigger('click')

    expect((sessionStore.refresh as ReturnType<typeof vi.fn>).mock.calls.length).toBe(callsBeforeRetry + 1)
  })
})


describe('行動版導覽在路由變更後關閉', () => {
  const toggleOf = (wrapper: { findAll: (s: string) => Array<{ text: () => string }> }) =>
    wrapper.findAll('button').find(button => button.text().includes('選單'))!

  it('展開後切換路由，選單關閉且 aria-expanded 回到 false', async () => {
    const { wrapper, router } = await mountAppAt('/')
    const toggle = toggleOf(wrapper) as unknown as {
      trigger: (e: string) => Promise<void>
      attributes: (name: string) => string | undefined
    }

    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('true')
    expect(wrapper.find('.primary-nav--open').exists()).toBe(true)

    await router.push('/support')
    await flushPromises()

    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(wrapper.find('.primary-nav--open').exists()).toBe(false)
  })

  it('只有 query 改變也算導覽，一樣要關閉選單', async () => {
    // 首頁分類卡去的是 /products?category=CPU —— path 沒變、只有 query 變，
    // 監聽 route.path 會漏掉這種情況，所以實作監聽的是 route.fullPath。
    const { wrapper, router } = await mountAppAt('/')
    const toggle = toggleOf(wrapper) as unknown as {
      trigger: (e: string) => Promise<void>
      attributes: (name: string) => string | undefined
    }

    await router.push('/products')
    await flushPromises()
    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('true')

    await router.push({ path: '/products', query: { category: 'CPU' } })
    await flushPromises()

    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(wrapper.find('.primary-nav--open').exists()).toBe(false)
  })

  it('點 RouterLink 導覽也會關閉選單', async () => {
    const { wrapper } = await mountAppAt('/')
    const toggle = toggleOf(wrapper) as unknown as {
      trigger: (e: string) => Promise<void>
      attributes: (name: string) => string | undefined
    }

    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('true')

    const supportLink = wrapper.findAll('a').find(link => link.text().includes('客服中心'))!
    await supportLink.trigger('click')
    await flushPromises()

    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(wrapper.find('.primary-nav--open').exists()).toBe(false)
  })
})


describe('Header 品牌連結的可及名稱只出現一次', () => {
  it('標記是裝飾圖片，品牌名由旁邊的文字承載', async () => {
    const { wrapper } = await mountAppAt('/')
    const brandLink = wrapper.get('a.brand-link')
    const img = brandLink.get('img')

    // 圖片是裝飾：空 alt，不進 accessible name 的計算
    expect(img.attributes('alt')).toBe('')
    // 可見文字只出現一次
    expect(brandLink.text().replace(/s+/g, '')).toBe('DoSelect懂選')
  })
})
