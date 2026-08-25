<script setup lang="ts">
/**
 * Specification values (SkuDto.specifications) are shown read-only here. Editing them
 * needs a per-category catalog of SpecificationDefinition semantic keys/value types,
 * which no admin endpoint exposes yet — out of scope for this slice. New SKUs are
 * created with an empty specifications array.
 */
import { isApiError } from '@doselect/web-shared/api'
import { reactive, ref } from 'vue'
import { useDeleteSku, useUpdateSku } from '../../features/skus/useSkus'
import type { SkuDto, UpdateSkuRequest } from '../../features/skus/types'
import { describeApiError } from '../../features/shared/errorMessages'

const props = defineProps<{
  sku: SkuDto
  productPublicId: string
}>()

const emit = defineEmits<{
  /**
   * PR #24 review round 7 (P1): fired after this row's own update/delete mutation succeeds —
   * both legitimately advance the parent Product's RowVersion (Product.Touch(), round 5). The
   * parent explicitly refetches and re-snapshots its own edit token in response, rather than
   * treating every background refetch of the product query as safe to adopt.
   */
  skuMutated: []
}>()

const updateMutation = useUpdateSku(props.productPublicId)
const deleteMutation = useDeleteSku(props.productPublicId)

const editing = ref(false)
// PR #24 review round 7 (P1): captured once at startEdit, alongside the rest of the edit
// snapshot in `state` — submit() must send the token read at the moment editing began, not
// whatever props.sku.rowVersion has become by the time the admin clicks 儲存 (a background
// refetch between those two moments would otherwise silently swap in a newer token and defeat
// the optimistic-concurrency check, same class of bug as ProductEditPage's editRowVersion).
const editRowVersion = ref(props.sku.rowVersion)

function formatSkuStatus(status: string): string {
  return {
    Draft: '草稿',
    Published: '已上架',
    Unpublished: '已下架',
  }[status] ?? status
}

function stateFromSku(sku: SkuDto) {
  return {
    nameZhTw: sku.nameZhTw,
    listPrice: Number(sku.listPrice),
    unitCost: Number(sku.unitCost),
    weightKg: sku.weightKg == null ? null : Number(sku.weightKg),
    lengthCm: sku.lengthCm == null ? null : Number(sku.lengthCm),
    widthCm: sku.widthCm == null ? null : Number(sku.widthCm),
    heightCm: sku.heightCm == null ? null : Number(sku.heightCm),
    status: sku.status,
    isDefault: sku.isDefault,
    requiresPrepayment: sku.requiresPrepayment,
  }
}

const state = reactive(stateFromSku(props.sku))

// PR #24 review: `state` used to only be built once at component setup. Because v-for keys
// this row on sku.publicId, Vue reuses the same instance across refetches — reopening edit
// (startEdit) redisplayed whatever was last typed here, including an abandoned/cancelled
// draft, and could silently resubmit stale field values (with a fresh, so still-valid,
// RowVersion) over a value that had actually changed since. Rebuilding from the latest
// props on every startEdit, and discarding the draft on cancel, keeps the form honest.
function startEdit() {
  Object.assign(state, stateFromSku(props.sku))
  editRowVersion.value = props.sku.rowVersion
  editing.value = true
}

function cancelEdit() {
  Object.assign(state, stateFromSku(props.sku))
  editing.value = false
}

function submit() {
  const request: UpdateSkuRequest = {
    nameZhTw: state.nameZhTw,
    listPrice: state.listPrice,
    unitCost: state.unitCost,
    weightKg: state.weightKg,
    lengthCm: state.lengthCm,
    widthCm: state.widthCm,
    heightCm: state.heightCm,
    status: state.status,
    isDefault: state.isDefault,
    requiresPrepayment: state.requiresPrepayment,
    specifications: props.sku.specifications.map((spec) => ({
      semanticKey: spec.semanticKey,
      valueType: spec.valueType,
      stringValue: spec.stringValue,
      decimalValue: spec.decimalValue,
      booleanValue: spec.booleanValue,
      optionCode: spec.optionCode,
    })),
    rowVersion: editRowVersion.value,
  }
  updateMutation.mutate({ skuPublicId: props.sku.publicId, request }, {
    onSuccess: () => {
      editing.value = false
      emit('skuMutated')
    },
  })
}

function remove() {
  if (!globalThis.confirm(`確定要刪除 SKU「${props.sku.skuCode}」嗎？`)) {
    return
  }
  deleteMutation.mutate({ skuPublicId: props.sku.publicId, rowVersion: props.sku.rowVersion }, {
    onSuccess: () => emit('skuMutated'),
  })
}
</script>

<template>
  <tr v-if="!editing">
    <td>{{ sku.skuCode }}</td>
    <td>{{ sku.nameZhTw }}</td>
    <td>{{ sku.listPrice }}</td>
    <td>{{ sku.unitCost }}</td>
    <td>{{ formatSkuStatus(sku.status) }}</td>
    <td>{{ sku.isDefault ? '是' : '否' }}</td>
    <td>{{ sku.inventory?.onHandQuantity ?? '—' }}</td>
    <td>
      <button
        type="button"
        @click="startEdit"
      >
        編輯
      </button>
      <button
        type="button"
        :disabled="sku.isDefault || deleteMutation.isPending.value"
        :title="sku.isDefault ? '無法刪除目前的預設 SKU，請先將其他 SKU 設為預設' : undefined"
        @click="remove"
      >
        刪除
      </button>
    </td>
  </tr>
  <tr
    v-else
    class="sku-editor-row--editing"
  >
    <td>{{ sku.skuCode }}</td>
    <td>
      <input
        v-model="state.nameZhTw"
        aria-label="名稱"
      >
    </td>
    <td>
      <input
        v-model.number="state.listPrice"
        type="number"
        min="0"
        step="0.01"
        aria-label="售價"
      >
    </td>
    <td>
      <input
        v-model.number="state.unitCost"
        type="number"
        min="0"
        step="0.01"
        aria-label="成本"
      >
    </td>
    <td>
      <select
        v-model="state.status"
        aria-label="狀態"
      >
        <option value="Draft">
          草稿
        </option>
        <option value="Published">
          已上架
        </option>
        <option value="Unpublished">
          已下架
        </option>
      </select>
    </td>
    <td>
      <input
        v-model="state.isDefault"
        type="checkbox"
        aria-label="預設 SKU"
        :disabled="sku.isDefault"
        :title="sku.isDefault ? '無法直接取消目前的預設 SKU，請改為將其他 SKU 設為預設' : undefined"
      >
    </td>
    <td>{{ sku.inventory?.onHandQuantity ?? '—' }}</td>
    <td>
      <button
        type="button"
        :disabled="updateMutation.isPending.value"
        @click="submit"
      >
        儲存
      </button>
      <button
        type="button"
        @click="cancelEdit"
      >
        取消
      </button>
    </td>
  </tr>
  <tr v-if="updateMutation.error.value || deleteMutation.error.value">
    <td
      colspan="8"
      class="sku-editor-row__error"
    >
      {{
        isApiError(updateMutation.error.value)
          ? describeApiError(updateMutation.error.value)
          : (isApiError(deleteMutation.error.value) ? describeApiError(deleteMutation.error.value) : null)
      }}
    </td>
  </tr>
</template>

<style scoped>
.sku-editor-row__error {
  color: #b91c1c;
  font-size: 0.875rem;
}
</style>
