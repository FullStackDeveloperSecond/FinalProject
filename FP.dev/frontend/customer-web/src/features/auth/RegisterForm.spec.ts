import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import RegisterForm from './RegisterForm.vue'
import { clearRegistrationDraft } from './registrationDraft'

const { registerMember } = vi.hoisted(() => ({
  registerMember: vi.fn(),
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    registerMember,
  }
})

const globalStubs = { RouterLink: { template: '<a><slot /></a>' } }

async function fillValidForm(wrapper: ReturnType<typeof mount>): Promise<void> {
  await wrapper.get('#register-email').setValue('member@example.com')
  await wrapper.get('#register-password').setValue('correct-horse-battery-staple')
  await wrapper.get('#register-confirm-password').setValue('correct-horse-battery-staple')
  await wrapper.get('#register-display-name').setValue('王小明')
  await wrapper.get('#register-accept-terms').setValue(true)
}

describe('RegisterForm', () => {
  beforeEach(() => clearRegistrationDraft())

  it('keeps the in-memory draft when the form is remounted after reading a policy', async () => {
    const first = mount(RegisterForm, { global: { stubs: globalStubs } })
    await fillValidForm(first)
    first.unmount()

    const returned = mount(RegisterForm, { global: { stubs: globalStubs } })
    expect((returned.get('#register-display-name').element as HTMLInputElement).value).toBe('王小明')
    expect((returned.get('#register-email').element as HTMLInputElement).value).toBe('member@example.com')
    expect((returned.get('#register-password').element as HTMLInputElement).value).toBe('correct-horse-battery-staple')
    expect((returned.get('#register-confirm-password').element as HTMLInputElement).value).toBe('correct-horse-battery-staple')
    expect((returned.get('#register-accept-terms').element as HTMLInputElement).checked).toBe(true)
  })

  it('blocks submission when the confirm-password field does not match', async () => {
    registerMember.mockClear()
    const wrapper = mount(RegisterForm, { global: { stubs: globalStubs } })

    await fillValidForm(wrapper)
    await wrapper.get('#register-confirm-password').setValue('a-different-password')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('密碼與確認密碼不一致')
    expect(registerMember).not.toHaveBeenCalled()
  })

  it('blocks submission when the terms checkbox is not accepted', async () => {
    registerMember.mockClear()
    const wrapper = mount(RegisterForm, { global: { stubs: globalStubs } })

    await fillValidForm(wrapper)
    await wrapper.get('#register-accept-terms').setValue(false)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('請先閱讀並同意服務條款與隱私權政策')
    expect(registerMember).not.toHaveBeenCalled()
  })

  it('shows a pending-verification panel with the masked email after a successful submission', async () => {
    registerMember.mockClear()
    registerMember.mockResolvedValueOnce({
      publicId: '018f1f0a-70d1-7c53-9a3f-000000000000',
      emailMasked: 'm***@example.com',
      accountStatus: 'pendingEmailVerification',
    })
    const wrapper = mount(RegisterForm, { global: { stubs: globalStubs } })

    await fillValidForm(wrapper)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('m***@example.com')
    expect(registerMember).toHaveBeenCalledWith({
      email: 'member@example.com',
      password: 'correct-horse-battery-staple',
      displayName: '王小明',
      acceptTermsVersion: 1,
    })
  })

  it('shows the duplicate-email message under the email field on a 409 conflict', async () => {
    registerMember.mockClear()
    registerMember.mockRejectedValueOnce(new ApiError('Conflict', {
      status: 409,
      code: 'account_email_in_use',
    }))
    const wrapper = mount(RegisterForm, { global: { stubs: globalStubs } })

    await fillValidForm(wrapper)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('此 Email 已被註冊')
  })
})
