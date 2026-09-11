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

  it('lists administrators and creates a directly usable account that must enroll TOTP on first login', async () => {
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
    await dialog.get('#create-display-name').setValue('新管理員')
    await dialog.get('#create-employee-code').setValue('EMP-NEW')
    await dialog.get('#create-email').setValue('new@example.invalid')
    await dialog.get('#create-password').setValue('temporary-passphrase')
    await dialog.get('#create-password-confirmation').setValue('temporary-passphrase')
    const roleCheckbox = dialog.findAll('input[type="checkbox"]')
      .find(input => input.element.parentElement?.textContent?.includes('訂單與物流'))
    await roleCheckbox!.setValue(true)
    await dialog.get('form').trigger('submit')
    await flushPromises()

    expect(api.createAdminAccount).toHaveBeenCalledWith({
      email: 'new@example.invalid',
      password: 'temporary-passphrase',
      displayName: '新管理員',
      employeeCode: 'EMP-NEW',
      roles: ['OrderManager'],
      confirmSuperAdmin: false,
    })
    expect(wrapper.text()).toContain('首次登入時必須設定 TOTP')
    expect(dialog.findAll('input[type="password"]')).toHaveLength(2)
    expect(dialog.findAll('.admin-accounts__control')).toHaveLength(5)
    expect(dialog.findAll('.admin-accounts__checkbox')).toHaveLength(10)
  })

  it('does not submit when the password confirmation does not match', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const wrapper = mount(AdminAccountsPage, {
      global: {
        plugins: [[VueQueryPlugin, { queryClient }]],
        stubs: { PagePager: true },
      },
    })
    await flushPromises()
    await wrapper.get('.admin-accounts__header button').trigger('click')

    const dialog = wrapper.get('dialog')
    await dialog.get('#create-display-name').setValue('新管理員')
    await dialog.get('#create-employee-code').setValue('EMP-NEW')
    await dialog.get('#create-email').setValue('new@example.invalid')
    await dialog.get('#create-password').setValue('temporary-passphrase')
    await dialog.get('#create-password-confirmation').setValue('different-passphrase')
    await dialog.get('form').trigger('submit')

    expect(api.createAdminAccount).not.toHaveBeenCalled()
    expect(dialog.text()).toContain('兩次輸入的密碼不一致')
  })
})
