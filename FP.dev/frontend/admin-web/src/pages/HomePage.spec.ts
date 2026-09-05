import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import HomePage from './HomePage.vue'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'

describe('HomePage', () => {
  beforeEach(() => {
    // 入口卡會依登入者角色決定是否列出營運報表，因此需要一個啟用中的 Pinia。
    setActivePinia(createPinia())
  })

  it('renders the administration foundation status', () => {
    const wrapper = mount(HomePage, {
      global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } },
    })

    expect(wrapper.get('h1').text()).toBe('管理工作台')
  })

  it('lists only shipped management pages, without inventing dashboard metrics', () => {
    useAdminAuthStore().session = {
      isAuthenticated: true,
      user: { publicId: 'admin-1', displayName: '客服', emailMasked: 'a***@example.test', emailVerified: true, locale: 'zh-TW', roles: ['CustomerService'] },
      expiresAtUtc: null,
      requiresTwoFactor: false,
    }
    const wrapper = mount(HomePage, {
      global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } },
    })

    const titles = wrapper.findAll('.home-card h3').map(node => node.text())
    expect(titles).toContain('客服 SLA 佇列')
    expect(titles).toContain('案件工作台')
    expect(titles).not.toContain('退貨案件')
    expect(titles).toContain('商品評價審核')
    expect(titles).not.toContain('商品管理')
    // 客服沒有報表或訂單管理角色。
    expect(titles).not.toContain('訂單管理')
    expect(titles).not.toContain('營運報表')
  })
})
