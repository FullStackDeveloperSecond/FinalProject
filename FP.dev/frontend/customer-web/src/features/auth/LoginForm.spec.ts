import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import LoginForm from './LoginForm.vue'

const { loginMember, fetchSession, logoutMember, push } = vi.hoisted(() => ({
  loginMember: vi.fn(),
  fetchSession: vi.fn(),
  logoutMember: vi.fn(),
  push: vi.fn(),
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return { ...actual, loginMember, fetchSession, logoutMember }
})

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return { ...actual, useRouter: () => ({ push }) }
})

const globalStubs = { RouterLink: { template: '<a><slot /></a>' } }

describe('LoginForm', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    loginMember.mockClear()
    push.mockClear()
  })

  it('logs in and navigates home on success', async () => {
    loginMember.mockResolvedValueOnce({
      isAuthenticated: true,
      user: {
        publicId: '018f1f0a-70d1-7c53-9a3f-000000000000',
        displayName: '王小明',
        emailMasked: 'm***@example.com',
        emailVerified: true,
        locale: 'zh-TW',
      },
    })
    const wrapper = mount(LoginForm, { global: { stubs: globalStubs } })

    await wrapper.get('#login-email').setValue('member@example.com')
    await wrapper.get('#login-password').setValue('correct-horse-battery-staple')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(loginMember).toHaveBeenCalledWith({
      email: 'member@example.com',
      password: 'correct-horse-battery-staple',
      rememberMe: false,
    })
    expect(push).toHaveBeenCalledWith('/')
  })

  it('shows a generic message for invalid credentials without naming the field', async () => {
    // The API returns this same invalid_credentials code whether the password was wrong, the
    // account does not exist, or the account is internally locked out (AuthController.Login) —
    // the frontend has no separate "locked" code to branch on, by design.
    loginMember.mockRejectedValueOnce(new ApiError('Unauthorized', {
      status: 401,
      code: 'invalid_credentials',
    }))
    const wrapper = mount(LoginForm, { global: { stubs: globalStubs } })

    await wrapper.get('#login-email').setValue('member@example.com')
    await wrapper.get('#login-password').setValue('wrong-password')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('Email 或密碼錯誤')
    expect(push).not.toHaveBeenCalled()
  })
})
