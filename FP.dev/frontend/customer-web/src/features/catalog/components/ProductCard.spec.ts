import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ProductCard from './ProductCard.vue'

describe('ProductCard', () => {
  it('compares string prices numerically when deciding whether to show a sale', () => {
    const wrapper = mount(ProductCard, {
      props: {
        product: {
          productPublicId: 'product-1',
          defaultSkuPublicId: 'sku-1',
          productCode: 'PRODUCT-1',
          skuCode: 'SKU-1',
          name: '測試商品',
          brand: { code: 'BRAND', name: '品牌' },
          category: { code: 'CATEGORY', name: '分類' },
          price: { list: '100', sale: '20', currency: 'TWD' },
          availability: 'inStock',
          primaryImage: null,
          badges: [],
        },
      },
      global: {
        stubs: {
          RouterLink: { template: '<a><slot /></a>' },
        },
      },
    })

    expect(wrapper.find('.product-card__price-original').text()).toBe('NT$100')
  })
})
