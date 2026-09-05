import { mount } from '@vue/test-utils'
import { computed, nextTick } from 'vue'
import { describe, expect, it } from 'vitest'
import { crispPreset, dongguPreset, gentlePreset, motionPresetKey } from '@doselect/web-shared/motion'
import CaseSplitLayout from './CaseSplitLayout.vue'

/**
 * 動畫加上去之後，2/5 : 3/5 的版面契約必須原封不動：
 * 同層並排、不覆蓋列表、沒有 dialog／overlay、只由右上角關閉鈕收起。
 */

function mountLayout(detailOpen: boolean, preset = gentlePreset) {
  return mount(CaseSplitLayout, {
    props: { detailOpen, detailTitle: '案件 CS-0001', closeLabel: '關閉案件檢視' },
    slots: {
      list: '<table class="probe-list"><tbody><tr><td>列表</td></tr></tbody></table>',
      detail: '<p class="probe-detail">詳細內容</p>',
    },
    global: {
      provide: { [motionPresetKey as unknown as symbol]: computed(() => preset) },
      // VTU 預設會把 <Transition> stub 掉，那樣就驗不到 GSAP 的 enter／leave hook。
      stubs: { transition: false },
    },
    attachTo: document.body,
  })
}

describe('caseSplitLayout with motion', () => {
  it('keeps the list and the detail on the same layer, with no dialog or overlay', () => {
    const wrapper = mountLayout(true)

    expect(wrapper.find('.case-split__list .probe-list').exists()).toBe(true)
    expect(wrapper.find('.case-split__detail .probe-detail').exists()).toBe(true)

    // 沒有任何遮罩式容器。
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    expect(wrapper.find('[aria-modal="true"]').exists()).toBe(false)
    expect(wrapper.find('.p-dialog').exists()).toBe(false)
    expect(wrapper.find('.p-drawer').exists()).toBe(false)
    expect(wrapper.html()).not.toContain('overlay')

    wrapper.unmount()
  })

  it('drives the two-column grid from a data attribute, not from an inline width tween', () => {
    const closed = mountLayout(false)
    expect(closed.get('.case-split').attributes('data-detail-open')).toBe('false')
    expect(closed.find('.case-split__detail').exists()).toBe(false)
    closed.unmount()

    const open = mountLayout(true)
    expect(open.get('.case-split').attributes('data-detail-open')).toBe('true')

    // 動畫只碰 transform / opacity；不得對詳細欄寫入 width 或 grid 相關的 inline style。
    const style = open.get('.case-split__detail').attributes('style') ?? ''
    expect(style).not.toContain('width')
    expect(style).not.toContain('grid-template')
    expect(style).not.toContain('left')
    expect(style).not.toContain('top')

    open.unmount()
  })

  it('closes only through the close button, for every preset', async () => {
    for (const preset of [gentlePreset, dongguPreset, crispPreset]) {
      const wrapper = mountLayout(true, preset)

      // 點列表區或詳細區本身都不會關閉。
      await wrapper.get('.case-split__list').trigger('click')
      await wrapper.get('.case-split__detail-body').trigger('click')
      expect(wrapper.emitted('close')).toBeUndefined()

      await wrapper.get('.case-split__close').trigger('click')
      expect(wrapper.emitted('close')).toHaveLength(1)

      wrapper.unmount()
    }
  })

  it('removes the detail node without waiting for an animation when motion is reduced', async () => {
    // jsdom 沒有 matchMedia，detectReducedMotion() 回傳 true，
    // createPanelLeave 會同步呼叫 onComplete，Transition 立即完成。
    const wrapper = mountLayout(true)
    expect(wrapper.find('.case-split__detail').exists()).toBe(true)

    await wrapper.setProps({ detailOpen: false })
    // 一次 tick 完成 leave 與 DOM 移除，再一次 tick 讓 data-detail-open 跟上。
    await nextTick()
    await nextTick()

    expect(wrapper.find('.case-split__detail').exists()).toBe(false)
    expect(wrapper.get('.case-split').attributes('data-detail-open')).toBe('false')

    wrapper.unmount()
  })
})
