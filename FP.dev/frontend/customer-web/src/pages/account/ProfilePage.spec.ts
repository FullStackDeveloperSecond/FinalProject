import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'
import type { MemberProfile } from '../../features/members/api'

const mockFetchProfile = vi.fn<() => Promise<MemberProfile>>()
const mockUpdateProfile = vi.fn<(...args: unknown[]) => Promise<MemberProfile>>()

vi.mock('../../features/members/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../features/members/api')>()
  return {
    ...actual,
    fetchProfile: () => mockFetchProfile(),
    updateProfile: (...args: unknown[]) => mockUpdateProfile(...args),
  }
})

const baseProfile: MemberProfile = {
  publicId: 'member-1',
  displayName: '測試會員',
  emailMasked: 'm***@example.com',
  emailVerified: true,
  phone: '0912345678',
  locale: 'zh-TW',
  createdAtUtc: '2026-01-01T00:00:00Z',
  rowVersion: 'AAAA',
}

async function mountProfilePage() {
  const { default: ProfilePage } = await import('./ProfilePage.vue')
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return mount(ProfilePage, {
    global: { plugins: [[VueQueryPlugin, { queryClient }]] },
  })
}

beforeEach(() => {
  mockFetchProfile.mockReset()
  mockUpdateProfile.mockReset()
})

describe('ProfilePage', () => {
  it('shows the loading state before the profile resolves', async () => {
    mockFetchProfile.mockReturnValue(new Promise(() => {}))
    const wrapper = await mountProfilePage()

    expect(wrapper.text()).toContain('會員資料載入中')
  })

  it('renders the profile summary once loaded', async () => {
    mockFetchProfile.mockResolvedValueOnce(baseProfile)
    const wrapper = await mountProfilePage()
    await flushPromises()

    expect(wrapper.text()).toContain('m***@example.com')
    expect(wrapper.text()).toContain('測試會員')
    expect(wrapper.text()).toContain('0912345678')
    expect(wrapper.text()).toContain('繁體中文')
  })

  it('switches to edit mode, saves, and returns to the summary view', async () => {
    mockFetchProfile.mockResolvedValueOnce(baseProfile)
    mockUpdateProfile.mockResolvedValueOnce({ ...baseProfile, displayName: '新名稱' })
    const wrapper = await mountProfilePage()
    await flushPromises()

    await wrapper.get('button').trigger('click')
    await wrapper.get('#profile-display-name').setValue('新名稱')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(mockUpdateProfile).toHaveBeenCalledWith({
      displayName: '新名稱',
      phone: '0912345678',
      locale: 'zh-TW',
      rowVersion: 'AAAA',
    })
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.text()).toContain('新名稱')
  })

  it('shows a concurrency-conflict message and stays in edit mode on save failure', async () => {
    mockFetchProfile.mockResolvedValueOnce(baseProfile)
    mockUpdateProfile.mockRejectedValueOnce(new ApiError('Conflict', {
      status: 409,
      code: 'concurrency_conflict',
    }))
    const wrapper = await mountProfilePage()
    await flushPromises()

    await wrapper.get('button').trigger('click')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('會員資料已被更新')
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('discards edits when cancel is clicked', async () => {
    mockFetchProfile.mockResolvedValueOnce(baseProfile)
    const wrapper = await mountProfilePage()
    await flushPromises()

    await wrapper.get('button').trigger('click')
    await wrapper.get('#profile-display-name').setValue('未儲存的名稱')
    await wrapper.get('button[type="button"]').trigger('click')

    expect(mockUpdateProfile).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('測試會員')
    expect(wrapper.text()).not.toContain('未儲存的名稱')
  })
})
