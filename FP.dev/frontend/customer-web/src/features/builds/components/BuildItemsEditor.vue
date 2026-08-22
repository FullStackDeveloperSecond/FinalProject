<script setup lang="ts">
/**
 * Component-slot picker stand-in: there is no Catalog API on this backend branch yet
 * (catalog-api hasn't merged to `dev`, so `feature/build-compat-api` doesn't carry its
 * controllers either) — a real picker would let the shopper browse/search SKUs per build
 * slot (CPU／主機板／顯卡…). Until that lands, each row is a manually-entered SKU PublicId
 * + quantity, which is enough to exercise the real create/update/compatibility-check API
 * end to end. Flagged for 組長 like the other cross-slice gaps this session found; swap
 * this out for a real `features/catalog` search picker once catalog-frontend merges.
 */
import { reactive } from 'vue'

export interface EditableBuildItem {
  skuPublicId: string
  quantity: number
  name: string
}

const props = defineProps<{
  items: EditableBuildItem[]
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:items': [items: EditableBuildItem[]]
}>()

const draftRow = reactive<EditableBuildItem>({ skuPublicId: '', quantity: 1, name: '' })

function addRow(): void {
  if (!draftRow.skuPublicId.trim() || draftRow.quantity < 1) {
    return
  }

  emit('update:items', [...props.items, { ...draftRow }])
  draftRow.skuPublicId = ''
  draftRow.quantity = 1
  draftRow.name = ''
}

function removeRow(index: number): void {
  emit('update:items', props.items.filter((_, i) => i !== index))
}

function updateQuantity(index: number, quantity: number): void {
  emit('update:items', props.items.map((item, i) => (i === index ? { ...item, quantity } : item)))
}
</script>

<template>
  <div class="build-items-editor">
    <table
      v-if="items.length > 0"
      class="build-items-editor__table"
    >
      <thead>
        <tr>
          <th>SKU PublicId</th>
          <th>名稱</th>
          <th>數量</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="(item, index) in items"
          :key="`${item.skuPublicId}-${index}`"
        >
          <td class="build-items-editor__sku">
            {{ item.skuPublicId }}
          </td>
          <td>{{ item.name || '—' }}</td>
          <td>
            <input
              type="number"
              min="1"
              max="99"
              :value="item.quantity"
              :disabled="disabled"
              aria-label="數量"
              @change="updateQuantity(index, Number(($event.target as HTMLInputElement).value))"
            >
          </td>
          <td>
            <button
              type="button"
              :disabled="disabled"
              @click="removeRow(index)"
            >
              移除
            </button>
          </td>
        </tr>
      </tbody>
    </table>
    <p
      v-else
      class="build-items-editor__empty"
    >
      尚未加入任何零件。
    </p>

    <div class="build-items-editor__add-row">
      <input
        v-model="draftRow.skuPublicId"
        type="text"
        placeholder="SKU PublicId（GUID）"
        aria-label="SKU PublicId"
        :disabled="disabled"
      >
      <input
        v-model="draftRow.name"
        type="text"
        placeholder="顯示名稱（選填）"
        aria-label="顯示名稱"
        :disabled="disabled"
      >
      <input
        v-model.number="draftRow.quantity"
        type="number"
        min="1"
        max="99"
        aria-label="數量"
        :disabled="disabled"
      >
      <button
        type="button"
        :disabled="disabled"
        @click="addRow"
      >
        加入零件
      </button>
    </div>
  </div>
</template>

<style scoped>
.build-items-editor {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.build-items-editor__table {
  width: 100%;
  border-collapse: collapse;
}

.build-items-editor__table th,
.build-items-editor__table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}

.build-items-editor__sku {
  font-family: ui-monospace, SFMono-Regular, monospace;
  font-size: 0.8125rem;
  overflow-wrap: anywhere;
}

.build-items-editor__empty {
  color: #4b5563;
}

.build-items-editor__add-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.build-items-editor__add-row input {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.build-items-editor__add-row input[type='text'] {
  flex: 1 1 12rem;
}

.build-items-editor__add-row input[type='number'] {
  width: 6rem;
}
</style>
