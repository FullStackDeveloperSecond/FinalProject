import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import GuestOrderVerifyForm from './GuestOrderVerifyForm.vue'

const { verifyGuestOrderAccess, resendGuestOrderAccess, useRoute, useRouter, routerPush } = vi.hoisted(() => ({
  verifyGuestOrderAccess: vi.fn(),
  resendGuestOrderAccess: vi.fn(),
  useRoute: vi.fn(),
  useRouter: vi.fn(),
  routerPush: vi.fn(),
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    verifyGuestOrderAccess,
    resendGuestOrderAccess,
  }
})

vi.mock('vue-router', () => ({ useRoute, useRouter }))

const globalStubs = { RouterLink: { template: '<a><slot /></a>' } }

beforeEach(() => {
  routerPush.mockReset().mockResolvedValue(undefined)
  useRouter.mockReturnValue({ push: routerPush })
  verifyGuestOrderAccess.mockReset()
  resendGuestOrderAccess.mockReset()
})

describe('GuestOrderVerifyForm', () => {
  it('shows a way back to the access page when the request id query param is missing', async () => {
    useRoute.mockReturnValue({ query: {} })
    const wrapper = mount(GuestOrderVerifyForm, { global: { stubs: globalStubs } })

    expect(wrapper.text()).toContain('查無查詢請求')
    expect(verifyGuestOrderAccess).not.toHaveBeenCalled()
  })

  it('verifies the code and navigates to the order detail page on success', async () => {
    useRoute.mockReturnValue({
      query: {
        requestId: '018f1f0a-70d1-7c53-9a3f-000000000000',
        expiresAt: '2026-08-31T10:10:00Z',
      },
    })
    verifyGuestOrderAccess.mockResolvedValueOnce({
      orderPublicId: '018f1f0a-70d1-7c53-9a3f-000000000001',
      expiresAtUtc: '2026-08-31T10:40:00Z',
    })
    const wrapper = mount(GuestOrderVerifyForm, { global: { stubs: globalStubs } })

    await wrapper.get('#guest-order-code').setValue('123456')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(verifyGuestOrderAccess).toHaveBeenCalledWith({
      requestPublicId: '018f1f0a-70d1-7c53-9a3f-000000000000',
      code: '123456',
    })
    expect(routerPush).toHaveBeenCalledWith({
      name: 'order-detail',
      params: { orderId: '018f1f0a-70d1-7c53-9a3f-000000000001' },
    })
  })

  it('shows a generic error without distinguishing wrong code from an expired challenge', async () => {
    useRoute.mockReturnValue({
      query: { requestId: '018f1f0a-70d1-7c53-9a3f-000000000000' },
    })
    verifyGuestOrderAccess.mockRejectedValueOnce(new ApiError('Bad Request', {
      status: 400,
      code: 'verification_invalid',
    }))
    const wrapper = mount(GuestOrderVerifyForm, { global: { stubs: globalStubs } })

    await wrapper.get('#guest-order-code').setValue('000000')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('驗證碼錯誤或已過期')
    expect(routerPush).not.toHaveBeenCalled()
  })

  it('resends the code and shows a confirmation message', async () => {
    useRoute.mockReturnValue({
      query: { requestId: '018f1f0a-70d1-7c53-9a3f-000000000000' },
    })
    resendGuestOrderAccess.mockResolvedValueOnce({
      requestPublicId: '018f1f0a-70d1-7c53-9a3f-000000000000',
      expiresAtUtc: '2026-08-31T10:10:00Z',
      resendAvailableAtUtc: '2026-08-31T10:02:00Z',
    })
    const wrapper = mount(GuestOrderVerifyForm, { global: { stubs: globalStubs } })

    await wrapper.findAll('button').find(button => button.text().includes('重新寄送'))!.trigger('click')
    await flushPromises()

    expect(resendGuestOrderAccess).toHaveBeenCalledWith('018f1f0a-70d1-7c53-9a3f-000000000000')
    expect(wrapper.text()).toContain('已重新寄送驗證碼')
  })
})
