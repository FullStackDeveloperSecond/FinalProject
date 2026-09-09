<script setup lang="ts">
import { ErrorState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import BuildItemsEditor, { type EditableBuildItem } from '../features/builds/components/BuildItemsEditor.vue'
import CompatibilityFindingsList from '../features/builds/components/CompatibilityFindingsList.vue'
import { useCompatibilityCheck, useCreateBuildList } from '../features/builds/useBuilds'
import { validateBuildItems } from '../features/builds/types'
import {
  clearGuestBuildDraft,
  consumePendingBuildSaveResume,
  loadGuestBuildDraft,
  markPendingBuildSaveResume,
  saveGuestBuildDraft,
} from '../features/builds/guestBuildDraft'
import { useSessionStore } from '../stores/session'
import CartBuildPicker from '../features/builds/components/CartBuildPicker.vue'
import { clearPendingBuildImport, readPendingBuildImport, mergeBuildImport, type BuildImport, type OwnedBuildPart } from '../features/builds/buildImport'
import { addBuildToCart } from '../features/builds/api'
import { getCart } from '../features/cart/api'
import { useQueryClient } from '@tanstack/vue-query'
import type { AddBuildToCartRequest, BuildListDto } from '../features/builds/types'

const router = useRouter()
const route = useRoute()
const sessionStore = useSessionStore()

const draft = loadGuestBuildDraft()
const name = ref(draft.name)
const items = ref<EditableBuildItem[]>(draft.items)
const ownedParts = ref<OwnedBuildPart[]>(draft.ownedParts ?? [])
const cartSource = ref(draft.cartSource)
const pendingImport = ref(readPendingBuildImport())
const showCartPicker = ref(route.query.import === 'cart')
const importError = ref('')
const queryClient = useQueryClient()
const purchasing = ref(false)
let pageActive = true
const purchaseError = ref('')
let savedForPurchase: { signature: string, build: BuildListDto } | null = null
let purchaseAttempt: { publicId: string, request: AddBuildToCartRequest, key: string } | null = null
const isBusy = computed(() => purchasing.value || createBuildList.isPending.value)

function previewImport(value: BuildImport): void { pendingImport.value = value; importError.value = '' }
function applyImport(mode: 'keep' | 'replace' | 'reset'): void {
  if (!pendingImport.value) return
  try {
    const merged = mergeBuildImport({ name: name.value, items: items.value, ownedParts: ownedParts.value, cartSource: cartSource.value }, pendingImport.value, mode)
    name.value = merged.name
    items.value = merged.items
    ownedParts.value = merged.ownedParts ?? []
    cartSource.value = merged.cartSource
    pendingImport.value = null
    clearPendingBuildImport()
    showCartPicker.value = false
    importError.value = ''
  } catch (caught) { importError.value = caught instanceof Error ? caught.message : '匯入失敗。' }
}
function cancelImport(): void { pendingImport.value = null; clearPendingBuildImport(); importError.value = '' }

function requestBody() {
  return { name: name.value, items: items.value.map(item => ({ skuPublicId: item.skuPublicId, quantity: item.quantity })), ownedParts: ownedParts.value }
}
async function saveAndPurchase(): Promise<void> {
  if (!canSave.value || isBusy.value || items.value.length === 0) return
  if (!sessionStore.isAuthenticated) {
    // Saving can resume after login, but cart transfer must be reconfirmed after guest/member cart merge.
    await router.push({ path: '/login', query: { redirect: route.fullPath } })
    return
  }
  purchasing.value = true
  purchaseError.value = ''
  const actor = sessionStore.user?.publicId
  const stillCurrent = () => pageActive && sessionStore.isAuthenticated && sessionStore.user?.publicId === actor
  try {
    const body = requestBody()
    const signature = JSON.stringify(body)
    if (!savedForPurchase || savedForPurchase.signature !== signature) {
      savedForPurchase = { signature, build: await createBuildList.mutateAsync(body) }
      purchaseAttempt = null
    }
    if (!stillCurrent()) return
    if (!purchaseAttempt) {
      const cart = await getCart()
      if (!stillCurrent()) return
      const source = cartSource.value
      if (source && (source.publicId !== cart.publicId || source.rowVersion !== cart.rowVersion))
        throw new Error('購物車已變更（例如登入合併或數量調整）。請移除舊的購物車匯入零件，再從目前購物車重新挑選，避免重複加購。')
      const transfers = (source?.items ?? []).flatMap(row => {
        const quantity = Math.min(row.quantity, items.value.find(item => item.skuPublicId === row.skuPublicId)?.quantity ?? 0)
        return quantity > 0 ? [{ cartItemPublicId: row.cartItemPublicId, quantity }] : []
      })
      purchaseAttempt = { publicId: savedForPurchase.build.publicId, key: crypto.randomUUID(), request: {
        quantity: 1, buildRowVersion: savedForPurchase.build.rowVersion, cartRowVersion: cart.rowVersion, cartTransfers: transfers,
      } }
    }
    await addBuildToCart(purchaseAttempt.publicId, purchaseAttempt.request, purchaseAttempt.key)
    await queryClient.invalidateQueries({ queryKey: ['cart'] })
    if (!stillCurrent()) return
    clearGuestBuildDraft()
    await router.push('/cart')
  } catch (caught) {
    if (!stillCurrent()) return
    purchaseError.value = isApiError(caught) ? caught.message : caught instanceof Error ? caught.message : '加入購物車失敗，請重試。'
    // A definitive rejection is safe to re-read; uncertain responses keep the exact key AND payload.
    if (isApiError(caught) && [400, 404, 409].includes(caught.status)) purchaseAttempt = null
  } finally { purchasing.value = false }
}

function removeCartImport(): void {
  const importedSkus = new Set(cartSource.value?.items.map(item => item.skuPublicId))
  items.value = items.value.filter(item => !importedSkus.has(item.skuPublicId))
  cartSource.value = undefined
  purchaseAttempt = null
}

const compatibilityCheck = useCompatibilityCheck()
const createBuildList = useCreateBuildList()

let debounceHandle: ReturnType<typeof setTimeout> | undefined

function runCompatibilityCheck(): void {
  if (items.value.length + ownedParts.value.length === 0) {
    return
  }

  compatibilityCheck.mutate({
    items: items.value.map((item) => ({ skuPublicId: item.skuPublicId, quantity: item.quantity })),
    ownedParts: ownedParts.value,
  })
}

const canSave = ref(false)
// 組長 PR #35 round-3 review, P1-2: mirrors EfCompatibilityCheckService.MergeAndValidateItems's
// own bounds (1–20 items, 1–8 per SKU) — must be checked before "儲存為我的清單" is even
// clickable, not just left for the backend to reject after the fact.
const itemsValidation = ref(validateBuildItems([]))

watch([name, items, ownedParts, cartSource], () => {
  try { saveGuestBuildDraft({ name: name.value, items: items.value, ownedParts: ownedParts.value, cartSource: cartSource.value }) }
  catch { importError.value = '瀏覽器無法儲存草稿，離開前請先儲存為會員清單。' }
  itemsValidation.value = validateBuildItems([...items.value, ...ownedParts.value.map((part, index) => ({ skuPublicId: part.skuPublicId ?? `owned-${index}`, quantity: Number(part.quantity) }))])
  canSave.value = name.value.trim().length > 0 && itemsValidation.value.isValid

  clearTimeout(debounceHandle)
  debounceHandle = setTimeout(runCompatibilityCheck, 500)
}, { deep: true, immediate: true })
onBeforeUnmount(() => { pageActive = false; clearTimeout(debounceHandle) })

const saveError = ref<unknown>(null)

async function save(): Promise<void> {
  saveError.value = null
  try {
    const buildList = await createBuildList.mutateAsync(requestBody())
    clearGuestBuildDraft()
    await router.push(`/builds/${buildList.publicId}`)
  } catch (error) {
    // 組長 PR #35 review, item 2: a guest without a session used to be sent to /unauthorized —
    // a dead end that abandons the draft. Redirect to login instead, preserving this exact page
    // (draft included, since it only clears from localStorage on a real 200) as the return
    // target; `autoResumeAfterLogin` below finishes the save once they're back and authenticated.
    if (isApiError(error) && error.status === 401) {
      markPendingBuildSaveResume()
      await router.push({ path: '/login', query: { redirect: route.fullPath } })
      return
    }

    saveError.value = error
  }
}

// 組長 PR #35 review, item 2: "登入成功後再把 localStorage 草稿建立成新的會員清單" — once the
// shopper is back on this exact page (LoginForm's redirect target) and the session has actually
// resolved to authenticated, finish the save automatically instead of making them press the
// button again. Guarded to fire at most once per page load so it can't loop if the create call
// itself keeps failing for some other reason.
//
// 組長 PR #35 round-2 review, P1-1: "session authenticated + a draft exists" isn't proof this page
// load is actually that specific guest-save -> login -> return round trip — an already-logged-in
// member just visiting /builds/new, or one who switched accounts while an old draft was still in
// localStorage, both satisfy the same two conditions and would have their own account silently
// import whatever stale draft happens to be sitting around. `consumePendingBuildSaveResume()`
// only returns true when `save()`'s own 401 handler set the marker moments before this exact
// redirect — a one-shot signal for this one round trip, not a standing "always auto-import" rule.
let hasAttemptedAutoResume = false
watch(() => sessionStore.status, (status) => {
  if (status !== 'authenticated' || hasAttemptedAutoResume || !canSave.value) {
    return
  }
  hasAttemptedAutoResume = true
  if (!consumePendingBuildSaveResume()) {
    return
  }
  void save()
}, { immediate: true })
</script>

<template>
  <section
    class="store-workspace"
    aria-labelledby="new-build-page-title"
  >
    <h1 id="new-build-page-title">
      新增組裝清單
    </h1>
    <p class="new-build-page__hint">
      在登入並儲存之前，這份清單只會暫存在此瀏覽器中。
    </p>

    <div class="new-build-page__field">
      <label for="build-name">清單名稱</label>
      <input
        id="build-name"
        v-model="name"
        :disabled="isBusy"
        type="text"
        maxlength="160"
        placeholder="例如：文書機、電競主機"
      >
    </div>

    <div class="new-build-page__import-links">
      <button
        type="button"
        :disabled="isBusy"
        @click="showCartPicker = !showCartPicker"
      >
        從購物車挑選
      </button>
      <RouterLink to="/account/favorites">
        從收藏挑選
      </RouterLink>
      <RouterLink to="/products">
        從商品挑選
      </RouterLink>
      <RouterLink to="/ai-search">
        前往 AI 靈感站
      </RouterLink>
    </div>
    <CartBuildPicker
      v-if="showCartPicker && !isBusy"
      @select="previewImport"
    />
    <section
      v-if="pendingImport"
      aria-label="確認匯入組裝清單"
    >
      <h2>確認匯入內容</h2>
      <ul>
        <li
          v-for="item in pendingImport.items"
          :key="item.skuPublicId"
        >
          新購：{{ item.name }} × {{ item.quantity }}
        </li>
        <li
          v-for="(part, index) in pendingImport.ownedParts"
          :key="index"
        >
          自有：{{ part.displayName }} × {{ part.quantity }}（不加入購物車）
        </li>
      </ul>
      <p>CPU、主機板、顯示卡、電源供應器、機殼與散熱器各限一件。記憶體／儲存裝置合併數量，每項最多八件。</p>
      <button
        type="button"
        :disabled="isBusy"
        @click="applyImport('keep')"
      >
        合併並保留草稿的衝突分類
      </button>
      <button
        type="button"
        :disabled="isBusy"
        @click="applyImport('replace')"
      >
        合併並取代草稿的衝突分類
      </button>
      <button
        type="button"
        :disabled="isBusy"
        @click="applyImport('reset')"
      >
        以匯入內容取代整份草稿
      </button>
      <button
        type="button"
        @click="cancelImport"
      >
        取消匯入
      </button>
    </section>
    <p
      v-if="importError"
      role="alert"
    >
      {{ importError }}
    </p>
    <p v-if="cartSource">
      已記住購物車來源，加入購物車時只移轉所需數量。
      <button
        type="button"
        :disabled="isBusy"
        @click="removeCartImport"
      >
        移除購物車匯入的零件，重新挑選
      </button>
    </p>

    <section
      v-if="ownedParts.length"
      aria-label="自有零件"
    >
      <h2>自有零件</h2>
      <p>只用於相容性檢查，不加入購物車；本清單只購買新零件，不收組裝費，也不建立組裝工單。</p>
      <ul>
        <li
          v-for="(part, index) in ownedParts"
          :key="index"
        >
          {{ part.displayName }} × {{ part.quantity }}（自有）
          <button
            type="button"
            :disabled="isBusy"
            @click="ownedParts.splice(index, 1)"
          >
            移除自有零件
          </button>
        </li>
      </ul>
    </section>

    <BuildItemsEditor
      :items="items"
      :disabled="isBusy"
      :owned-category-codes="ownedParts.map(part => part.categoryCode ?? '')"
      @update:items="(next) => { items = next }"
    />

    <CompatibilityFindingsList
      v-if="compatibilityCheck.data.value"
      :overall="compatibilityCheck.data.value.overall"
      :results="compatibilityCheck.data.value.results"
    />
    <ErrorState
      v-else-if="compatibilityCheck.isError.value"
      title="相容性檢查失敗"
      :correlation-id="isApiError(compatibilityCheck.error.value) ? compatibilityCheck.error.value.correlationId : undefined"
      @retry="runCompatibilityCheck"
    />

    <ul
      v-if="items.length > 0 && !itemsValidation.isValid"
      class="new-build-page__items-errors"
    >
      <li
        v-for="error in itemsValidation.errors"
        :key="error"
      >
        {{ error }}
      </li>
    </ul>

    <div class="new-build-page__actions">
      <button
        type="button"
        :disabled="!canSave || isBusy"
        @click="save"
      >
        儲存為我的清單
      </button>
      <button
        type="button"
        :disabled="!canSave || isBusy || items.length === 0"
        @click="saveAndPurchase"
      >
        儲存並加入購物車
      </button>
    </div>
    <p
      v-if="purchaseError"
      role="alert"
    >
      {{ purchaseError }}
    </p>

    <ErrorState
      v-if="saveError"
      title="儲存失敗"
      :correlation-id="isApiError(saveError) ? saveError.correlationId : undefined"
      :description="isApiError(saveError) ? saveError.message : undefined"
      @retry="save"
    />
  </section>
</template>

<style scoped>
.new-build-page__hint {
  color: var(--color-text-muted);
  margin-block-end: 1.5rem;
}

.new-build-page__field {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  max-width: 24rem;
  margin-block-end: 1.5rem;
}

.new-build-page__field input {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  font: inherit;
}

.new-build-page__items-errors {
  margin: 1rem 0 0;
  padding-left: 1.25rem;
  color: var(--color-danger);
  font-size: 0.875rem;
}

.new-build-page__actions {
  margin-block: 1.5rem;
}
</style>
