import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import ForgotPasswordForm from './ForgotPasswordForm.vue'

const { requestPasswordReset, consumePendingForgotPasswordEmail } = vi.hoisted(() => ({
  requestPasswordReset: vi.fn(),
  consumePendingForgotPasswordEmail: vi.fn(),
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    requestPasswordReset,
  }
})

vi.mock('./forgotPasswordEmailHandoff', () => ({ consumePendingForgotPasswordEmail }))

const globalStubs = { RouterLink: { template: '<a><slot /></a>' } }

describe('ForgotPasswordForm', () => {
  it('prefills the email from the sessionStorage handoff left by LoginForm, not a URL query param', () => {
    consumePendingForgotPasswordEmail.mockReturnValueOnce('locked-out@example.com')
    const wrapper = mount(ForgotPasswordForm, { global: { stubs: globalStubs } })

    expect(consumePendingForgotPasswordEmail).toHaveBeenCalled()
    expect(wrapper.get<HTMLInputElement>('#forgot-password-email').element.value)
      .toBe('locked-out@example.com')
  })

  it('shows a confirmation panel after a successful submission without revealing account existence', async () => {
    consumePendingForgotPasswordEmail.mockReturnValueOnce('')
    requestPasswordReset.mockResolvedValueOnce(undefined)
    const wrapper = mount(ForgotPasswordForm, { global: { stubs: globalStubs } })

    await wrapper.get('#forgot-password-email').setValue('someone@example.com')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(requestPasswordReset).toHaveBeenCalledWith({ email: 'someone@example.com' })
    expect(wrapper.text()).toContain('請查看您的信箱')
  })
})
