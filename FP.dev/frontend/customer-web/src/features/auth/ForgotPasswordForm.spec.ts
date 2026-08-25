import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import ForgotPasswordForm from './ForgotPasswordForm.vue'

const { requestPasswordReset } = vi.hoisted(() => ({
  requestPasswordReset: vi.fn(),
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    requestPasswordReset,
  }
})

const globalStubs = { RouterLink: { template: '<a><slot /></a>' } }

describe('ForgotPasswordForm', () => {
  it('shows a confirmation panel after a successful submission without revealing account existence', async () => {
    requestPasswordReset.mockResolvedValueOnce(undefined)
    const wrapper = mount(ForgotPasswordForm, { global: { stubs: globalStubs } })

    await wrapper.get('#forgot-password-email').setValue('someone@example.com')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(requestPasswordReset).toHaveBeenCalledWith({ email: 'someone@example.com' })
    expect(wrapper.text()).toContain('請查看您的信箱')
  })

  it('shows a rate-limit message without revealing account existence when throttled', async () => {
    // A previous version had no catch at all here, so a failed request left the user with a
    // re-enabled button and no indication anything went wrong (Alex review, 2026-08-24). The
    // message must still not say whether the email belongs to an account.
    requestPasswordReset.mockRejectedValueOnce(new ApiError('Too Many Requests', {
      status: 429,
      code: 'rate_limit_exceeded',
    }))
    const wrapper = mount(ForgotPasswordForm, { global: { stubs: globalStubs } })

    await wrapper.get('#forgot-password-email').setValue('someone@example.com')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('請求過於頻繁')
    expect(wrapper.text()).not.toContain('請查看您的信箱')
  })

  it('shows a generic failure message for a non-rate-limit error', async () => {
    requestPasswordReset.mockRejectedValueOnce(new ApiError('Service Unavailable', {
      status: 503,
      code: 'service_unavailable',
    }))
    const wrapper = mount(ForgotPasswordForm, { global: { stubs: globalStubs } })

    await wrapper.get('#forgot-password-email').setValue('someone@example.com')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('寄送重設連結時發生錯誤')
  })
})
