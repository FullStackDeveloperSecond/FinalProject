<script setup lang="ts">
import { ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import BuildItemsEditor, { type EditableBuildItem } from '../features/builds/components/BuildItemsEditor.vue'
import CompatibilityFindingsList from '../features/builds/components/CompatibilityFindingsList.vue'
import {
  useAddBuildToCart,
  useBuildList,
  useCreateBuildShare,
  useDeleteBuildList,
  useRevokeBuildShare,
  useUpdateBuildList,
} from '../features/builds/useBuilds'
import type { BuildShareDto } from '../features/builds/types'

const props = defineProps<{ buildId: string }>()
const router = useRouter()

const { data: buildList, isPending, isError, error, refetch } = useBuildList(() => props.buildId)
const updateBuildList = useUpdateBuildList()
const deleteBuildList = useDeleteBuildList()
const createShare = useCreateBuildShare(() => props.buildId)
const revokeShare = useRevokeBuildShare(() => props.buildId)
const addToCart = useAddBuildToCart()

const name = ref('')
const items = ref<EditableBuildItem[]>([])
const hasConcurrencyConflict = ref(false)
const saveError = ref<unknown>(null)

watch(buildList, (value) => {
  if (value && items.value.length === 0 && !name.value) {
    resetFromServer()
  }
}, { immediate: true })

function resetFromServer(): void {
  if (!buildList.value) {
    return
  }
  name.value = buildList.value.name
  items.value = buildList.value.items.map((item) => ({
    skuPublicId: item.skuPublicId, quantity: Number(item.quantity), name: item.name, categoryCode: item.categoryCode,
  }))
  hasConcurrencyConflict.value = false
  saveError.value = null
}

async function reloadAndDiscardEdits(): Promise<void> {
  await refetch()
  resetFromServer()
}

/**
 * 組長 PR #35 round-2 review, P1-2: the editor only ever mutates local `name`/`items`; nothing
 * else on this page reads them. Compatibility, price totals, the share link, and add-to-cart all
 * read straight from `buildList.value` (the last-fetched server state) — so a shopper who swaps a
 * part but never clicks "儲存變更" could still share or add-to-cart, and the backend would act on
 * whatever it already has stored, not the edit currently on screen. `isDirty` is the single source
 * of truth both actions gate on below; order-independent since BuildItemsEditor's emitted array
 * order isn't guaranteed to match the server's stored order.
 */
function itemsSignature(list: { skuPublicId: string, quantity: number }[]): string {
  return list.map((item) => `${item.skuPublicId}:${item.quantity}`).sort().join('|')
}

const isDirty = computed(() => {
  const build = buildList.value
  if (!build) {
    return false
  }
  if (name.value !== build.name) {
    return true
  }
  const serverSignature = itemsSignature(build.items.map((item) => ({ skuPublicId: item.skuPublicId, quantity: Number(item.quantity) })))
  return itemsSignature(items.value) !== serverSignature
})

const unsavedEditsMessage = computed(() => (
  isDirty.value ? '您有尚未儲存的變更，請先按「儲存變更」或「放棄變更」，才能分享或加入購物車。' : null
))

async function save(): Promise<void> {
  if (!buildList.value) {
    return
  }

  saveError.value = null
  try {
    await updateBuildList.mutateAsync({
      publicId: props.buildId,
      request: {
        name: name.value,
        items: items.value.map((item) => ({ skuPublicId: item.skuPublicId, quantity: item.quantity })),
        rowVersion: buildList.value.rowVersion,
      },
    })
  } catch (error) {
    if (isApiError(error) && error.code === 'concurrency_conflict') {
      hasConcurrencyConflict.value = true
      return
    }
    saveError.value = error
  }
}

const showDeleteConfirm = ref(false)
const deleteError = ref<unknown>(null)

async function confirmDelete(): Promise<void> {
  if (!buildList.value) {
    return
  }
  deleteError.value = null
  try {
    await deleteBuildList.mutateAsync({ publicId: props.buildId, rowVersion: buildList.value.rowVersion })
    await router.push('/account/builds')
  } catch (error) {
    deleteError.value = error
  }
}

/**
 * 組長 PR #35 review, item 3: the backend only ever persists the share token's hash (see
 * `BuildActiveShareDto`'s remarks), so a share URL created in a *past* visit can never be shown
 * again — `buildList.value.activeShare` (from the real, regenerated GET) only carries whether
 * one exists and when it expires. `justCreatedShare` is the one place the openable URL is ever
 * visible at all: the moment `createShare` (or `revoke`) itself returns it.
 */
const justCreatedShare = ref<BuildShareDto | null>(null)
const shareError = ref<unknown>(null)
const recoveredActiveShare = computed(() => buildList.value?.activeShare ?? null)
const hasAnyActiveShare = computed(() => justCreatedShare.value !== null || recoveredActiveShare.value !== null)

async function share(): Promise<void> {
  if (isDirty.value) {
    return
  }
  shareError.value = null
  try {
    justCreatedShare.value = await createShare.mutateAsync()
  } catch (error) {
    shareError.value = error
  }
}

async function revoke(): Promise<void> {
  shareError.value = null
  try {
    await revokeShare.mutateAsync()
    justCreatedShare.value = null
  } catch (error) {
    shareError.value = error
  }
}

const cartQuantity = ref(1)
const cartResultMessage = ref<string | null>(null)
const cartError = ref<unknown>(null)
// 組長 PR #35 review, item 5 (P2): mirrors EfBuildListService.GetSharedBuildAsync's own
// `canAddToCart` computation exactly — the button used to disable only for `overall === 'blocked'`
// and let a shopper submit `insufficientData` (including "missing one of the 8 required
// categories"), an unavailable/insufficient-stock item, or a disabled-rule finding straight
// through to a guaranteed backend rejection. Proactively showing why beats a generic error after
// the fact.
const cartBlockReason = computed<string | null>(() => {
  const build = buildList.value
  if (!build) {
    return null
  }
  if (isDirty.value) {
    return unsavedEditsMessage.value
  }
  if (build.compatibility.overall === 'blocked') {
    return '此組裝清單目前不相容，請先解決相容性問題才能加入購物車。'
  }
  if (build.compatibility.overall === 'insufficientData') {
    return '尚缺少必要元件（CPU、主機板、記憶體、顯示卡、儲存裝置、電源供應器、機殼、散熱器），請補齊後再加入購物車。'
  }
  if (build.items.some((item) => item.availability !== 'available')) {
    return '有品項已下架或庫存不足，請先調整後再加入購物車。'
  }
  if (build.compatibility.results.some((finding) => finding.severity === 'ruleDisabled')) {
    return '有相容性規則目前已停用，需先確認狀態才能加入購物車。'
  }
  return null
})
const canAddToCart = computed(() => cartBlockReason.value === null)

// 組長 PR #35 review, item 4: a fresh crypto.randomUUID() on every call meant a retry after a
// lost response (backend wrote it, shopper never saw the reply) used a different Idempotency-Key
// than the original attempt — the backend has no way to recognize it as the same logical
// operation, so a retry could add the whole build a second time. One logical add-to-cart attempt
// and its safe retries must share one key until it either succeeds or the shopper changes the
// input (a different quantity is a genuinely different operation, not a retry of the same one).
let cartIdempotencyKey = crypto.randomUUID()
watch(cartQuantity, () => { cartIdempotencyKey = crypto.randomUUID() })

async function addBuildToCart(): Promise<void> {
  if (!buildList.value || isDirty.value) {
    return
  }
  cartResultMessage.value = null
  cartError.value = null
  try {
    await addToCart.mutateAsync({
      publicId: props.buildId,
      request: { quantity: cartQuantity.value, buildRowVersion: buildList.value.rowVersion },
      idempotencyKey: cartIdempotencyKey,
    })
    cartResultMessage.value = '已加入購物車。'
    cartIdempotencyKey = crypto.randomUUID()
  } catch (error) {
    cartError.value = error
  }
}
</script>

<template>
  <section aria-labelledby="build-detail-page-title">
    <LoadingState
      v-if="isPending"
      label="組裝清單載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />

    <template v-else-if="buildList">
      <div class="build-detail-page__header">
        <h1 id="build-detail-page-title">
          {{ buildList.name }}
        </h1>
        <button
          type="button"
          @click="showDeleteConfirm = true"
        >
          刪除清單
        </button>
      </div>

      <div
        v-if="showDeleteConfirm"
        class="build-detail-page__confirm"
        role="alertdialog"
        aria-label="確認刪除組裝清單"
      >
        <p>
          確定要刪除「{{ buildList.name }}」嗎？此操作無法復原，清單中的所有零件設定將永久消失。
        </p>
        <div class="build-detail-page__confirm-actions">
          <button
            type="button"
            :disabled="deleteBuildList.isPending.value"
            @click="confirmDelete"
          >
            確定刪除
          </button>
          <button
            type="button"
            @click="showDeleteConfirm = false"
          >
            取消
          </button>
        </div>
        <ErrorState
          v-if="deleteError"
          title="刪除失敗"
          :correlation-id="isApiError(deleteError) ? deleteError.correlationId : undefined"
          @retry="confirmDelete"
        />
      </div>

      <div
        v-if="hasConcurrencyConflict"
        class="build-detail-page__conflict"
        role="alert"
      >
        <p>此清單已被其他裝置或分頁修改過，為避免覆蓋新版本，請重新載入後再編輯。</p>
        <button
          type="button"
          @click="reloadAndDiscardEdits"
        >
          重新載入最新版本
        </button>
      </div>

      <div class="build-detail-page__field">
        <label for="build-detail-name">清單名稱</label>
        <input
          id="build-detail-name"
          v-model="name"
          type="text"
          maxlength="160"
        >
      </div>

      <BuildItemsEditor
        :items="items"
        :disabled="updateBuildList.isPending.value"
        @update:items="(next) => { items = next }"
      />

      <div class="build-detail-page__actions">
        <button
          type="button"
          :disabled="updateBuildList.isPending.value"
          @click="save"
        >
          儲存變更
        </button>
        <button
          type="button"
          @click="resetFromServer"
        >
          放棄變更
        </button>
      </div>
      <ErrorState
        v-if="saveError"
        title="儲存失敗"
        :correlation-id="isApiError(saveError) ? saveError.correlationId : undefined"
        :description="isApiError(saveError) ? saveError.message : undefined"
        @retry="save"
      />

      <CompatibilityFindingsList
        :overall="buildList.compatibility.overall"
        :results="buildList.compatibility.results"
      />

      <dl class="build-detail-page__totals">
        <dt>商品小計</dt>
        <dd>NT${{ buildList.totals.merchandise.toLocaleString('zh-Hant-TW') }}</dd>
        <dt>組裝費</dt>
        <dd>NT${{ buildList.totals.assemblyFee.toLocaleString('zh-Hant-TW') }}</dd>
        <dt>合計</dt>
        <dd>NT${{ buildList.totals.grandTotal.toLocaleString('zh-Hant-TW') }}</dd>
      </dl>

      <section
        class="build-detail-page__section"
        aria-labelledby="build-detail-share-title"
      >
        <h2 id="build-detail-share-title">
          分享
        </h2>
        <p v-if="unsavedEditsMessage">
          {{ unsavedEditsMessage }}
        </p>
        <template v-else-if="justCreatedShare">
          <p>分享連結：<code>{{ justCreatedShare.url }}</code></p>
        </template>
        <p v-else-if="recoveredActiveShare">
          目前有作用中的分享連結（重新載入頁面後無法再顯示原始網址，僅能撤銷或重新產生）。
        </p>
        <div class="build-detail-page__share-actions">
          <button
            type="button"
            :disabled="createShare.isPending.value || isDirty"
            @click="share"
          >
            {{ hasAnyActiveShare ? '重新產生連結' : '建立分享連結' }}
          </button>
          <button
            v-if="hasAnyActiveShare"
            type="button"
            :disabled="revokeShare.isPending.value"
            @click="revoke"
          >
            撤銷分享
          </button>
        </div>
        <ErrorState
          v-if="shareError"
          title="分享操作失敗"
          :correlation-id="isApiError(shareError) ? shareError.correlationId : undefined"
        />
      </section>

      <section
        class="build-detail-page__section"
        aria-labelledby="build-detail-cart-title"
      >
        <h2 id="build-detail-cart-title">
          加入購物車
        </h2>
        <p v-if="cartBlockReason">
          {{ cartBlockReason }}
        </p>
        <div class="build-detail-page__cart-controls">
          <label for="cart-quantity">數量</label>
          <input
            id="cart-quantity"
            v-model.number="cartQuantity"
            type="number"
            min="1"
            max="8"
          >
          <button
            type="button"
            :disabled="!canAddToCart || addToCart.isPending.value"
            @click="addBuildToCart"
          >
            加入購物車
          </button>
        </div>
        <p v-if="cartResultMessage">
          {{ cartResultMessage }}
        </p>
        <ErrorState
          v-if="cartError"
          title="加入購物車失敗"
          :correlation-id="isApiError(cartError) ? cartError.correlationId : undefined"
          :description="isApiError(cartError) ? cartError.message : undefined"
          @retry="addBuildToCart"
        />
      </section>
    </template>
  </section>
</template>

<style scoped>
.build-detail-page__share-actions {
  display: flex;
  gap: 0.75rem;
}

.build-detail-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-block-end: 1rem;
}

.build-detail-page__confirm,
.build-detail-page__conflict {
  padding: 1rem;
  border-radius: 0.5rem;
  margin-block-end: 1.5rem;
}

.build-detail-page__confirm {
  border: 1px solid #fca5a5;
  background: #fef2f2;
}

.build-detail-page__conflict {
  border: 1px solid #fcd34d;
  background: #fffbeb;
}

.build-detail-page__confirm-actions {
  display: flex;
  gap: 0.5rem;
  margin-block-start: 0.75rem;
}

.build-detail-page__field {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  max-width: 24rem;
  margin-block-end: 1.5rem;
}

.build-detail-page__field input {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.build-detail-page__actions {
  display: flex;
  gap: 0.5rem;
  margin-block: 1.5rem;
}

.build-detail-page__totals {
  display: grid;
  grid-template-columns: auto auto;
  gap: 0.25rem 1rem;
  margin-block: 1.5rem;
}

.build-detail-page__totals dt {
  color: #4b5563;
}

.build-detail-page__totals dd {
  margin: 0;
  font-weight: 700;
}

.build-detail-page__section {
  margin-block-start: 2rem;
  padding-block-start: 1.5rem;
  border-top: 1px solid #e5e7eb;
}

.build-detail-page__cart-controls {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.build-detail-page__cart-controls input {
  width: 5rem;
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}
</style>
