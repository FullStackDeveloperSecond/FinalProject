import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import LoginForm from './LoginForm.vue'

const { loginMember, fetchSession, logoutMember, requestEmailVerification, push, routeQuery } = vi.hoisted(() => ({
  loginMember: vi.fn(),
  fetchSession: vi.fn(),
  logoutMember: vi.fn(),
  requestEmailVerification: vi.fn(),
  push: vi.fn(),
  routeQuery: {} as Record<string, unknown>,
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return { ...actual, loginMember, fetchSession, logoutMember, requestEmailVerification }
})

// 組長 PR #35 review, item 2: LoginForm now also calls useRoute() (to read a `redirect` query
// value) — the real vue-router plugin isn't installed in this test, so both hooks are faked the
// same way, not just useRouter.
vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return { ...actual, useRouter: () => ({ push }), useRoute: () => ({ query: routeQuery }) }
})

const globalStubs = { RouterLink: { template: '<a><slot /></a>' } }

describe('LoginForm', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    loginMember.mockClear()
    push.mockClear()
    for (const key of Object.keys(routeQuery)) {
      delete routeQuery[key]
    }
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

  /**
   * 組長 PR #35 review, item 2: a shopper sent here from NewBuildPage (guest draft → login) must
   * land back on that exact page, not always the home page.
   */
  it('navigates to a safe redirect target from the query string on success', async () => {
    routeQuery.redirect = '/builds/new'
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

    expect(push).toHaveBeenCalledWith('/builds/new')
  })

  it('falls back to / for an unsafe redirect target (e.g. an absolute external URL)', async () => {
    routeQuery.redirect = 'https://evil.example/phish'
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

  it('shows an error and re-enables retry when resending the verification email fails', async () => {
    // handleResendVerification used to swallow every failure and just fall back to 'idle' with no
    // feedback (Alex review, 2026-08-24) — the user had no way to know the resend never happened.
    loginMember.mockRejectedValueOnce(new ApiError('Forbidden', {
      status: 403,
      code: 'account_email_unverified',
    }))
    requestEmailVerification.mockRejectedValueOnce(new ApiError('Too Many Requests', {
      status: 429,
      code: 'rate_limit_exceeded',
    }))
    const wrapper = mount(LoginForm, { global: { stubs: globalStubs } })

    await wrapper.get('#login-email').setValue('member@example.com')
    await wrapper.get('#login-password').setValue('correct-horse-battery-staple')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const resendButton = wrapper.get('.resend-verification')
    await resendButton.trigger('click')
    await flushPromises()

    expect(requestEmailVerification).toHaveBeenCalledWith({ email: 'member@example.com' })
    expect(wrapper.text()).toContain('請求過於頻繁')
    expect(resendButton.attributes('disabled')).toBeUndefined()
  })
})
