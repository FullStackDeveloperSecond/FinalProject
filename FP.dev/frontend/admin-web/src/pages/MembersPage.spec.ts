import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'
import MembersPage from './MembersPage.vue'

const { listMembers, getMember, changeMemberStatus } = vi.hoisted(() => ({
  listMembers: vi.fn(),
  getMember: vi.fn(),
  changeMemberStatus: vi.fn(),
}))

vi.mock('../features/members/api', () => ({ listMembers, getMember, changeMemberStatus }))

const member = {
  publicId: 'member-1',
  displayName: '測試會員',
  emailMasked: 'm***@example.invalid',
  status: 'Anonymized',
  emailVerified: true,
  createdAtUtc: '2026-09-01T00:00:00Z',
  updatedAtUtc: '2026-09-02T00:00:00Z',
  rowVersion: 'AAAA',
}

describe('MembersPage', () => {
  beforeEach(() => {
    listMembers.mockReset()
    getMember.mockReset()
    changeMemberStatus.mockReset()
    listMembers.mockResolvedValue({ items: [member], totalCount: 1, page: 1, pageSize: 20 })
    getMember.mockResolvedValue(member)
  })

  it('offers only the three shopper-facing account statuses and spaces the detail facts', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    useAdminAuthStore().session = {
      isAuthenticated: true,
      expiresAtUtc: '2026-09-10T12:00:00Z',
      requiresTwoFactor: null,
      user: {
        publicId: 'admin-1',
        displayName: '管理員',
        emailMasked: 'a***@example.invalid',
        emailVerified: true,
        locale: 'zh-TW',
        roles: ['SuperAdmin'],
      },
    }
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const wrapper = mount(MembersPage, {
      global: {
        plugins: [pinia, [VueQueryPlugin, { queryClient }]],
        stubs: { PagePager: true },
      },
    })
    await flushPromises()

    expect(wrapper.findAll('.members-filters select option').map(option => option.text()))
      .toEqual(['全部', '啟用', '停用', '待驗證'])
    expect(wrapper.get('tbody').text()).toContain('停用')

    await wrapper.get('tbody button').trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.member-detail__facts > div')).toHaveLength(4)
    expect(wrapper.find('.member-detail__action').exists()).toBe(false)
  })
})
