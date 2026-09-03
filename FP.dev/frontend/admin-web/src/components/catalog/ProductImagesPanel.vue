<script setup lang="ts">
/**
 * A-06 商品圖片區塊（M-03「商品圖片上傳、後台預覽、發布及中繼資料寫入」）。
 *
 * 五條端點都在這裡用完：上傳（multipart）、預覽（<img> 走授權預覽路由，同源 cookie 就夠）、
 * PATCH 中繼資料、發布、刪除。圖片是獨立的 Aggregate（自己的 RowVersion），不會動到商品的
 * RowVersion，所以這個區塊不受商品表單髒不髒的影響，也不需要像 SKU 那樣回頭重抓商品的 token。
 */
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref } from 'vue'
import {
  useDeleteProductImage,
  usePublishProductImage,
  useUpdateProductImage,
  useUploadProductImage,
} from '../../features/products/useProducts'
import type { AdminProductImageDto } from '../../features/products/types'
import { describeApiError } from '../../features/shared/errorMessages'

const props = defineProps<{
  productPublicId: string
  images: AdminProductImageDto[]
}>()

const uploadMutation = useUploadProductImage()
const updateMutation = useUpdateProductImage()
const publishMutation = usePublishProductImage()
const deleteMutation = useDeleteProductImage()

const uploadForm = reactive({ altText: '', sourceUrl: '', licenseName: '', licenseUrl: '' })
const selectedFile = ref<File | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)

function onFileChange(event: Event) {
  const input = event.target as HTMLInputElement
  selectedFile.value = input.files?.[0] ?? null
}

function submitUpload() {
  if (!selectedFile.value) {
    return
  }
  const productPublicId = props.productPublicId
  uploadMutation.mutate({
    productPublicId,
    input: {
      file: selectedFile.value,
      altText: uploadForm.altText || undefined,
      sourceUrl: uploadForm.sourceUrl || undefined,
      licenseName: uploadForm.licenseName || undefined,
      licenseUrl: uploadForm.licenseUrl || undefined,
    },
  }, {
    onSuccess: () => {
      // 上傳成功才清表單；失敗時留著讓管理員改檔案再送，不用重打四個欄位。
      uploadForm.altText = ''
      uploadForm.sourceUrl = ''
      uploadForm.licenseName = ''
      uploadForm.licenseUrl = ''
      selectedFile.value = null
      if (fileInput.value) {
        fileInput.value.value = ''
      }
    },
  })
}

// 一次只編輯一列；RowVersion 在按下「編輯」那一刻鎖定（與 SkuEditorRow 的 editRowVersion 同一個理由：
// 送出時不能拿背景重抓後的新 token，那會讓樂觀併發檢查失效）。
const editingId = ref<string | null>(null)
const editRowVersion = ref('')
const editForm = reactive({ altText: '', sortOrder: 0, sourceUrl: '', licenseName: '', licenseUrl: '' })

function startEdit(image: AdminProductImageDto) {
  editingId.value = image.publicId
  editRowVersion.value = image.rowVersion
  editForm.altText = image.altText
  // 產生的契約把 int32 標成 number | string（與 SkuEditorRow 對 listPrice 的處理相同）。
  editForm.sortOrder = Number(image.sortOrder)
  editForm.sourceUrl = image.sourceUrl ?? ''
  editForm.licenseName = image.licenseName ?? ''
  editForm.licenseUrl = image.licenseUrl ?? ''
  updateMutation.reset()
}

function cancelEdit() {
  editingId.value = null
}

function submitEdit(image: AdminProductImageDto) {
  updateMutation.mutate({
    productPublicId: props.productPublicId,
    imagePublicId: image.publicId,
    request: {
      altText: editForm.altText,
      sortOrder: Number(editForm.sortOrder),
      sourceUrl: editForm.sourceUrl || null,
      licenseName: editForm.licenseName || null,
      licenseUrl: editForm.licenseUrl || null,
      rowVersion: editRowVersion.value,
    },
  }, {
    onSuccess: () => {
      editingId.value = null
    },
  })
}

function publish(image: AdminProductImageDto) {
  publishMutation.mutate({
    productPublicId: props.productPublicId,
    imagePublicId: image.publicId,
    rowVersion: image.rowVersion,
  })
}

function remove(image: AdminProductImageDto) {
  if (!globalThis.confirm(`確定要移除這張圖片（${image.originalFileName}）嗎？已發布的公開網址會立即失效。`)) {
    return
  }
  deleteMutation.mutate({
    productPublicId: props.productPublicId,
    imagePublicId: image.publicId,
    rowVersion: image.rowVersion,
  })
}

const isAnyMutationPending = computed(() =>
  uploadMutation.isPending.value ||
  updateMutation.isPending.value ||
  publishMutation.isPending.value ||
  deleteMutation.isPending.value)

function formatStatus(status: string): string {
  return {
    Processing: '處理中',
    Ready: '待發布',
    Published: '已發布',
    Rejected: '已退回',
    PendingDelete: '待刪除',
    Deleted: '已刪除',
  }[status] ?? status
}

function publicUrlOf(image: AdminProductImageDto): string | null {
  return image.variants.find((variant) => variant.variant === '800')?.publicUrl ?? null
}
</script>

<template>
  <section
    aria-labelledby="product-images-title"
    class="product-images"
  >
    <h2 id="product-images-title">
      商品圖片
    </h2>
    <p class="product-form__hint">
      JPG／PNG／WebP，最大 10 MB。上傳後為「待發布」；填齊 Alt、來源網址與授權後才能發布，發布後前台才看得到。
    </p>

    <table
      v-if="images.length > 0"
      class="product-images__table"
    >
      <thead>
        <tr>
          <th>預覽</th>
          <th>Alt</th>
          <th>排序</th>
          <th>狀態</th>
          <th>來源／授權</th>
          <th />
        </tr>
      </thead>
      <tbody>
        <template
          v-for="image in images"
          :key="image.publicId"
        >
          <tr v-if="editingId !== image.publicId">
            <td>
              <img
                :src="`${image.previewPathBase}/320`"
                :alt="image.altText"
                :width="image.variants[0]?.width"
                :height="image.variants[0]?.height"
                class="product-images__thumbnail"
                loading="lazy"
              >
            </td>
            <td>
              {{ image.altText }}
              <span
                v-if="image.isPrimary"
                class="product-images__badge"
              >主圖</span>
            </td>
            <td>{{ image.sortOrder }}</td>
            <td>
              {{ formatStatus(image.status) }}
              <a
                v-if="publicUrlOf(image)"
                :href="publicUrlOf(image)!"
                target="_blank"
                rel="noopener"
              >公開網址</a>
            </td>
            <td>
              <span v-if="image.hasCompleteMetadata">{{ image.licenseName }}</span>
              <span
                v-else
                class="product-images__incomplete"
              >未填齊（無法發布）</span>
            </td>
            <td>
              <button
                type="button"
                :disabled="isAnyMutationPending"
                @click="startEdit(image)"
              >
                編輯
              </button>
              <button
                type="button"
                :disabled="isAnyMutationPending || image.status === 'Published' || !image.hasCompleteMetadata"
                :title="!image.hasCompleteMetadata ? '請先填齊 Alt、來源網址、授權名稱與授權網址' : undefined"
                @click="publish(image)"
              >
                發布
              </button>
              <button
                type="button"
                :disabled="isAnyMutationPending"
                @click="remove(image)"
              >
                刪除
              </button>
            </td>
          </tr>
          <tr
            v-else
            class="product-images__row--editing"
          >
            <td>
              <img
                :src="`${image.previewPathBase}/320`"
                :alt="image.altText"
                class="product-images__thumbnail"
              >
            </td>
            <td>
              <input
                v-model="editForm.altText"
                aria-label="圖片 Alt"
                maxlength="160"
              >
            </td>
            <td>
              <input
                v-model.number="editForm.sortOrder"
                type="number"
                min="0"
                max="9999"
                aria-label="圖片排序"
              >
            </td>
            <td>{{ formatStatus(image.status) }}</td>
            <td class="product-images__attribution">
              <input
                v-model="editForm.sourceUrl"
                aria-label="來源網址"
                maxlength="1000"
                placeholder="來源網址"
              >
              <input
                v-model="editForm.licenseName"
                aria-label="授權名稱"
                maxlength="100"
                placeholder="授權名稱"
              >
              <input
                v-model="editForm.licenseUrl"
                aria-label="授權網址"
                maxlength="1000"
                placeholder="授權網址"
              >
            </td>
            <td>
              <button
                type="button"
                :disabled="updateMutation.isPending.value || !editForm.altText"
                @click="submitEdit(image)"
              >
                儲存
              </button>
              <button
                type="button"
                :disabled="updateMutation.isPending.value"
                @click="cancelEdit"
              >
                取消
              </button>
            </td>
          </tr>
        </template>
      </tbody>
    </table>
    <p
      v-else
      class="product-form__hint"
    >
      尚無圖片。
    </p>

    <p
      v-if="isApiError(updateMutation.error.value)"
      class="product-form__error"
    >
      {{ describeApiError(updateMutation.error.value) }}
    </p>
    <p
      v-if="isApiError(publishMutation.error.value)"
      class="product-form__error"
    >
      {{ describeApiError(publishMutation.error.value) }}
    </p>
    <p
      v-if="isApiError(deleteMutation.error.value)"
      class="product-form__error"
    >
      {{ describeApiError(deleteMutation.error.value) }}
    </p>

    <form
      class="product-images__upload"
      aria-label="上傳商品圖片"
      @submit.prevent="submitUpload"
    >
      <label>
        圖片檔案
        <input
          ref="fileInput"
          type="file"
          accept="image/jpeg,image/png,image/webp"
          aria-label="圖片檔案"
          @change="onFileChange"
        >
      </label>
      <label>
        Alt
        <input
          v-model="uploadForm.altText"
          aria-label="上傳 Alt"
          maxlength="160"
        >
      </label>
      <label>
        來源網址
        <input
          v-model="uploadForm.sourceUrl"
          aria-label="上傳來源網址"
          maxlength="1000"
        >
      </label>
      <label>
        授權名稱
        <input
          v-model="uploadForm.licenseName"
          aria-label="上傳授權名稱"
          maxlength="100"
        >
      </label>
      <label>
        授權網址
        <input
          v-model="uploadForm.licenseUrl"
          aria-label="上傳授權網址"
          maxlength="1000"
        >
      </label>
      <button
        type="submit"
        :disabled="!selectedFile || uploadMutation.isPending.value"
      >
        {{ uploadMutation.isPending.value ? '上傳中…' : '上傳圖片' }}
      </button>
      <p
        v-if="isApiError(uploadMutation.error.value)"
        class="product-form__error"
      >
        {{ describeApiError(uploadMutation.error.value) }}
      </p>
    </form>
  </section>
</template>

<style scoped>
.product-images {
  margin-block-start: 2rem;
}

.product-images__table {
  width: 100%;
  border-collapse: collapse;
}

.product-images__table th,
.product-images__table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
  vertical-align: top;
}

.product-images__thumbnail {
  max-width: 8rem;
  height: auto;
  border-radius: 0.25rem;
  background: #f3f4f6;
}

.product-images__badge {
  margin-inline-start: 0.375rem;
  padding: 0.125rem 0.375rem;
  border-radius: 0.25rem;
  background: #e0e7ff;
  font-size: 0.75rem;
}

.product-images__incomplete {
  color: #b45309;
  font-size: 0.875rem;
}

.product-images__attribution {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.product-images__upload {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
  gap: 0.75rem;
  margin-block-start: 1rem;
  padding: 1rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
}

.product-images__upload label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.875rem;
}

.product-images__upload input {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.product-form__error {
  color: #b91c1c;
  font-size: 0.875rem;
}

.product-form__hint {
  color: #6b7280;
  font-size: 0.875rem;
}
</style>
