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

  /**
   * 組長 PR #78 round-2 review item 2：兩個物流入口的角色不同——門市是 ShippingRead
   * （OrderManager／CatalogManager／SuperAdmin），包裹限制是 ShippingManage（只有前者兩個）。
   * 無條件顯示等於給 CatalogManager 一個點下去只會被導到 /forbidden 的連結。
   */
  it.each([
    ['OrderManager'],
    ['SuperAdmin'],
  ])('shows both shipping entries to %s', async (role) => {
    signIn([role])

    const wrapper = await mountShell()

    expect(wrapper.text()).toContain('示範超商門市')
    expect(wrapper.text()).toContain('包裹限制版本')
  })

  it('shows only the stores entry to a CatalogManager', async () => {
    signIn(['CatalogManager'])

    const wrapper = await mountShell()

    expect(wrapper.text()).toContain('示範超商門市')
    expect(wrapper.text()).not.toContain('包裹限制版本')
  })

  it('hides both shipping entries when nobody is signed in', async () => {
    const wrapper = await mountShell()

    expect(wrapper.text()).not.toContain('示範超商門市')
    expect(wrapper.text()).not.toContain('包裹限制版本')
  })

  /**
   * 兩個匯入頁的角色不同：商品匯入是 CatalogImport.*（CatalogManager／SuperAdmin），庫存匯入是
   * InventoryAdjust.*（InventoryManager／SuperAdmin）。組長 PR #78 round-2 review item 2 的原則
   * ——不給一個點下去只會被導到 /forbidden 的入口。
   */
  it('shows only the catalog import entry to a CatalogManager', async () => {
    signIn(['CatalogManager'])

    const wrapper = await mountShell()

    expect(wrapper.text()).toContain('商品匯入')
    expect(wrapper.text()).not.toContain('庫存匯入')
  })

  it('shows only the inventory import entry to an InventoryManager', async () => {
    signIn(['InventoryManager'])

    const wrapper = await mountShell()

    expect(wrapper.text()).toContain('庫存匯入')
    expect(wrapper.text()).not.toContain('商品匯入')
  })

  it('hides both import entries when nobody is signed in', async () => {
    const wrapper = await mountShell()

    expect(wrapper.text()).not.toContain('商品匯入')
    expect(wrapper.text()).not.toContain('庫存匯入')
  })

  /**
   * 組長 PR #114 裁定 B1：對帳案件頁（A-29）的入口只給 InventoryManager／SuperAdmin——與 route meta
   * 和後端 InventoryManager Policy 相同；其他角色與未登入不該看到一個點下去只會被導到 /forbidden 的入口。
   */
  it.each([
    ['InventoryManager'],
    ['SuperAdmin'],
  ])('shows the inventory reconciliation entry to %s', async (role) => {
    signIn([role])

    const wrapper = await mountShell()

    expect(wrapper.text()).toContain('庫存對帳案件')
  })

  it.each([
    ['CatalogManager'],
    ['OrderManager'],
  ])('hides the inventory reconciliation entry from %s', async (role) => {
    signIn([role])

    const wrapper = await mountShell()

    expect(wrapper.text()).not.toContain('庫存對帳案件')
  })

  it('hides the inventory reconciliation entry when nobody is signed in', async () => {
    const wrapper = await mountShell()

    expect(wrapper.text()).not.toContain('庫存對帳案件')
  })
})
