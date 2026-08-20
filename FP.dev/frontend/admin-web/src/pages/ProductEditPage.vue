<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, watch } from 'vue'
import { useRouter } from 'vue-router'
import SkuEditorRow from '../components/catalog/SkuEditorRow.vue'
import { useAdminProductDetail, useCreateProduct, useUpdateProduct } from '../features/products/useProducts'
import { useBrandList } from '../features/brands/useBrands'
import { useCategoryList } from '../features/categories/useCategories'
import { useTagList } from '../features/tags/useTags'
import { useCreateSku } from '../features/skus/useSkus'
import { describeApiError } from '../features/shared/errorMessages'

const props = defineProps<{
  id?: string
}>()

const router = useRouter()
const isEditMode = computed(() => Boolean(props.id))

const { data: product, isPending, isError, error, refetch } = useAdminProductDetail(() => props.id)
const { data: brandResult } = useBrandList({ isActive: true, pageSize: 100 })
const { data: categoryResult } = useCategoryList({ isActive: true, pageSize: 100 })
const { data: tagResult } = useTagList({ isActive: true, pageSize: 100 })

const createMutation = useCreateProduct()
const updateMutation = useUpdateProduct()

const form = reactive({
  productCode: '',
  nameZhTw: '',
  brandPublicId: '',
  categoryPublicId: '',
  descriptionZhTw: '',
  warrantyMonths: null as number | null,
  status: 'Draft',
  tagCodes: [] as string[],
})

watch(product, (value) => {
  if (!value) {
    return
  }
  form.productCode = value.productCode
  form.nameZhTw = value.nameZhTw
  form.descriptionZhTw = value.descriptionZhTw ?? ''
  form.warrantyMonths = value.warrantyMonths == null ? null : Number(value.warrantyMonths)
  form.status = value.status
  form.tagCodes = value.tags.map((tag) => tag.code)
  const brand = brandResult.value?.items.find((candidate) => candidate.code === value.brand.code)
  if (brand) {
    form.brandPublicId = brand.publicId
  }
  const category = categoryResult.value?.items.find((candidate) => candidate.code === value.category.code)
  if (category) {
    form.categoryPublicId = category.publicId
  }
}, { immediate: true })

function tagPublicIds(): string[] {
  const tags = tagResult.value?.items ?? []
  return form.tagCodes
    .map((code) => tags.find((tag) => tag.code === code)?.publicId)
    .filter((value): value is string => Boolean(value))
}

function submitCreate() {
  createMutation.mutate({
    productCode: form.productCode,
    nameZhTw: form.nameZhTw,
    brandPublicId: form.brandPublicId,
    categoryPublicId: form.categoryPublicId,
    descriptionZhTw: form.descriptionZhTw || null,
    warrantyMonths: form.warrantyMonths,
    tagPublicIds: tagPublicIds(),
    status: form.status,
  }, {
    onSuccess: (created) => router.push(`/products/${created.publicId}/edit`),
  })
}

function submitUpdate() {
  if (!product.value) {
    return
  }
  updateMutation.mutate({
    publicId: product.value.publicId,
    request: {
      nameZhTw: form.nameZhTw,
      brandPublicId: form.brandPublicId,
      categoryPublicId: form.categoryPublicId,
      descriptionZhTw: form.descriptionZhTw || null,
      warrantyMonths: form.warrantyMonths,
      tagPublicIds: tagPublicIds(),
      status: form.status,
      rowVersion: product.value.rowVersion,
    },
  })
}

// props.id (the route param) already equals the product's publicId in edit mode, so the
// mutation composable can be called unconditionally at setup top-level as Vue requires,
// without waiting for `product` to finish loading.
const createSkuMutation = useCreateSku(props.id ?? '')
const newSku = reactive({
  skuCode: '',
  nameZhTw: '',
  listPrice: 0,
  unitCost: 0,
  status: 'Draft',
  isDefault: false,
  requiresPrepayment: false,
})

function submitNewSku() {
  createSkuMutation.mutate({
    skuCode: newSku.skuCode,
    nameZhTw: newSku.nameZhTw,
    listPrice: newSku.listPrice,
    unitCost: newSku.unitCost,
    weightKg: null,
    lengthCm: null,
    widthCm: null,
    heightCm: null,
    status: newSku.status,
    isDefault: newSku.isDefault,
    requiresPrepayment: newSku.requiresPrepayment,
    specifications: [],
  }, {
    onSuccess: () => {
      newSku.skuCode = ''
      newSku.nameZhTw = ''
      newSku.listPrice = 0
      newSku.unitCost = 0
      newSku.isDefault = false
      newSku.requiresPrepayment = false
    },
  })
}
</script>

<template>
  <section aria-labelledby="product-edit-title">
    <h1 id="product-edit-title">
      {{ isEditMode ? '編輯商品' : '新增商品' }}
    </h1>

    <LoadingState
      v-if="isEditMode && isPending"
      label="商品載入中"
    />
    <ErrorState
      v-else-if="isEditMode && isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />
    <EmptyState
      v-else-if="isEditMode && !product"
      title="找不到這個商品"
    />
    <template v-else>
      <form
        class="product-form"
        aria-label="商品資料"
        @submit.prevent="isEditMode ? submitUpdate() : submitCreate()"
      >
        <label>
          商品代碼
          <input
            v-model="form.productCode"
            :disabled="isEditMode"
            required
          >
        </label>
        <label>
          名稱
          <input
            v-model="form.nameZhTw"
            required
          >
        </label>
        <label>
          品牌
          <select
            v-model="form.brandPublicId"
            required
          >
            <option
              value=""
              disabled
            >
              請選擇品牌
            </option>
            <option
              v-for="brand in brandResult?.items ?? []"
              :key="brand.publicId"
              :value="brand.publicId"
            >
              {{ brand.nameZhTw }}
            </option>
          </select>
        </label>
        <label>
          分類
          <select
            v-model="form.categoryPublicId"
            required
          >
            <option
              value=""
              disabled
            >
              請選擇分類
            </option>
            <option
              v-for="category in categoryResult?.items ?? []"
              :key="category.publicId"
              :value="category.publicId"
            >
              {{ category.nameZhTw }}
            </option>
          </select>
        </label>
        <label>
          說明
          <textarea v-model="form.descriptionZhTw" />
        </label>
        <label>
          保固月數
          <input
            v-model.number="form.warrantyMonths"
            type="number"
          >
        </label>
        <label>
          狀態
          <select v-model="form.status">
            <option value="Draft">
              草稿
            </option>
            <option value="Published">
              已上架
            </option>
          </select>
        </label>
        <fieldset class="product-form__tags">
          <legend>標籤</legend>
          <label
            v-for="tag in tagResult?.items ?? []"
            :key="tag.publicId"
          >
            <input
              v-model="form.tagCodes"
              type="checkbox"
              :value="tag.code"
            >
            {{ tag.nameZhTw }}
          </label>
        </fieldset>
        <button
          type="submit"
          :disabled="createMutation.isPending.value || updateMutation.isPending.value"
        >
          儲存
        </button>
        <p
          v-if="isApiError(createMutation.error.value)"
          class="product-form__error"
        >
          {{ describeApiError(createMutation.error.value) }}
        </p>
        <p
          v-if="isApiError(updateMutation.error.value)"
          class="product-form__error"
        >
          {{ describeApiError(updateMutation.error.value) }}
        </p>
      </form>

      <section
        v-if="isEditMode && product"
        aria-labelledby="product-skus-title"
        class="product-skus"
      >
        <h2 id="product-skus-title">
          SKU 管理
        </h2>
        <table class="product-skus__table">
          <thead>
            <tr>
              <th>代碼</th>
              <th>名稱</th>
              <th>售價</th>
              <th>成本</th>
              <th>狀態</th>
              <th>預設</th>
              <th>庫存</th>
              <th />
            </tr>
          </thead>
          <tbody>
            <SkuEditorRow
              v-for="sku in product.skus"
              :key="sku.publicId"
              :sku="sku"
              :product-public-id="product.publicId"
            />
            <tr>
              <td>
                <input
                  v-model="newSku.skuCode"
                  aria-label="新 SKU 代碼"
                >
              </td>
              <td>
                <input
                  v-model="newSku.nameZhTw"
                  aria-label="新 SKU 名稱"
                >
              </td>
              <td>
                <input
                  v-model.number="newSku.listPrice"
                  type="number"
                  step="0.01"
                  aria-label="新 SKU 售價"
                >
              </td>
              <td>
                <input
                  v-model.number="newSku.unitCost"
                  type="number"
                  step="0.01"
                  aria-label="新 SKU 成本"
                >
              </td>
              <td>
                <select
                  v-model="newSku.status"
                  aria-label="新 SKU 狀態"
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
                  v-model="newSku.isDefault"
                  type="checkbox"
                  aria-label="設為預設"
                >
              </td>
              <td>—</td>
              <td>
                <button
                  type="button"
                  :disabled="createSkuMutation.isPending.value"
                  @click="submitNewSku"
                >
                  新增 SKU
                </button>
              </td>
            </tr>
          </tbody>
        </table>
        <p
          v-if="isApiError(createSkuMutation.error.value)"
          class="product-form__error"
        >
          {{ describeApiError(createSkuMutation.error.value) }}
        </p>
      </section>
    </template>
  </section>
</template>

<style scoped>
.product-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 32rem;
  margin-block-end: 2rem;
}

.product-form label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.875rem;
}

.product-form input,
.product-form select,
.product-form textarea {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.product-form__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
  padding: 0.75rem;
}

.product-form__tags label {
  flex-direction: row;
  align-items: center;
  gap: 0.375rem;
}

.product-form__error {
  color: #b91c1c;
  font-size: 0.875rem;
}

.product-skus__table {
  width: 100%;
  border-collapse: collapse;
}

.product-skus__table th,
.product-skus__table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}
</style>
