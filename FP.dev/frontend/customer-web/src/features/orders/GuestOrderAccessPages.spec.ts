import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import GuestOrderAccessPage from './GuestOrderAccessPage.vue'
import GuestOrderVerifyPage from './GuestOrderVerifyPage.vue'

const { requestGuestOrderAccess, resendGuestOrderAccess, verifyGuestOrderAccess, routerPush } = vi.hoisted(() => ({
  requestGuestOrderAccess: vi.fn(),
  resendGuestOrderAccess: vi.fn(),
  verifyGuestOrderAccess: vi.fn(),
  routerPush: vi.fn(),
}))

vi.mock('./guestAccessApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./guestAccessApi')>()
  return {
    ...actual,
    requestGuestOrderAccess,
    resendGuestOrderAccess,
    verifyGuestOrderAccess,
  }
})

let routeQuery: Record<string, string> = {}
vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRoute: () => ({ query: routeQuery }),
    useRouter: () => ({ push: routerPush }),
  }
})

describe('guest order access pages', () => {
  beforeEach(() => {
    routeQuery = {}
    requestGuestOrderAccess.mockReset()
    resendGuestOrderAccess.mockReset()
    verifyGuestOrderAccess.mockReset()
    routerPush.mockReset()
  })

  it('requests a challenge without revealing whether the order exists', async () => {
    requestGuestOrderAccess.mockResolvedValue({
      requestPublicId: 'request-public-id',
      expiresAtUtc: '2026-09-01T09:10:00Z',
      resendAvailableAtUtc: '2026-09-01T09:01:00Z',
    })
    const wrapper = mount(GuestOrderAccessPage)

    await wrapper.get('#guest-order-number').setValue('DS20260901001')
    await wrapper.get('#guest-order-email').setValue('buyer@example.com')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(requestGuestOrderAccess).toHaveBeenCalledWith({
      orderNumber: 'DS20260901001',
      email: 'buyer@example.com',
    })
    expect(wrapper.text()).toContain('若資料相符，驗證碼會寄到訂單 Email')
    expect(routerPush).toHaveBeenCalledWith({
      name: 'guest-order-verify',
      query: { requestPublicId: 'request-public-id' },
    })
  })

  it('verifies the six-digit code and opens only the authorized order', async () => {
    routeQuery = { requestPublicId: 'request-public-id' }
    verifyGuestOrderAccess.mockResolvedValue({
      orderPublicId: 'order-public-id',
      expiresAtUtc: '2026-09-01T09:30:00Z',
    })
    const wrapper = mount(GuestOrderVerifyPage)

    await wrapper.get('#guest-order-code').setValue('123456')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(verifyGuestOrderAccess).toHaveBeenCalledWith({
      requestPublicId: 'request-public-id',
      code: '123456',
    })
    expect(routerPush).toHaveBeenCalledWith({
      name: 'order-detail',
      params: { orderId: 'order-public-id' },
    })
  })

  it('keeps invalid verification errors on the same page', async () => {
    routeQuery = { requestPublicId: 'request-public-id' }
    verifyGuestOrderAccess.mockRejectedValue(new ApiError('Bad Request', {
      status: 400,
      code: 'guest_order_verification_invalid',
    }))
    const wrapper = mount(GuestOrderVerifyPage)

    await wrapper.get('#guest-order-code').setValue('123456')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('驗證碼無效或已過期')
    expect(routerPush).not.toHaveBeenCalled()
  })

  it('resends using only the opaque request id', async () => {
    routeQuery = { requestPublicId: 'request-public-id' }
    resendGuestOrderAccess.mockResolvedValue({
      requestPublicId: 'request-public-id',
      expiresAtUtc: '2026-09-01T09:10:00Z',
      resendAvailableAtUtc: '2026-09-01T09:01:00Z',
    })
    const wrapper = mount(GuestOrderVerifyPage)

    await wrapper.get('[data-test="resend-code"]').trigger('click')
    await flushPromises()

    expect(resendGuestOrderAccess).toHaveBeenCalledWith('request-public-id')
    expect(wrapper.text()).toContain('若資料仍有效，新的驗證碼已重新寄送')
  })
})
