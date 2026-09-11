import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AdminInvitationPage from './AdminInvitationPage.vue'

const api = vi.hoisted(() => ({ acceptAdminInvitation: vi.fn() }))

vi.mock('../../adminAccounts/api', () => api)

describe('AdminInvitationPage', () => {
  beforeEach(() => {
    api.acceptAdminInvitation.mockReset()
    api.acceptAdminInvitation.mockResolvedValue(undefined)
  })

  it('uses the one-time fragment token, removes it from the address, and accepts the invitation', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/login/invitation', component: AdminInvitationPage },
        { path: '/login', component: { template: '<p>登入</p>' } },
      ],
    })
    await router.push('/login/invitation#publicId=018f2e6a-0000-7000-8000-000000000001&token=one%2Btime')
    await router.isReady()

    const wrapper = mount(AdminInvitationPage, { global: { plugins: [router] } })
    await flushPromises()
    expect(router.currentRoute.value.hash).toBe('')

    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue('StrongPassword123!')
    await inputs[1]!.setValue('StrongPassword123!')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(api.acceptAdminInvitation).toHaveBeenCalledWith({
      publicId: '018f2e6a-0000-7000-8000-000000000001',
      token: 'one+time',
      newPassword: 'StrongPassword123!',
    })
    expect(wrapper.text()).toContain('管理員帳號設定完成')
  })
})
