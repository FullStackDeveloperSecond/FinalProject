import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import VerifyEmailForm from './VerifyEmailForm.vue'

const { confirmEmailVerification, useRoute, useRouter, routerReplace } = vi.hoisted(() => ({
  confirmEmailVerification: vi.fn(),
  useRoute: vi.fn(),
  useRouter: vi.fn(),
  routerReplace: vi.fn(),
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    confirmEmailVerification,
  }
})

vi.mock('vue-router', () => ({ useRoute, useRouter }))

const globalStubs = { RouterLink: { template: '<a><slot /></a>' } }

beforeEach(() => {
  routerReplace.mockReset().mockResolvedValue(undefined)
  useRouter.mockReturnValue({ replace: routerReplace })
})

describe('VerifyEmailForm', () => {
  it('shows a missing-link message when the publicId or token query param is absent', async () => {
    useRoute.mockReturnValue({ path: '/verify-email', query: {} })
    const wrapper = mount(VerifyEmailForm, { global: { stubs: globalStubs } })
    await flushPromises()

    expect(wrapper.text()).toContain('驗證連結不完整')
    expect(confirmEmailVerification).not.toHaveBeenCalled()
    expect(routerReplace).not.toHaveBeenCalled()
  })

  it('confirms the token from the query string and shows success', async () => {
    useRoute.mockReturnValue({
      path: '/verify-email',
      query: { publicId: '018f1f0a-70d1-7c53-9a3f-000000000000', token: 'a-token' },
    })
    confirmEmailVerification.mockResolvedValueOnce({ accountStatus: 'active' })
    const wrapper = mount(VerifyEmailForm, { global: { stubs: globalStubs } })
    await flushPromises()

    expect(confirmEmailVerification).toHaveBeenCalledWith({
      userPublicId: '018f1f0a-70d1-7c53-9a3f-000000000000',
      token: 'a-token',
    })
    expect(wrapper.text()).toContain('Email 驗證成功')
  })

  it('strips the publicId and token query params from the URL once they have been read', async () => {
    useRoute.mockReturnValue({
      path: '/verify-email',
      query: { publicId: '018f1f0a-70d1-7c53-9a3f-000000000000', token: 'a-token' },
    })
    confirmEmailVerification.mockResolvedValueOnce({ accountStatus: 'active' })
    mount(VerifyEmailForm, { global: { stubs: globalStubs } })
    await flushPromises()

    expect(routerReplace).toHaveBeenCalledWith({ path: '/verify-email' })
  })

  it('shows an actionable message when the token is rejected', async () => {
    useRoute.mockReturnValue({
      path: '/verify-email',
      query: { publicId: '018f1f0a-70d1-7c53-9a3f-000000000000', token: 'bad-token' },
    })
    confirmEmailVerification.mockRejectedValueOnce(new ApiError('Bad Request', {
      status: 400,
      code: 'email_token_invalid',
    }))
    const wrapper = mount(VerifyEmailForm, { global: { stubs: globalStubs } })
    await flushPromises()

    expect(wrapper.text()).toContain('驗證連結無效、已使用或已過期')
  })
})
