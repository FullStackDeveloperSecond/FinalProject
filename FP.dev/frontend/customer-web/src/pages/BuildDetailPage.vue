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
import { validateBuildItems, type BuildShareDto } from '../features/builds/types'

const props = defineProps<{ buildId: string }>()
const router = useRouter()

const { data: buildList, isPending, isError, error, refetch } = useBuildList(() => props.buildId)
const updateBuildList = useUpdateBuildList()
const deleteBuildList = useDeleteBuildList()
const createShare = useCreateBuildShare()
const revokeShare = useRevokeBuildShare()
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

/**
 * 組長 PR #35 round-4 review, P2: 上一輪的 `watch(() => props.buildId, ...)` 只在參數改變的「當下」
 * 清空本地狀態，對「已經送出、還沒回應」的請求無效——Vue Router 在同一個 route record 上換
 * :buildId 不會卸載元件，所以清單 A 的 async function 會繼續跑完，把 A 的結果寫進正在顯示 B 的
 * 同一個元件實例（A 的分享網址出現在 B 頁、B 頁顯示「已加入購物車」但加的是 A）。
 *
 * 每個操作在開始時 snapshot 當下的 buildId，每次 await 之後先用這個函式確認使用者仍在同一份清單，
 * 才允許寫入 UI 狀態或導航。刻意不取消已送出的請求：那些是後端已經受理的安全操作（分享、加入
 * 購物車），取消只會讓前後端狀態不一致；要擋的是「遲到的結果污染新頁面」，不是操作本身。
 */
function isStillViewing(requestedBuildId: string): boolean {
  return props.buildId === requestedBuildId
}

async function reloadAndDiscardEdits(): Promise<void> {
  // 自我審查補充（組長未點名，但與 P2 同一類）：refetch 完成前若已切到另一份清單，
  // resetFromServer() 會把 A 的伺服器資料寫進正在顯示 B 的 editor。
  const requestedBuildId = props.buildId
  await refetch()
  if (!isStillViewing(requestedBuildId)) {
    return
  }
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

// 組長 PR #35 round-3 review, P1-2: mirrors EfCompatibilityCheckService.MergeAndValidateItems's
// own bounds (1–20 items, 1–8 per SKU) — must gate "儲存變更" the same way NewBuildPage.vue's
// "儲存為我的清單" is gated, not just left for the backend to reject after the fact.
const itemsValidation = computed(() => validateBuildItems(items.value))

// 組長 PR #35 round-6 review, P2-2: `save()` only ever gated on `itemsValidation` — the name field
// could be cleared to blank/whitespace-only and "儲存變更" would still submit it, even though the
// backend's `UpdateBuildListRequest.Name` is `[Required, StringLength(160, MinimumLength = 1)]`
// (BuildListContracts.cs) and would reject it. NewBuildPage.vue's own `canSave` already requires
// `name.value.trim().length > 0` for the same field on create — this mirrors that exact check for
// update, rather than leaving this page as the one place a blank name can reach the backend.
const isNameValid = computed(() => name.value.trim().length > 0)

const unsavedEditsMessage = computed(() => (
  isDirty.value ? '您有尚未儲存的變更，請先按「儲存變更」或「放棄變更」，才能分享或加入購物車。' : null
))

async function save(): Promise<void> {
  if (!buildList.value || !itemsValidation.value.isValid || !isNameValid.value) {
    return
  }

  const requestedBuildId = props.buildId
  saveError.value = null
  try {
    await updateBuildList.mutateAsync({
      publicId: requestedBuildId,
      request: {
        name: name.value,
        items: items.value.map((item) => ({ skuPublicId: item.skuPublicId, quantity: item.quantity })),
        rowVersion: buildList.value.rowVersion,
      },
    })
    // 組長 PR #35 round-3 review: mutation 成功後要以 server response 重設本地 editor
    // baseline／dirty state。updateBuildList 的 onSuccess 已經用回傳的 BuildListDto
    // setQueryData，所以這裡的 buildList.value 已經是伺服器合併後的最新資料（例如同一顆 SKU
    // 被 MergeAndValidateItems 合併成一列）——重新套用它，讓本地 items／name 與 isDirty 不會停在
    // 送出當下（可能尚未合併）的那份舊值。
    if (!isStillViewing(requestedBuildId)) {
      return
    }
    resetFromServer()
  } catch (error) {
    if (!isStillViewing(requestedBuildId)) {
      return
    }
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
  const requestedBuildId = props.buildId
  deleteError.value = null
  try {
    await deleteBuildList.mutateAsync({ publicId: requestedBuildId, rowVersion: buildList.value.rowVersion })
    if (!isStillViewing(requestedBuildId)) {
      return
    }
    await router.push('/account/builds')
  } catch (error) {
    if (!isStillViewing(requestedBuildId)) {
      return
    }
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
  const requestedBuildId = props.buildId
  shareError.value = null
  try {
    const created = await createShare.mutateAsync(requestedBuildId)
    if (!isStillViewing(requestedBuildId)) {
      return
    }
    justCreatedShare.value = created
  } catch (error) {
    if (!isStillViewing(requestedBuildId)) {
      return
    }
    shareError.value = error
  }
}

async function revoke(): Promise<void> {
  const requestedBuildId = props.buildId
  shareError.value = null
  try {
    await revokeShare.mutateAsync(requestedBuildId)
    if (!isStillViewing(requestedBuildId)) {
      return
    }
    justCreatedShare.value = null
  } catch (error) {
    if (!isStillViewing(requestedBuildId)) {
      return
    }
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
// 組長 PR #35 round-6 review, P2-2: the quantity `<input type="number" min="1" max="8">` is a UI
// hint only — nothing stopped a manually-typed 0, 9, a decimal, or an emptied field from reaching
// `addBuildToCart()`. The backend's `AddBuildToCartRequest` is `[Range(1, 8)] int Quantity`
// (BuildListContracts.cs); `cartQuantity` itself can end up as a non-numeric string here (Vue's
// `.number` modifier falls back to the raw string when `parseFloat` can't parse it, e.g. an
// emptied field), so this checks `Number.isInteger` rather than trusting the ref's declared type.
const isCartQuantityValid = computed(() => Number.isInteger(cartQuantity.value) && cartQuantity.value >= 1 && cartQuantity.value <= 8)
const canAddToCart = computed(() => cartBlockReason.value === null && isCartQuantityValid.value)

// 組長 PR #35 review, item 4: a fresh crypto.randomUUID() on every call meant a retry after a
// lost response (backend wrote it, shopper never saw the reply) used a different Idempotency-Key
// than the original attempt — the backend has no way to recognize it as the same logical
// operation, so a retry could add the whole build a second time. One logical add-to-cart attempt
// and its safe retries must share one key until it either succeeds or the shopper changes the
// input (a different quantity is a genuinely different operation, not a retry of the same one).
let cartIdempotencyKey = crypto.randomUUID()
watch(cartQuantity, () => { cartIdempotencyKey = crypto.randomUUID() })

/**
 * 組長 PR #35 round-3 review, P2-5: Vue Router 在同一個 route record 上只換 :buildId 參數時
 * 不會卸載／重新掛載這個元件（先例：ProductDetailPage.vue 的 selectedSkuPublicId，PR #24
 * review 已修過一次同類問題）——若不主動重置，從 /builds/A 導覽到 /builds/B 時，name／items
 * 等本地狀態會殘留 A 清單的內容，短暫地跟 B 的 buildList／compatibility／totals 顯示混在一起，
 * 且下面 `watch(buildList, ...)` 的自動帶入邏輯只在「items 是空的」時才會觸發，殘留的舊資料會讓
 * 它誤判成「使用者已經在編輯」而不去帶入 B 的資料。換 buildId 時先清空全部本地暫存狀態，讓
 * 上面的 watch 在新資料到達時能重新走一次正常的初次帶入流程；購物車 idempotency key 也要換新
 * 的，因為換了清單就是全新的一次邏輯操作，不是同一個操作的重試。
 */
watch(() => props.buildId, () => {
  name.value = ''
  items.value = []
  hasConcurrencyConflict.value = false
  saveError.value = null
  showDeleteConfirm.value = false
  deleteError.value = null
  justCreatedShare.value = null
  shareError.value = null
  cartQuantity.value = 1
  cartResultMessage.value = null
  cartError.value = null
  cartIdempotencyKey = crypto.randomUUID()
})

async function addBuildToCart(): Promise<void> {
  if (!buildList.value || isDirty.value || !isCartQuantityValid.value) {
    return
  }
  const requestedBuildId = props.buildId
  cartResultMessage.value = null
  cartError.value = null
  try {
    await addToCart.mutateAsync({
      publicId: requestedBuildId,
      request: { quantity: cartQuantity.value, buildRowVersion: buildList.value.rowVersion },
      idempotencyKey: cartIdempotencyKey,
    })
    if (!isStillViewing(requestedBuildId)) {
      return
    }
    cartResultMessage.value = '已加入購物車。'
    cartIdempotencyKey = crypto.randomUUID()
  } catch (error) {
    if (!isStillViewing(requestedBuildId)) {
      return
    }
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
        <p
          v-if="!isNameValid"
          class="build-detail-page__validation-error"
        >
          清單名稱不可為空白。
        </p>
      </div>

      <BuildItemsEditor
        :items="items"
        :disabled="updateBuildList.isPending.value"
        @update:items="(next) => { items = next }"
      />

      <ul
        v-if="!itemsValidation.isValid"
        class="build-detail-page__items-errors"
      >
        <li
          v-for="itemError in itemsValidation.errors"
          :key="itemError"
        >
          {{ itemError }}
        </li>
      </ul>

      <div class="build-detail-page__actions">
        <button
          type="button"
          :disabled="!itemsValidation.isValid || !isNameValid || updateBuildList.isPending.value"
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
        <p
          v-if="!isCartQuantityValid"
          class="build-detail-page__validation-error"
        >
          數量須為 1–8 之間的整數。
        </p>
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
  border: 1px solid var(--color-danger-border);
  background: var(--color-danger-bg);
}

.build-detail-page__conflict {
  border: 1px solid var(--color-butter);
  background: var(--color-warning-bg);
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
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  font: inherit;
}

.build-detail-page__items-errors {
  margin: 1rem 0 0;
  padding-left: 1.25rem;
  color: var(--color-danger);
  font-size: 0.875rem;
}

.build-detail-page__validation-error {
  margin: 0.25rem 0 0;
  font-size: 0.8125rem;
  color: var(--color-danger);
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
  color: var(--color-text-muted);
}

.build-detail-page__totals dd {
  margin: 0;
  font-weight: 700;
}

.build-detail-page__section {
  margin-block-start: 2rem;
  padding-block-start: 1.5rem;
  border-top: 1px solid var(--color-border-soft);
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
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  font: inherit;
}
</style>
