import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ResetPasswordForm from './ResetPasswordForm.vue'

const { confirmPasswordReset, useRoute, useRouter, routerReplace } = vi.hoisted(() => ({
  confirmPasswordReset: vi.fn(),
  useRoute: vi.fn(),
  useRouter: vi.fn(),
  routerReplace: vi.fn(),
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    confirmPasswordReset,
  }
})

vi.mock('vue-router', () => ({ useRoute, useRouter }))

const globalStubs = { RouterLink: { template: '<a><slot /></a>' } }

beforeEach(() => {
  routerReplace.mockReset().mockResolvedValue(undefined)
  useRouter.mockReturnValue({ replace: routerReplace })
  confirmPasswordReset.mockReset()
})

async function fillMatchingPasswords(wrapper: ReturnType<typeof mount>): Promise<void> {
  await wrapper.get('#reset-password-new').setValue('correct-horse-battery-staple')
  await wrapper.get('#reset-password-confirm').setValue('correct-horse-battery-staple')
}

describe('ResetPasswordForm', () => {
  it('shows a missing-link message when the publicId or token query param is absent', () => {
    useRoute.mockReturnValue({ path: '/reset-password', query: {} })
    const wrapper = mount(ResetPasswordForm, { global: { stubs: globalStubs } })

    expect(wrapper.text()).toContain('重設連結不完整')
    expect(routerReplace).not.toHaveBeenCalled()
  })

  it('strips the publicId and token query params from the URL once mounted', async () => {
    useRoute.mockReturnValue({
      path: '/reset-password',
      query: { publicId: '018f1f0a-70d1-7c53-9a3f-000000000000', token: 'a-token' },
    })
    mount(ResetPasswordForm, { global: { stubs: globalStubs } })
    await flushPromises()

    expect(routerReplace).toHaveBeenCalledWith({ path: '/reset-password' })
  })

  it('submits the captured publicId and token even after the URL query has been cleared', async () => {
    useRoute.mockReturnValue({
      path: '/reset-password',
      query: { publicId: '018f1f0a-70d1-7c53-9a3f-000000000000', token: 'a-token' },
    })
    confirmPasswordReset.mockResolvedValueOnce(undefined)
    const wrapper = mount(ResetPasswordForm, { global: { stubs: globalStubs } })
    await flushPromises()

    await fillMatchingPasswords(wrapper)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(confirmPasswordReset).toHaveBeenCalledWith({
      userPublicId: '018f1f0a-70d1-7c53-9a3f-000000000000',
      token: 'a-token',
      newPassword: 'correct-horse-battery-staple',
    })
    expect(wrapper.text()).toContain('密碼已重設')
  })

  it('submits the publicId and token from the URL fragment', async () => {
    useRoute.mockReturnValue({
      path: '/reset-password',
      query: {},
      hash: '#publicId=018f1f0a-70d1-7c53-9a3f-000000000000&token=a-token',
    })
    confirmPasswordReset.mockResolvedValueOnce(undefined)
    const wrapper = mount(ResetPasswordForm, { global: { stubs: globalStubs } })
    await flushPromises()

    await fillMatchingPasswords(wrapper)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(confirmPasswordReset).toHaveBeenCalledWith({
      userPublicId: '018f1f0a-70d1-7c53-9a3f-000000000000',
      token: 'a-token',
      newPassword: 'correct-horse-battery-staple',
    })
    expect(routerReplace).toHaveBeenCalledWith({ path: '/reset-password' })
    expect(wrapper.text()).toContain('密碼已重設')
  })

  it('blocks submission when the passwords do not match', async () => {
    useRoute.mockReturnValue({
      path: '/reset-password',
      query: { publicId: '018f1f0a-70d1-7c53-9a3f-000000000000', token: 'a-token' },
    })
    const wrapper = mount(ResetPasswordForm, { global: { stubs: globalStubs } })
    await flushPromises()

    await wrapper.get('#reset-password-new').setValue('correct-horse-battery-staple')
    await wrapper.get('#reset-password-confirm').setValue('a-different-password')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('密碼與確認密碼不一致')
    expect(confirmPasswordReset).not.toHaveBeenCalled()
  })

  it('shows an actionable message when the token is rejected', async () => {
    useRoute.mockReturnValue({
      path: '/reset-password',
      query: { publicId: '018f1f0a-70d1-7c53-9a3f-000000000000', token: 'bad-token' },
    })
    confirmPasswordReset.mockRejectedValueOnce(new ApiError('Bad Request', {
      status: 400,
      code: 'password_reset_token_invalid',
    }))
    const wrapper = mount(ResetPasswordForm, { global: { stubs: globalStubs } })
    await flushPromises()

    await fillMatchingPasswords(wrapper)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('重設連結無效、已使用或已過期')
  })
})
