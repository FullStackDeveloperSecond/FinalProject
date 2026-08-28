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
