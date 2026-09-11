import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAdminAuthStore } from '../../features/auth/stores/useAdminAuthStore'
import AdminAccountsPage from './AdminAccountsPage.vue'

const api = vi.hoisted(() => ({
  listAdminAccounts: vi.fn(),
  createAdminAccount: vi.fn(),
  updateAdminRoles: vi.fn(),
  resendAdminInvitation: vi.fn(),
}))

vi.mock('../../features/adminAccounts/api', () => api)

const account = {
  publicId: '018f2e6a-0000-7000-8000-000000000001',
  displayName: '訂單管理員',
  employeeCode: 'EMP-001',
  email: 'orders@example.invalid',
  status: 'Active',
  emailVerified: true,
  twoFactorEnabled: true,
  roles: ['OrderManager'],
  createdAtUtc: '2026-09-01T00:00:00Z',
  updatedAtUtc: '2026-09-01T00:00:00Z',
  rowVersion: 'AQIDBAUGBwg=',
}

describe('AdminAccountsPage', () => {
  beforeEach(() => {
    api.listAdminAccounts.mockReset()
    api.createAdminAccount.mockReset()
    api.updateAdminRoles.mockReset()
    api.resendAdminInvitation.mockReset()
    api.listAdminAccounts.mockResolvedValue({
      items: [account],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      availableRoles: ['SuperAdmin', 'OrderManager'],
    })
    api.createAdminAccount.mockResolvedValue(account)
    HTMLDialogElement.prototype.showModal = function () { this.open = true }
    HTMLDialogElement.prototype.close = function () { this.open = false }

    const pinia = createPinia()
    setActivePinia(pinia)
    useAdminAuthStore().session = {
      isAuthenticated: true,
      user: {
        publicId: 'super-admin',
        displayName: '最高管理員',
        emailMasked: 's***@example.invalid',
        emailVerified: true,
        locale: 'zh-TW',
        roles: ['SuperAdmin'],
      },
      expiresAtUtc: null,
      requiresTwoFactor: false,
    }
  })

  it('lists administrators and creates a pending invitation without collecting a password', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const wrapper = mount(AdminAccountsPage, {
      global: {
        plugins: [[VueQueryPlugin, { queryClient }]],
        stubs: { PagePager: true },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('訂單管理員')
    expect(wrapper.text()).toContain('已綁定')
    await wrapper.get('.admin-accounts__header button').trigger('click')
    await flushPromises()

    const dialog = wrapper.get('dialog')
    expect(dialog.attributes()).toHaveProperty('open')
    await dialog.get('input:not([type="checkbox"])').setValue('新管理員')
    const textInputs = dialog.findAll('input:not([type="checkbox"])')
    await textInputs[1]!.setValue('EMP-NEW')
    await textInputs[2]!.setValue('new@example.invalid')
    const roleCheckbox = dialog.findAll('input[type="checkbox"]')
      .find(input => input.element.parentElement?.textContent?.includes('訂單與物流'))
    await roleCheckbox!.setValue(true)
    await dialog.get('form').trigger('submit')
    await flushPromises()

    expect(api.createAdminAccount).toHaveBeenCalledWith({
      email: 'new@example.invalid',
      displayName: '新管理員',
      employeeCode: 'EMP-NEW',
      roles: ['OrderManager'],
      confirmSuperAdmin: false,
    })
    expect(wrapper.text()).toContain('邀請信已交由郵件服務處理')
    expect(dialog.find('input[type="password"]').exists()).toBe(false)
  })
})
