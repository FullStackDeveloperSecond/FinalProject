import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ProductCard from './ProductCard.vue'

describe('ProductCard', () => {
  it('compares string prices numerically and recovers image rendering when its URL changes', async () => {
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
    expect(wrapper.text()).toContain('尚無商品圖片')
    const product = wrapper.props('product')
    await wrapper.setProps({ product: { ...product, primaryImage: { url: '/broken.png', alt: '商品照片', width: 400, height: 300 } } })
    await wrapper.get('img').trigger('error')
    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.text()).toContain('圖片暫時無法載入')
    await wrapper.setProps({ product: { ...product, primaryImage: { url: '/replacement.png', alt: '商品照片', width: 400, height: 300 } } })
    expect(wrapper.get('img').attributes('src')).toBe('/replacement.png')
  })
})
