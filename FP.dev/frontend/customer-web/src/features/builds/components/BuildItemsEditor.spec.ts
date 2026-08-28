import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { describe, expect, it } from 'vitest'
import BuildItemsEditor, { type EditableBuildItem } from './BuildItemsEditor.vue'

/**
 * Stands in for the real picker (which does live product search) — emits a deterministic fake
 * pick for whichever category slot it's rendered under, keyed so a test can target one slot's
 * button without ambiguity.
 */
const BuildCategorySlotPickerStub = defineComponent({
  props: { categoryCode: { type: String, required: true }, disabled: { type: Boolean, default: false } },
  emits: ['select'],
  template: `
    <button
      type="button"
      :data-testid="'pick-' + categoryCode"
      :disabled="disabled"
      @click="$emit('select', { skuPublicId: categoryCode + '-sku-new', skuCode: categoryCode + '-NEW', name: categoryCode + ' 新選商品' })"
    >
      pick {{ categoryCode }}
    </button>
  `,
})

function mountEditor(items: EditableBuildItem[] = []) {
  let emitted: EditableBuildItem[] = items
  const wrapper = mount(BuildItemsEditor, {
    props: {
      items,
      'onUpdate:items': (next: EditableBuildItem[]) => { emitted = next },
    },
    global: { stubs: { BuildCategorySlotPicker: BuildCategorySlotPickerStub } },
  })
  return { wrapper, getEmitted: () => emitted }
}

describe('BuildItemsEditor', () => {
  it('renders all 8 build-component category slots', () => {
    const { wrapper } = mountEditor()
    for (const code of ['CPU', 'MOTHERBOARD', 'MEMORY', 'GPU', 'STORAGE', 'PSU', 'CASE', 'CPU_COOLER']) {
      expect(wrapper.find(`[data-testid="pick-${code}"]`).exists()).toBe(true)
    }
  })

  it('shows a missing-slot marker for an empty singleton category', () => {
    const { wrapper } = mountEditor()
    expect(wrapper.text()).toContain('CPU')
    expect(wrapper.text()).toContain('（尚未選擇）')
  })

  /**
   * 組長 PR #35 review, item 1: CPU／主機板／顯卡／PSU／機殼／散熱器 hold at most one SKU —
   * picking a new one must replace whatever was already in that slot, not add a second CPU.
   */
  it('replaces the existing item when a new SKU is picked for a singleton category', async () => {
    const existingCpu: EditableBuildItem = { skuPublicId: 'old-cpu', quantity: 1, name: '舊 CPU', categoryCode: 'CPU' }
    const { wrapper, getEmitted } = mountEditor([existingCpu])

    await wrapper.find('[data-testid="pick-CPU"]').trigger('click')

    const result = getEmitted()
    const cpuItems = result.filter((item) => item.categoryCode === 'CPU')
    expect(cpuItems).toHaveLength(1)
    expect(cpuItems[0].skuPublicId).toBe('CPU-sku-new')
  })

  /**
   * 記憶體／儲存裝置 accept multiple rows — picking a new SKU must add alongside the existing
   * one(s), not replace them.
   */
  it('appends rather than replaces when a new SKU is picked for a multi-item category', async () => {
    const existingMemory: EditableBuildItem = { skuPublicId: 'old-mem', quantity: 1, name: '舊記憶體', categoryCode: 'MEMORY' }
    const { wrapper, getEmitted } = mountEditor([existingMemory])

    await wrapper.find('[data-testid="pick-MEMORY"]').trigger('click')

    const result = getEmitted()
    const memoryItems = result.filter((item) => item.categoryCode === 'MEMORY')
    expect(memoryItems).toHaveLength(2)
    expect(memoryItems.map((item) => item.skuPublicId)).toEqual(['old-mem', 'MEMORY-sku-new'])
  })

  it('picking a SKU for one category does not disturb items in a different category', async () => {
    const existingCpu: EditableBuildItem = { skuPublicId: 'old-cpu', quantity: 1, name: '舊 CPU', categoryCode: 'CPU' }
    const { wrapper, getEmitted } = mountEditor([existingCpu])

    await wrapper.find('[data-testid="pick-GPU"]').trigger('click')

    const result = getEmitted()
    expect(result.find((item) => item.categoryCode === 'CPU')?.skuPublicId).toBe('old-cpu')
    expect(result.find((item) => item.categoryCode === 'GPU')?.skuPublicId).toBe('GPU-sku-new')
  })

  it('removes an item and updates its quantity via the row controls', async () => {
    const item: EditableBuildItem = { skuPublicId: 'sku-1', quantity: 1, name: '測試記憶體', categoryCode: 'MEMORY' }
    const { wrapper, getEmitted } = mountEditor([item])

    const quantityInput = wrapper.find('input[type="number"]')
    await quantityInput.setValue(3)
    await quantityInput.trigger('change')
    expect(getEmitted()[0].quantity).toBe(3)

    const removeButton = wrapper.findAll('button').find((button) => button.text() === '移除')
    await removeButton!.trigger('click')
    expect(getEmitted()).toHaveLength(0)
  })
})
