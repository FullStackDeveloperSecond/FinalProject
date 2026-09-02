<script setup lang="ts">
/**
 * A-07 `/admin/products/import`（UC-IMPORT-01）：模板下載、XLSX 或三 CSV 預覽、逐列錯誤、原子確認。
 *
 * 組長 PR #89 item 6（裁定 A1）：規格是「上傳 XLSX，或三份 CSV」，兩條路都要能走。XLSX 是單一檔
 * 三張固定名稱的工作表（Products／Skus／Specifications），後端把工作表讀成與 CSV 相同的列再走同一組
 * 驗證，所以兩種格式的預覽結果對等。
 */
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref } from 'vue'
import ImportBatchPanel from '../features/imports/components/ImportBatchPanel.vue'
import { describeApiError } from '../features/shared/errorMessages'
import { useDownloadProductTemplate, usePreviewImport } from '../features/imports/useImports'

type UploadMode = 'csv' | 'workbook'

const mode = ref<UploadMode>('csv')
const productsFile = ref<File | null>(null)
const skusFile = ref<File | null>(null)
const specificationsFile = ref<File | null>(null)
const workbookFile = ref<File | null>(null)
const batchId = ref<string | null>(null)
const uploadError = ref<string | null>(null)

const template = useDownloadProductTemplate()
const preview = usePreviewImport('product')

const hasRequiredFiles = computed(() => (mode.value === 'workbook'
  ? Boolean(workbookFile.value)
  : Boolean(productsFile.value && skusFile.value && specificationsFile.value)))
const canUpload = computed(() => hasRequiredFiles.value && !preview.isPending.value)

function pick(target: EventTarget | null): File | null {
  const input = target as HTMLInputElement | null
  return input?.files?.[0] ?? null
}

async function downloadTemplate() {
  uploadError.value = null
  try {
    await template.mutateAsync()
  }
  catch (caught) {
    uploadError.value = isApiError(caught) ? describeApiError(caught) : '模板下載失敗，請稍後再試。'
  }
}

async function upload() {
  if (!canUpload.value) return
  uploadError.value = null
  try {
    const batch = await preview.mutateAsync(mode.value === 'workbook'
      ? { workbook: workbookFile.value! }
      : {
          products: productsFile.value!,
          skus: skusFile.value!,
          specifications: specificationsFile.value!,
        })
    batchId.value = batch.publicId
  }
  catch (caught) {
    uploadError.value = uploadErrorMessage(caught)
  }
}

/**
 * 上傳失敗一律是「整批沒有暫存」——說清楚才不會讓管理員以為有一半進去了。
 */
function uploadErrorMessage(caught: unknown): string {
  const code = isApiError(caught) ? caught.code : undefined
  switch (code) {
    case 'import_batch_in_progress':
      return '你已經有一個未結束的商品匯入批次，請先完成或等它逾期後再上傳。'
    case 'import_format_unsupported':
      return '檔案格式或模板版本不符，整批未暫存。請用最新模板重新匯出。'
    case 'import_dataset_missing':
      return mode.value === 'workbook'
        ? '工作簿必須包含 Products、Skus、Specifications 三張工作表，而且不能是空的。'
        : '三個資料集都必須提供，而且不能是空檔。'
    default:
      return isApiError(caught) ? describeApiError(caught) : '上傳失敗，整批未暫存。'
  }
}
</script>

<template>
  <section aria-labelledby="product-import-title">
    <h1 id="product-import-title">
      商品匯入
    </h1>
    <p class="product-import__lead">
      上傳一個 XLSX（三張工作表）或三個資料集的 CSV 進行預覽；確認之前不會寫入任何資料，確認時整批一次套用，任一列失敗全部回滾。
    </p>

    <div class="product-import__template">
      <button
        type="button"
        :disabled="template.isPending.value"
        @click="downloadTemplate"
      >
        下載匯入模板
      </button>
      <span>模板是一個 ZIP，內含 products、skus、specifications 三個 CSV，以及同樣三張工作表的 XLSX；都只有標題列。</span>
    </div>

    <form
      class="product-import__form"
      aria-label="商品匯入上傳"
      @submit.prevent="upload"
    >
      <fieldset class="product-import__mode">
        <legend>上傳格式</legend>
        <label>
          <input
            v-model="mode"
            type="radio"
            name="upload-mode"
            value="csv"
          >
          三個 CSV
        </label>
        <label>
          <input
            v-model="mode"
            type="radio"
            name="upload-mode"
            value="workbook"
          >
          單一 XLSX（Products／Skus／Specifications 三張工作表）
        </label>
      </fieldset>
      <template v-if="mode === 'workbook'">
        <label>
          商品匯入 XLSX
          <input
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            aria-label="商品匯入 XLSX"
            @change="workbookFile = pick($event.target)"
          >
        </label>
      </template>
      <template v-else>
        <label>
          Products CSV
          <input
            type="file"
            accept=".csv,text/csv"
            aria-label="Products CSV"
            @change="productsFile = pick($event.target)"
          >
        </label>
        <label>
          SKUs CSV
          <input
            type="file"
            accept=".csv,text/csv"
            aria-label="SKUs CSV"
            @change="skusFile = pick($event.target)"
          >
        </label>
        <label>
          Specifications CSV
          <input
            type="file"
            accept=".csv,text/csv"
            aria-label="Specifications CSV"
            @change="specificationsFile = pick($event.target)"
          >
        </label>
      </template>
      <button
        type="submit"
        :disabled="!canUpload"
      >
        上傳並預覽
      </button>
    </form>

    <p
      v-if="uploadError"
      class="product-import__error"
      role="alert"
    >
      {{ uploadError }}
    </p>

    <ImportBatchPanel
      kind="product"
      :batch-id="batchId"
    />
  </section>
</template>

<style scoped>
.product-import__lead {
  color: #6b7280;
  max-width: 60ch;
}

.product-import__template {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  margin-block: 1.5rem;
  color: #6b7280;
  font-size: 0.875rem;
}

.product-import__form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 1rem;
}

.product-import__form label {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  font-size: 0.875rem;
}

.product-import__mode {
  flex-basis: 100%;
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  border: 0;
  padding: 0;
  margin: 0;
}

.product-import__mode label {
  flex-direction: row;
  align-items: center;
}

.product-import__error {
  color: #b91c1c;
}
</style>
