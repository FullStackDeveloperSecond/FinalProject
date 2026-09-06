import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { BrandMark } from '@doselect/web-shared/components'

/**
 * Header 的品牌連結長這樣：
 *
 *   <a><BrandMark /><span>DoSelect 懂選</span></a>
 *
 * 螢幕閱讀器算這個連結的 accessible name 時，會把圖片的 `alt` 和旁邊的可見文字
 * **都**串進去。兩邊都是「DoSelect 懂選」的話，品牌名會被念兩次。
 * 所以 header 內的標記必須是裝飾圖片（空 alt），由文字擔任唯一的名稱。
 */
describe('BrandMark', () => {
  it('keeps a real alt by default, so a standalone mark is still accessible', () => {
    const wrapper = mount(BrandMark)
    const img = wrapper.get('img')

    expect(img.attributes('alt')).toBe('DoSelect 懂選')
    wrapper.unmount()
  })

  it('renders an empty alt in decorative mode', () => {
    const wrapper = mount(BrandMark, { props: { decorative: true } })
    const img = wrapper.get('img')

    // 空字串，不是省略 —— 省略 alt 的圖片仍會被輔助技術當成未命名圖片朗讀
    expect(img.attributes('alt')).toBe('')
    expect(img.attributes()).toHaveProperty('alt')
    wrapper.unmount()
  })

  it('still downloads exactly one resource in either mode', () => {
    for (const decorative of [false, true]) {
      const wrapper = mount(BrandMark, { props: { decorative } })

      expect(wrapper.findAll('img')).toHaveLength(1)
      expect(wrapper.findAll('picture')).toHaveLength(1)
      expect(wrapper.get('source').attributes('type')).toBe('image/webp')
      wrapper.unmount()
    }
  })

  it('hides the missing-asset fallback from assistive tech when decorative', async () => {
    const wrapper = mount(BrandMark, { props: { decorative: true } })
    await wrapper.get('img').trigger('error')

    const fallback = wrapper.get('.brand-mark__slot')
    expect(fallback.attributes('aria-hidden')).toBe('true')
    expect(fallback.attributes('role')).toBeUndefined()
    expect(fallback.attributes('aria-label')).toBeUndefined()
    wrapper.unmount()
  })

  it('keeps the fallback announced when not decorative', async () => {
    const wrapper = mount(BrandMark)
    await wrapper.get('img').trigger('error')

    const fallback = wrapper.get('.brand-mark__slot')
    expect(fallback.attributes('role')).toBe('img')
    expect(fallback.attributes('aria-label')).toBe('DoSelect 懂選（正式 Logo 尚未匯入）')
    expect(fallback.attributes('aria-hidden')).toBeUndefined()
    wrapper.unmount()
  })

  it('falls back to a placeholder instead of a broken image', async () => {
    const wrapper = mount(BrandMark)
    expect(wrapper.find('.brand-mark__slot').exists()).toBe(false)

    await wrapper.get('img').trigger('error')

    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.get('.brand-mark__slot').text()).toBe('D')
    wrapper.unmount()
  })
})
