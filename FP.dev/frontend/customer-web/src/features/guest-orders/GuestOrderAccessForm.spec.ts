import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import GuestOrderAccessForm from './GuestOrderAccessForm.vue'

const { requestGuestOrderAccess, useRouter, routerPush } = vi.hoisted(() => ({
  requestGuestOrderAccess: vi.fn(),
  useRouter: vi.fn(),
  routerPush: vi.fn(),
}))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    requestGuestOrderAccess,
  }
})

vi.mock('vue-router', () => ({ useRouter }))

beforeEach(() => {
  routerPush.mockReset().mockResolvedValue(undefined)
  useRouter.mockReturnValue({ push: routerPush })
  requestGuestOrderAccess.mockReset()
})

describe('GuestOrderAccessForm', () => {
  it('requests access and navigates to the verify page with the returned request id', async () => {
    requestGuestOrderAccess.mockResolvedValueOnce({
      requestPublicId: '018f1f0a-70d1-7c53-9a3f-000000000000',
      expiresAtUtc: '2026-08-31T10:10:00Z',
      resendAvailableAtUtc: '2026-08-31T10:01:00Z',
    })
    const wrapper = mount(GuestOrderAccessForm)

    await wrapper.get('#guest-order-number').setValue('DS-2026-000123')
    await wrapper.get('#guest-order-email').setValue('someone@example.com')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(requestGuestOrderAccess).toHaveBeenCalledWith({
      orderNumber: 'DS-2026-000123',
      email: 'someone@example.com',
    })
    expect(routerPush).toHaveBeenCalledWith({
      name: 'guest-order-verify',
      query: {
        requestId: '018f1f0a-70d1-7c53-9a3f-000000000000',
        expiresAt: '2026-08-31T10:10:00Z',
      },
    })
  })

  it('shows a rate-limit message without revealing whether the order exists', async () => {
    requestGuestOrderAccess.mockRejectedValueOnce(new ApiError('Too Many Requests', {
      status: 429,
      code: 'rate_limit_exceeded',
    }))
    const wrapper = mount(GuestOrderAccessForm)

    await wrapper.get('#guest-order-number').setValue('DS-2026-000123')
    await wrapper.get('#guest-order-email').setValue('someone@example.com')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('請求過於頻繁')
    expect(routerPush).not.toHaveBeenCalled()
  })

  it('shows a generic failure message for a non-rate-limit error', async () => {
    requestGuestOrderAccess.mockRejectedValueOnce(new ApiError('Service Unavailable', {
      status: 503,
      code: 'service_unavailable',
    }))
    const wrapper = mount(GuestOrderAccessForm)

    await wrapper.get('#guest-order-number').setValue('DS-2026-000123')
    await wrapper.get('#guest-order-email').setValue('someone@example.com')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('查詢訂單時發生錯誤')
  })
})
