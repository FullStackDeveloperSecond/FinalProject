<script setup lang="ts">
import { ErrorState, HttpStatusPage, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import SkuEditorRow from '../components/catalog/SkuEditorRow.vue'
import { useAdminProductDetail, useCreateProduct, useUpdateProduct } from '../features/products/useProducts'
import { useFullBrandList } from '../features/brands/useBrands'
import { useFullCategoryList } from '../features/categories/useCategories'
import { useFullTagList } from '../features/tags/useTags'
import { useCreateSku } from '../features/skus/useSkus'
import { describeApiError } from '../features/shared/errorMessages'

const props = defineProps<{
  productId?: string
}>()

// 組長 PR #24 round 4 review, item 6: API DTO與Schema契約.md documents tagPublicIds as 0..20 —
// now enforced server-side too; this just stops the admin from selecting more than the backend
// will ever accept instead of letting them find out from a 400 after submitting.
const MAX_TAG_COUNT = 20

const router = useRouter()
const isEditMode = computed(() => Boolean(props.productId))

const { data: product, isPending, isError, error, refetch } = useAdminProductDetail(() => props.productId)
// 組長 PR #24 round 4 review, item 5: isError fired for a 404 before this branch ever got a
// chance to render — GetById genuinely returns 404 for an unknown product (see
// AdminProductsController.GetById), so "找不到這個商品" was unreachable dead code. Matches
// customer-web's ProductDetailPage.vue isNotFound pattern.
const isNotFound = computed(() => isApiError(error.value) && error.value.status === 404)
// PR #24 review: these lookups resolve an existing product's brand/category/tag *code*
// (all AdminProductDetailDto carries — see ProductBrandRef/ProductCategoryRef/TagRef) back
// to the publicId this form needs to submit. Filtering isActive:true and capping pageSize
// worked for *picking a new* association, but silently dropped an existing one that had
// since been deactivated (or fell outside the page) — the form would submit an incomplete
// tagPublicIds array, or an empty brand/categoryPublicId, with no error. These fetch the
// full set (no isActive filter, paged through in <=100-sized requests — a flat pageSize:500
// gets rejected server-side, PR #24 review round 2) so every already-assigned code can always
// resolve.
const { data: brandResult, isPending: isBrandListPending, isError: isBrandListError, refetch: refetchBrands } = useFullBrandList()
const { data: categoryResult, isPending: isCategoryListPending, isError: isCategoryListError, refetch: refetchCategories } = useFullCategoryList()
const { data: tagResult, isPending: isTagListPending, isError: isTagListError, refetch: refetchTags } = useFullTagList()
const areLookupsPending = computed(() =>
  isBrandListPending.value || isCategoryListPending.value || isTagListPending.value)
const areLookupsErrored = computed(() =>
  isBrandListError.value || isCategoryListError.value || isTagListError.value)
function retryLookups() {
  refetchBrands()
  refetchCategories()
  refetchTags()
}

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

// PR #24 review round 3: watching all four unconditionally re-ran this on *any* change to any
// of them, including a background refetch of a lookup (e.g. another tab creating a brand
// invalidates ['brands'], or Vue Query's window-refocus refetch) while the admin is mid-edit —
// that would silently discard whatever the admin had typed into name/description/warranty and
// replace it with the server's last-saved values. Initialization now only (re)runs once per
// distinct product (identified by publicId alone) and only after every lookup has settled at
// least once, covering the "lookups arrive after product" race (fixed in round 1).
//
// PR #24 review round 6: the key used to also include rowVersion, which reintroduced the exact
// stomp this guard exists to prevent — creating/editing a SKU below now touches the Product's
// RowVersion (round 5's concurrency fix), so that refetch re-ran this block and overwrote
// whatever the admin had typed into the form above, mid-edit. Keying on publicId alone means the
// form is (re)populated once per product and never again for the same product, no matter what
// causes it to refetch.
const initializedFor = ref<string | null>(null)
// PR #24 review round 7 (P1): round 6 stopped this block from touching the form fields, but
// submitUpdate() still read product.value.rowVersion *live* at submit time — the concurrency
// token itself is not exempt from the same background-refetch problem. Scenario: admin A opens
// this page, admin B saves a change to the same product elsewhere, a background refetch here
// (window refocus, an unrelated invalidation) picks up B's new RowVersion into product.value.
// A submits their still-unsaved-since-open edits; submitUpdate() would send B's RowVersion, the
// optimistic-concurrency check on the server sees a "match" against what's now the current row,
// and silently accepts A's write over B's, discarding B's change with no 409 ever raised — the
// exact lost update the RowVersion check exists to prevent. editRowVersion is the token actually
// sent on submit; it is captured once, at the same moment as the form fields above, and is never
// updated by this watcher.
const editRowVersion = ref<string | null>(null)
// PR #24 review round 8 (P1): a mirror of `form` holding whatever was last confirmed to match the
// server (populated on load, and again after a successful save) — never touched by anything the
// admin types. `isProductFormDirty` compares the two; SKU operations are disabled while it's
// true (see the SkuEditorRow `:operations-disabled` binding below and `submitNewSku`'s guard), so
// `applyProductSnapshot` can safely resync *both* the form fields and the token together whenever
// it runs — there is never unsaved admin input to lose when it does.
const savedForm = reactive({
  nameZhTw: '',
  brandPublicId: '',
  categoryPublicId: '',
  descriptionZhTw: '',
  warrantyMonths: null as number | null,
  status: 'Draft',
  tagCodes: [] as string[],
})
const isProductFormDirty = computed(() => {
  if (
    form.nameZhTw !== savedForm.nameZhTw ||
    form.brandPublicId !== savedForm.brandPublicId ||
    form.categoryPublicId !== savedForm.categoryPublicId ||
    form.descriptionZhTw !== savedForm.descriptionZhTw ||
    form.warrantyMonths !== savedForm.warrantyMonths ||
    form.status !== savedForm.status
  ) {
    return true
  }
  const currentTags = [...form.tagCodes].sort()
  const savedTags = [...savedForm.tagCodes].sort()
  return currentTags.length !== savedTags.length ||
    currentTags.some((code, index) => code !== savedTags[index])
})

function applyProductSnapshot(value: NonNullable<typeof product.value>) {
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
  savedForm.nameZhTw = form.nameZhTw
  savedForm.brandPublicId = form.brandPublicId
  savedForm.categoryPublicId = form.categoryPublicId
  savedForm.descriptionZhTw = form.descriptionZhTw
  savedForm.warrantyMonths = form.warrantyMonths
  savedForm.status = form.status
  savedForm.tagCodes = [...form.tagCodes]
  editRowVersion.value = value.rowVersion
}

watch([product, brandResult, categoryResult, tagResult, areLookupsPending, areLookupsErrored], () => {
  const value = product.value
  if (!value || areLookupsPending.value || areLookupsErrored.value) {
    return
  }
  const key = value.publicId
  if (initializedFor.value === key) {
    return
  }
  initializedFor.value = key
  applyProductSnapshot(value)
}, { immediate: true })

/**
 * PR #24 review round 8 (P1): round 7 refreshed *only* the token after a SKU mutation this admin
 * performed on this page, keeping the form fields untouched — but the SKU write only validates
 * its own token, not the Product's, so it can silently ride past a change another admin made to
 * the product fields in between, advancing the token without ever showing that change. This admin
 * would then still be looking at stale field values, and a subsequent save would send those stale
 * fields with a *validly advanced* token — the server sees no conflict, and the other admin's
 * change to the product fields is silently overwritten. The minimal safe fix the review asked
 * for: SKU operations are disabled whenever `isProductFormDirty` (see SkuEditorRow below), so this
 * only ever runs when the form already matches `savedForm` — meaning there is nothing unsaved to
 * discard, and it's safe (and correct — it also surfaces anything the other admin changed) to
 * resync the *entire* snapshot, fields included, not just the token.
 *
 * `mutatedProductId` is the id the SKU mutation was actually pinned to (from its own frozen
 * variables — see useSkus.ts) — round 8 (P2): if the admin has since navigated to a different
 * product, this result belongs to a page state that no longer exists here and must not be
 * applied.
 */
async function syncRowVersionAfterOwnSkuMutation(mutatedProductId: string) {
  if (props.productId !== mutatedProductId) {
    return
  }
  const result = await refetch()
  if (result.data && props.productId === mutatedProductId) {
    applyProductSnapshot(result.data)
  }
}

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
    defaultSku: {
      skuCode: initialSku.skuCode,
      nameZhTw: initialSku.nameZhTw,
      listPrice: initialSku.listPrice,
      unitCost: initialSku.unitCost,
      weightKg: null,
      lengthCm: null,
      widthCm: null,
      heightCm: null,
      status: initialSku.status,
      isDefault: true,
      requiresPrepayment: initialSku.requiresPrepayment,
      specifications: [],
    },
  }, {
    onSuccess: (created) => router.push(`/products/${created.publicId}`),
  })
}

function submitUpdate() {
  if (!product.value || !editRowVersion.value) {
    return
  }
  const productPublicId = product.value.publicId
  updateMutation.mutate({
    publicId: productPublicId,
    request: {
      nameZhTw: form.nameZhTw,
      brandPublicId: form.brandPublicId,
      categoryPublicId: form.categoryPublicId,
      descriptionZhTw: form.descriptionZhTw || null,
      warrantyMonths: form.warrantyMonths,
      tagPublicIds: tagPublicIds(),
      status: form.status,
      rowVersion: editRowVersion.value,
    },
  }, {
    // A successful save is itself a deliberate, traceable action this admin just took — the
    // response carries the RowVersion that resulted from it, so the next save (if the admin
    // keeps editing) is checked against that, not re-fetched. PR #24 review round 8 (P2): if the
    // admin has since navigated to a different product, this response belongs to a page state
    // that no longer exists here — applying it would stamp the *new* product's token/fields with
    // the *old* product's just-saved values.
    onSuccess: (updated) => {
      if (props.productId !== productPublicId) {
        return
      }
      editRowVersion.value = updated.rowVersion
      savedForm.nameZhTw = form.nameZhTw
      savedForm.brandPublicId = form.brandPublicId
      savedForm.categoryPublicId = form.categoryPublicId
      savedForm.descriptionZhTw = form.descriptionZhTw
      savedForm.warrantyMonths = form.warrantyMonths
      savedForm.status = form.status
      savedForm.tagCodes = [...form.tagCodes]
    },
  })
}

// props.productId (the route param) already equals the product's publicId in edit mode, so the
// mutation composable can be called unconditionally at setup top-level as Vue requires, without
// waiting for `product` to finish loading. PR #24 review round 8 (P2): useCreateSku no longer
// takes a productPublicId at all — round 7's getter fixed the *write* target (resolved once when
// the request starts) but onSuccess re-read the getter again at *completion* time, which could
// have drifted to a different product if the admin navigated away while the request was still in
// flight. productPublicId is now supplied per-call as part of the mutation's own variables (see
// submitNewSku), so mutationFn and onSuccess both see the exact id this specific request was for.
const createSkuMutation = useCreateSku()
const initialSku = reactive({
  skuCode: '',
  nameZhTw: '',
  listPrice: 0,
  unitCost: 0,
  status: 'Draft',
  requiresPrepayment: false,
})
const newSku = reactive({
  skuCode: '',
  nameZhTw: '',
  listPrice: 0,
  unitCost: 0,
  status: 'Draft',
  isDefault: false,
  requiresPrepayment: false,
})

// PR #24 review round 7 (P2): a leftover draft in this row would otherwise ride along across a
// param-only navigation and could be submitted against whichever product the page has since
// switched to. PR #24 review round 8 (P2): `status` was missing from this reset — a status
// picked for product A's draft SKU would still be selected after switching to B. Also resets the
// previous product's create/update mutation error state, which otherwise stayed visible (and
// stale) on the new product's page.
watch(() => props.productId, () => {
  newSku.skuCode = ''
  newSku.nameZhTw = ''
  newSku.listPrice = 0
  newSku.unitCost = 0
  newSku.status = 'Draft'
  newSku.isDefault = false
  newSku.requiresPrepayment = false
  createSkuMutation.reset()
  updateMutation.reset()
})

function submitNewSku() {
  if (!props.productId || isProductFormDirty.value) {
    return
  }
  const productPublicId = props.productId
  createSkuMutation.mutate({
    productPublicId,
    request: {
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
    },
  }, {
    // PR #24 review round 8 (P2): if the admin has since navigated to a different product, this
    // SKU was created for a product that isn't the one shown here anymore — resetting `newSku`
    // (that product's own draft) or resyncing the token here would corrupt the new page's state.
    onSuccess: () => {
      if (props.productId !== productPublicId) {
        return
      }
      newSku.skuCode = ''
      newSku.nameZhTw = ''
      newSku.listPrice = 0
      newSku.unitCost = 0
      newSku.status = 'Draft'
      newSku.isDefault = false
      newSku.requiresPrepayment = false
      void syncRowVersionAfterOwnSkuMutation(productPublicId)
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
    <HttpStatusPage
      v-else-if="isEditMode && isNotFound"
      :status="404"
      home-href="/products"
    />
    <ErrorState
      v-else-if="isEditMode && isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
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
            aria-label="商品代碼"
            required
          >
        </label>
        <label>
          名稱
          <input
            v-model="form.nameZhTw"
            aria-label="商品名稱"
            required
          >
        </label>
        <label>
          品牌
          <select
            v-model="form.brandPublicId"
            aria-label="品牌"
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
            aria-label="分類"
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
          <select
            v-model="form.status"
            aria-label="商品狀態"
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
            <option value="Discontinued">
              已停產
            </option>
          </select>
        </label>
        <fieldset
          v-if="!isEditMode"
          class="product-form__default-sku"
        >
          <legend>第一個預設 SKU</legend>
          <label>
            SKU 代碼
            <input
              v-model="initialSku.skuCode"
              aria-label="預設 SKU 代碼"
              required
            >
          </label>
          <label>
            SKU 名稱
            <input
              v-model="initialSku.nameZhTw"
              aria-label="預設 SKU 名稱"
              required
            >
          </label>
          <label>
            售價
            <input
              v-model.number="initialSku.listPrice"
              type="number"
              min="0"
              step="0.01"
              aria-label="預設 SKU 售價"
              required
            >
          </label>
          <label>
            成本
            <input
              v-model.number="initialSku.unitCost"
              type="number"
              min="0"
              step="0.01"
              aria-label="預設 SKU 成本"
              required
            >
          </label>
          <label>
            狀態
            <select
              v-model="initialSku.status"
              aria-label="預設 SKU 狀態"
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
          </label>
          <label>
            <input
              v-model="initialSku.requiresPrepayment"
              type="checkbox"
            >
            需要預付款
          </label>
        </fieldset>
        <fieldset class="product-form__tags">
          <legend>標籤（最多 {{ MAX_TAG_COUNT }} 個，已選 {{ form.tagCodes.length }} 個）</legend>
          <label
            v-for="tag in tagResult?.items ?? []"
            :key="tag.publicId"
          >
            <input
              v-model="form.tagCodes"
              type="checkbox"
              :value="tag.code"
              :disabled="form.tagCodes.length >= MAX_TAG_COUNT && !form.tagCodes.includes(tag.code)"
            >
            {{ tag.nameZhTw }}
          </label>
        </fieldset>
        <button
          type="submit"
          :disabled="createMutation.isPending.value || updateMutation.isPending.value || areLookupsPending || areLookupsErrored"
        >
          儲存
        </button>
        <p
          v-if="areLookupsPending"
          class="product-form__hint"
        >
          品牌／分類／標籤資料載入中，載入完成前無法儲存，以免遺漏既有關聯。
        </p>
        <p
          v-if="areLookupsErrored"
          class="product-form__error"
        >
          品牌／分類／標籤資料載入失敗，無法安全判斷既有關聯，暫時無法儲存。
          <button
            type="button"
            @click="retryLookups"
          >
            重試
          </button>
        </p>
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
        <p
          v-if="isProductFormDirty"
          class="product-form__hint"
        >
          商品資料有未儲存的變更，請先儲存或還原後再操作 SKU。
        </p>
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
              :operations-disabled="isProductFormDirty"
              @sku-mutated="syncRowVersionAfterOwnSkuMutation"
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
                  min="0"
                  step="0.01"
                  aria-label="新 SKU 售價"
                >
              </td>
              <td>
                <input
                  v-model.number="newSku.unitCost"
                  type="number"
                  min="0"
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
                  <option value="Unpublished">
                    已下架
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
                  :disabled="createSkuMutation.isPending.value || isProductFormDirty"
                  :title="isProductFormDirty ? '商品資料有未儲存的變更，請先儲存或還原後再操作 SKU' : undefined"
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

.product-form__default-sku {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
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

.product-form__hint {
  color: #6b7280;
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
