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
})
