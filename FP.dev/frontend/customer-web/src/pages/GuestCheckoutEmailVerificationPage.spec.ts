import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mockVerify = vi.fn()

vi.mock('../features/checkout/api', () => ({
  verifyGuestCheckoutEmail: (requestPublicId: string, code: string, guestCartKey: string) =>
    mockVerify(requestPublicId, code, guestCartKey),
}))

vi.mock('../features/cart/guestCartKey', () => ({
  getOrCreateGuestCartKey: () => 'guest-cart-key',
}))

beforeEach(() => {
  mockVerify.mockReset()
  window.history.replaceState(null, '', '/checkout/verify-email')
})

describe('GuestCheckoutEmailVerificationPage', () => {
  it('verifies the fragment payload on the dedicated Checkout page', async () => {
    mockVerify.mockResolvedValue(undefined)
    const requestPublicId = '11111111-1111-4111-8111-111111111111'
    window.history.replaceState(
      null,
      '',
      `/checkout/verify-email#requestPublicId=${requestPublicId}&code=123456`,
    )
    const { default: Page } = await import('./GuestCheckoutEmailVerificationPage.vue')
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/checkout/verify-email', component: Page },
        { path: '/checkout', component: { template: '<div>checkout</div>' } },
      ],
    })

    const wrapper = mount(Page, { global: { plugins: [router] } })

    await vi.waitFor(() => expect(wrapper.text()).toContain('信箱驗證完成'))
    expect(mockVerify).toHaveBeenCalledWith(requestPublicId, '123456', 'guest-cart-key')
    expect(window.location.pathname).toBe('/checkout/verify-email')
    expect(window.location.hash).toBe('')
    expect(wrapper.find('a[href="/guest-orders/access"]').exists()).toBe(false)
  })

  it('does not call the API when the link is incomplete', async () => {
    const { default: Page } = await import('./GuestCheckoutEmailVerificationPage.vue')
    const wrapper = mount(Page, {
      global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } },
    })

    await vi.waitFor(() => expect(wrapper.text()).toContain('驗證連結不完整'))
    expect(mockVerify).not.toHaveBeenCalled()
  })
})
