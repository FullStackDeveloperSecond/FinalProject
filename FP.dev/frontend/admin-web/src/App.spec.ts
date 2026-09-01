import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { beforeEach, describe, expect, it } from 'vitest'
import App from './App.vue'
import { useAdminAuthStore } from './features/auth/stores/useAdminAuthStore'

/**
 * 側欄入口是否依角色顯示。
 *
 * Route guard 仍然會擋越權，所以這**不是安全邊界** —— 這裡驗的是
 * 「不要讓使用者看到一個點下去只會被導到 /forbidden 的入口」（alex #64 P3）。
 */
describe('admin shell navigation', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  function signIn(roles: string[]) {
    useAdminAuthStore().session = {
      isAuthenticated: true,
      user: {
        publicId: 'admin-1',
        displayName: 'Admin',
        emailMasked: 'a***@example.test',
        emailVerified: true,
        locale: 'zh-TW',
        roles,
      },
      expiresAtUtc: null,
      requiresTwoFactor: false,
    }
  }

  async function mountShell(): Promise<ReturnType<typeof mount>> {
    const router: Router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:pathMatch(.*)*', component: { template: '<div />' } }],
    })
    await router.push('/')
    await router.isReady()

    return mount(App, { global: { plugins: [router] } })
  }

  it.each([
    ['FinanceManager'],
    ['MarketingAnalyst'],
    ['SuperAdmin'],
  ])('shows the coupon entry to %s', async (role) => {
    signIn([role])

    const wrapper = await mountShell()

    expect(wrapper.text()).toContain('優惠券管理')
  })

  it('hides the coupon entry from a role without Coupon.Manage', async () => {
    // CatalogManager 看得到側欄，但點優惠券只會被導到 /forbidden。
    signIn(['CatalogManager'])

    const wrapper = await mountShell()

    expect(wrapper.text()).not.toContain('優惠券管理')
    // 其他入口不受影響 —— 本次只處理新加入的那一個。
    expect(wrapper.text()).toContain('商品管理')
  })

  it('hides the coupon entry when nobody is signed in', async () => {
    const wrapper = await mountShell()

    expect(wrapper.text()).not.toContain('優惠券管理')
  })
})
