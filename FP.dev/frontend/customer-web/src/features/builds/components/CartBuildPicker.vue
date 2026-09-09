<script setup lang="ts">
import { computed, ref } from 'vue'
import { useCart } from '../../cart/useCart'
import { BUILD_CATEGORY_SLOTS } from '../types'
import type { BuildImport } from '../buildImport'

const emit = defineEmits<{ select: [value: BuildImport] }>()
const cart = useCart()
const quantities = ref<Record<string, number>>({})
const available = computed(() => (cart.data.value?.items ?? []).filter(item => !item.assemblyGroupKey
  && BUILD_CATEGORY_SLOTS.some(slot => slot.code === item.categoryCode)))
function pick(): void {
  const source = cart.data.value
  if (!source) return
  const selected = available.value.filter(item => Number(quantities.value[item.publicId]) > 0)
  if (!selected.length) return
  emit('select', {
    name: '我的組裝清單',
    items: selected.map(item => ({ skuPublicId: item.skuPublicId, name: item.name,
      categoryCode: item.categoryCode!, quantity: Number(quantities.value[item.publicId]) })),
    cartSource: { publicId: source.publicId, rowVersion: source.rowVersion,
      items: selected.map(item => ({ cartItemPublicId: item.publicId, skuPublicId: item.skuPublicId,
        quantity: Number(quantities.value[item.publicId]) })) },
  })
}
</script>

<template>
  <section aria-label="從購物車挑選零件">
    <h2>從購物車挑選零件</h2>
    <p>只列出散裝電腦零件，已組裝群組不拆開。現在只匯入草稿；確認加入購物車時才移轉所選數量，剩餘散裝數量會保留。</p>
    <p
      v-if="cart.isPending.value"
      role="status"
    >
      正在讀取購物車…
    </p>
    <p
      v-else-if="cart.isError.value"
      role="alert"
    >
      購物車載入失敗，請重新整理後再選取。
    </p>
    <p v-else-if="!available.length">
      目前沒有可匯入的散裝零件。
    </p>
    <label
      v-for="item in available"
      :key="item.publicId"
      class="cart-build-row"
    >
      {{ item.name }}（購物車 {{ item.quantity }} 件）
      <select
        v-model.number="quantities[item.publicId]"
        :aria-label="`${item.name} 匯入數量`"
      >
        <option :value="undefined">不匯入</option>
        <option
          v-for="count in Math.min(Number(item.quantity), 8)"
          :key="count"
          :value="count"
        >{{ count }} 件</option>
      </select>
    </label>
    <button
      type="button"
      :disabled="!Object.values(quantities).some(value => value > 0)"
      @click="pick"
    >
      預覽匯入
    </button>
  </section>
</template>

<style scoped>
.cart-build-row { display: flex; justify-content: space-between; gap: 1rem; margin-block: .75rem; }
select { min-height: 2.75rem; }
</style>
