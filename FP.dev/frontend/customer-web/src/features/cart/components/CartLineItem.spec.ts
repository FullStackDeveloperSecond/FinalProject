import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import CartLineItem from './CartLineItem.vue'
import type { CartItemDto } from '../types'

function cartItem(overrides: Partial<CartItemDto> = {}): CartItemDto {
  return {
    publicId: 'item-1',
    skuPublicId: 'sku-1',
    skuCode: 'SKU-1',
    name: 'RTX 4070',
    quantity: 2,
    unitPrice: 18000,
    lineTotal: 36000,
    availability: 'available',
    priceChanged: false,
    maxPurchasableQuantity: 10,
    assemblyGroupKey: null,
    rowVersion: 'AAAA',
    ...overrides,
  }
}

function mountItem(item: CartItemDto) {
  return mount(CartLineItem, { props: { item, pending: false } })
}

describe('CartLineItem quantity selector', () => {
  it('offers exactly 1..maxPurchasableQuantity when stock is sufficient', () => {
    const wrapper = mountItem(cartItem({ quantity: 2, maxPurchasableQuantity: 10 }))

    const options = wrapper.findAll('option')
    expect(options.map((option) => option.attributes('value'))).toEqual(
      Array.from({ length: 10 }, (_, index) => String(index + 1)),
    )
    expect(wrapper.find('select').attributes('disabled')).toBeUndefined()
  })

  /**
   * 組長 PR #29 review round 3, P3: stock dropping from 10 (already in the cart) to 2 used to
   * still offer every value 1..10 as if legal, even though only 1..2 would actually be accepted
   * without immediately re-triggering the same "insufficient stock" problem on revalidate.
   */
  it('when stock drops below the quantity already in the cart, only offers the legal range plus a disabled marker for the current quantity', () => {
    const wrapper = mountItem(cartItem({ quantity: 10, maxPurchasableQuantity: 2 }))

    const options = wrapper.findAll('option')
    const legalOptions = options.filter((option) => option.attributes('disabled') === undefined)
    expect(legalOptions.map((option) => option.attributes('value'))).toEqual(['1', '2'])

    const markerOption = options.find((option) => option.attributes('disabled') !== undefined)
    expect(markerOption?.attributes('value')).toBe('10')
    expect(markerOption?.text()).toContain('超過可購數量')

    // The select itself must stay usable so the shopper can actually pick a legal value.
    expect(wrapper.find('select').attributes('disabled')).toBeUndefined()
  })

  /** 組長 PR #29 review round 3, P3: maxPurchasableQuantity === 0 must disable adjustment entirely and explicitly guide the shopper to remove the item, not offer a range ending at the stale quantity. */
  it('disables quantity adjustment entirely and shows a removal hint when stock is fully unavailable', () => {
    const wrapper = mountItem(cartItem({ quantity: 3, maxPurchasableQuantity: 0 }))

    expect(wrapper.find('select').attributes('disabled')).toBeDefined()
    const legalOptions = wrapper.findAll('option').filter((option) => option.attributes('disabled') === undefined)
    expect(legalOptions).toHaveLength(0)
    expect(wrapper.text()).toContain('已無足夠庫存，請移除此品項。')
  })

  it('disables the select while a pending action is in flight, even with sufficient stock', () => {
    const wrapper = mount(CartLineItem, {
      props: { item: cartItem({ quantity: 2, maxPurchasableQuantity: 10 }), pending: true },
    })

    expect(wrapper.find('select').attributes('disabled')).toBeDefined()
  })
})
