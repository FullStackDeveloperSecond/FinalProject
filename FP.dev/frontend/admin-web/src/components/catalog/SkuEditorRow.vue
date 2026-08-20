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

const updateMutation = useUpdateSku(props.productPublicId)
const deleteMutation = useDeleteSku(props.productPublicId)

const editing = ref(false)
const state = reactive({
  nameZhTw: props.sku.nameZhTw,
  listPrice: Number(props.sku.listPrice),
  unitCost: Number(props.sku.unitCost),
  weightKg: props.sku.weightKg == null ? null : Number(props.sku.weightKg),
  lengthCm: props.sku.lengthCm == null ? null : Number(props.sku.lengthCm),
  widthCm: props.sku.widthCm == null ? null : Number(props.sku.widthCm),
  heightCm: props.sku.heightCm == null ? null : Number(props.sku.heightCm),
  status: props.sku.status,
  isDefault: props.sku.isDefault,
  requiresPrepayment: props.sku.requiresPrepayment,
})

function startEdit() {
  editing.value = true
}

function cancelEdit() {
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
    rowVersion: props.sku.rowVersion,
  }
  updateMutation.mutate({ skuPublicId: props.sku.publicId, request }, {
    onSuccess: () => { editing.value = false },
  })
}

function remove() {
  if (!globalThis.confirm(`確定要刪除 SKU「${props.sku.skuCode}」嗎？`)) {
    return
  }
  deleteMutation.mutate({ skuPublicId: props.sku.publicId, rowVersion: props.sku.rowVersion })
}
</script>

<template>
  <tr v-if="!editing">
    <td>{{ sku.skuCode }}</td>
    <td>{{ sku.nameZhTw }}</td>
    <td>{{ sku.listPrice }}</td>
    <td>{{ sku.unitCost }}</td>
    <td>{{ sku.status }}</td>
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
        :disabled="deleteMutation.isPending.value"
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
        step="0.01"
        aria-label="售價"
      >
    </td>
    <td>
      <input
        v-model.number="state.unitCost"
        type="number"
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
      </select>
    </td>
    <td>
      <input
        v-model="state.isDefault"
        type="checkbox"
        aria-label="預設 SKU"
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
