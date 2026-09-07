<script setup lang="ts">
/**
 * 組長 PR #35 review, item 1: was a manually-typed "SKU PublicId (GUID)" text box, which didn't
 * match the PR's own "pick a SKU per component slot" description and wasn't usable by an
 * ordinary shopper. Now renders the 8 build-component category slots
 * (`BUILD_CATEGORY_SLOTS`) — CPU／主機板／顯卡／PSU／機殼／散熱器 each hold at most one SKU
 * (selecting a new one replaces it); 記憶體／儲存裝置 accept multiple rows.
 */
import { computed } from 'vue'
import BuildCategorySlotPicker from './BuildCategorySlotPicker.vue'
import { BUILD_CATEGORY_SLOTS } from '../types'

export interface EditableBuildItem {
  skuPublicId: string
  quantity: number
  name: string
  categoryCode: string
}

const props = defineProps<{
  items: EditableBuildItem[]
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:items': [items: EditableBuildItem[]]
}>()

const itemsByCategory = computed(() => {
  const map = new Map<string, EditableBuildItem[]>()
  for (const item of props.items) {
    const existing = map.get(item.categoryCode)
    if (existing) {
      existing.push(item)
    } else {
      map.set(item.categoryCode, [item])
    }
  }
  return map
})

// 組長 PR #35 round-3 review, P1-2: EfCompatibilityCheckService.MergeAndValidateItems merges by
// SkuPublicId and rejects a merged row's quantity outside 1–8 — this editor must enforce the same
// bound itself, not just rely on the backend to reject it after the fact.
const MAX_ITEM_QUANTITY = 8

function selectForSlot(
  slot: { code: string, singleton: boolean },
  picked: { skuPublicId: string, skuCode: string, name: string },
): void {
  if (slot.singleton) {
    const remaining = props.items.filter((item) => item.categoryCode !== slot.code)
    emit('update:items', [...remaining, { skuPublicId: picked.skuPublicId, quantity: 1, name: picked.name, categoryCode: slot.code }])
    return
  }

  // 組長 PR #35 round-3 review, P1-2: picking the same SKU twice for a multi-quantity slot
  // (記憶體／儲存裝置) used to append a brand-new row every time — two rows for one SKU, which the
  // backend's own MergeAndValidateItems silently collapses into a single merged row on save. That
  // left this editor's local state permanently out of sync with what the server actually stored:
  // the page would keep showing two rows (and keep reporting unsaved changes) even immediately
  // after a successful save. Increment the existing row's quantity instead of ever creating a
  // duplicate, capped at the same bound the backend enforces post-merge.
  const existingIndex = props.items.findIndex(
    (item) => item.categoryCode === slot.code && item.skuPublicId === picked.skuPublicId,
  )
  if (existingIndex === -1) {
    emit('update:items', [...props.items, { skuPublicId: picked.skuPublicId, quantity: 1, name: picked.name, categoryCode: slot.code }])
    return
  }

  const next = [...props.items]
  const existing = next[existingIndex]!
  next[existingIndex] = { ...existing, quantity: Math.min(existing.quantity + 1, MAX_ITEM_QUANTITY) }
  emit('update:items', next)
}

function removeItem(skuPublicId: string, categoryCode: string): void {
  emit('update:items', props.items.filter(
    (item) => !(item.skuPublicId === skuPublicId && item.categoryCode === categoryCode),
  ))
}

// 組長 PR #35 round-3 review, P1-2: the quantity `<input min max>` was a UI hint only (max="99",
// not even matching the backend's real 1–8 bound) — nothing stopped a shopper from typing 9–99 and
// having it sent straight through to a request the backend was always going to reject. Clamps to
// the real bound instead of just displaying it.
function updateQuantity(skuPublicId: string, categoryCode: string, quantity: number): void {
  if (!Number.isFinite(quantity)) {
    return
  }
  const clamped = Math.min(Math.max(Math.trunc(quantity), 1), MAX_ITEM_QUANTITY)
  emit('update:items', props.items.map((item) =>
    (item.skuPublicId === skuPublicId && item.categoryCode === categoryCode ? { ...item, quantity: clamped } : item)))
}
</script>

<template>
  <div class="build-items-editor">
    <section
      v-for="slot in BUILD_CATEGORY_SLOTS"
      :key="slot.code"
      class="build-items-editor__slot"
    >
      <h3 class="build-items-editor__slot-label">
        {{ slot.label }}
        <span
          v-if="!(itemsByCategory.get(slot.code)?.length)"
          class="build-items-editor__slot-missing"
        >
          （尚未選擇）
        </span>
      </h3>

      <ul
        v-if="itemsByCategory.get(slot.code)?.length"
        class="build-items-editor__slot-items"
      >
        <li
          v-for="item in itemsByCategory.get(slot.code)"
          :key="item.skuPublicId"
        >
          <span class="build-items-editor__item-name">{{ item.name }}</span>
          <input
            type="number"
            min="1"
            max="8"
            :value="item.quantity"
            :disabled="disabled"
            :aria-label="`${item.name} 數量`"
            @change="updateQuantity(item.skuPublicId, slot.code, Number(($event.target as HTMLInputElement).value))"
          >
          <button
            type="button"
            :disabled="disabled"
            @click="removeItem(item.skuPublicId, slot.code)"
          >
            移除
          </button>
        </li>
      </ul>

      <p
        v-if="slot.singleton && itemsByCategory.get(slot.code)?.length"
        class="build-items-editor__slot-replace-hint"
      >
        選擇新商品將取代上面這一項。
      </p>
      <BuildCategorySlotPicker
        :category-code="slot.code"
        :disabled="disabled"
        @select="(picked) => selectForSlot(slot, picked)"
      />
    </section>
  </div>
</template>

<style scoped>
.build-items-editor {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.build-items-editor__slot {
  padding: 0.75rem;
  border: 1px solid var(--color-border-soft);
  border-radius: 0.5rem;
}

.build-items-editor__slot-label {
  margin: 0 0 0.5rem;
  font-size: 0.9375rem;
  font-weight: 700;
}

.build-items-editor__slot-missing {
  font-weight: 400;
  color: var(--color-danger);
}

.build-items-editor__slot-replace-hint {
  margin: 0 0 0.375rem;
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}

.build-items-editor__slot-items {
  list-style: none;
  margin: 0 0 0.5rem;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.build-items-editor__slot-items li {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.build-items-editor__item-name {
  flex: 1;
}

.build-items-editor__slot-items input {
  width: 4.5rem;
  padding: 0.375rem 0.5rem;
  border: 1px solid var(--color-border);
  border-radius: 0.375rem;
  font: inherit;
}
</style>
