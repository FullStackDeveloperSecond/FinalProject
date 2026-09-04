<script setup lang="ts">
/** A-13 `/admin/inventory/imports`（UC-ADM-INV-01 匯入）：庫存模板預覽、逐列錯誤、原子確認。 */
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref } from 'vue'
import ImportBatchPanel from '../features/imports/components/ImportBatchPanel.vue'
import { describeApiError } from '../features/shared/errorMessages'
import { usePreviewImport } from '../features/imports/useImports'

const adjustmentsFile = ref<File | null>(null)
const batchId = ref<string | null>(null)
const uploadError = ref<string | null>(null)

const preview = usePreviewImport('inventory')

const canUpload = computed(() => Boolean(adjustmentsFile.value) && !preview.isPending.value)

function pick(target: EventTarget | null): File | null {
  const input = target as HTMLInputElement | null
  return input?.files?.[0] ?? null
}

async function upload() {
  if (!canUpload.value) return
  uploadError.value = null
  try {
    const batch = await preview.mutateAsync({ adjustments: adjustmentsFile.value! })
    batchId.value = batch.publicId
  }
  catch (caught) {
    uploadError.value = uploadErrorMessage(caught)
  }
}

function uploadErrorMessage(caught: unknown): string {
  const code = isApiError(caught) ? caught.code : undefined
  switch (code) {
    case 'import_batch_in_progress':
      return '你已經有一個未結束的庫存匯入批次，請先完成或等它逾期後再上傳。'
    case 'import_format_unsupported':
      return '檔案格式或模板版本不符，整批未暫存。'
    case 'import_dataset_missing':
      return '請選擇一個非空的 CSV 檔。'
    default:
      return isApiError(caught) ? describeApiError(caught) : '上傳失敗，整批未暫存。'
  }
}
</script>

<template>
  <section aria-labelledby="inventory-import-title">
    <h1 id="inventory-import-title">
      庫存匯入
    </h1>
    <p class="inventory-import__lead">
      上傳盤點結果進行預覽；系統會依目前庫存算出每一列的調整量。確認時整批一次套用，任一列失敗全部回滾。
    </p>

    <!--
      庫存不與商品模板混用（匯入暫存與庫存調整設計.md）。這裡把欄位契約直接寫在畫面上——
      規格只有四欄，與其要管理員去翻文件，不如讓他當場對照。
    -->
    <section
      class="inventory-import__contract"
      aria-labelledby="inventory-import-contract-title"
    >
      <h2 id="inventory-import-contract-title">
        欄位格式
      </h2>
      <table>
        <thead>
          <tr>
            <th>欄位</th>
            <th>必填</th>
            <th>說明</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>sku_code</code></td>
            <td>是</td>
            <td>必須是既有 SKU</td>
          </tr>
          <tr>
            <td><code>target_on_hand</code></td>
            <td>是</td>
            <td>盤點後的目標現有量，0 以上的整數；不可低於已保留數量</td>
          </tr>
          <tr>
            <td><code>reason_code</code></td>
            <td>是</td>
            <td>StocktakeDifference／Damaged／Lost／ReturnRestock／DataCorrection／Other</td>
          </tr>
          <tr>
            <td><code>note</code></td>
            <td>條件</td>
            <td>原因碼為 Other 時必填，其餘可留空</td>
          </tr>
        </tbody>
      </table>
    </section>

    <form
      class="inventory-import__form"
      aria-label="庫存匯入上傳"
      @submit.prevent="upload"
    >
      <label>
        庫存調整 CSV
        <input
          type="file"
          accept=".csv,text/csv"
          aria-label="庫存調整 CSV"
          @change="adjustmentsFile = pick($event.target)"
        >
      </label>
      <button
        type="submit"
        :disabled="!canUpload"
      >
        上傳並預覽
      </button>
    </form>

    <p
      v-if="uploadError"
      class="inventory-import__error"
      role="alert"
    >
      {{ uploadError }}
    </p>

    <ImportBatchPanel
      kind="inventory"
      :batch-id="batchId"
    />
  </section>
</template>

<style scoped>
.inventory-import__lead {
  color: #6b7280;
  max-width: 60ch;
}

.inventory-import__contract {
  margin-block: 1.5rem;
}

.inventory-import__contract h2 {
  font-size: 1rem;
}

.inventory-import__contract table {
  border-collapse: collapse;
}

.inventory-import__contract th,
.inventory-import__contract td {
  padding: 0.375rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
  font-size: 0.875rem;
}

.inventory-import__form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 1rem;
}

.inventory-import__form label {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  font-size: 0.875rem;
}

.inventory-import__error {
  color: #b91c1c;
}
</style>
