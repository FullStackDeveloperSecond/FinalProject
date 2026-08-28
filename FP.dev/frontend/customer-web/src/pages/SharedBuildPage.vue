<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import CompatibilityFindingsList from '../features/builds/components/CompatibilityFindingsList.vue'
import { useAddBuildToCart, useCreateBuildList, useSharedBuild } from '../features/builds/useBuilds'
import { useSessionStore } from '../stores/session'

const props = defineProps<{ shareToken: string }>()
const router = useRouter()
const route = useRoute()
const sessionStore = useSessionStore()

const { data: sharedBuild, isPending, isError, error, refetch } = useSharedBuild(() => props.shareToken)

// 失效／已撤銷／已刪除的分享一律回 404，前端不揭露原因（商品、組裝與相容性.md）。
const isUnavailable = () => isApiError(error.value) && error.value.status === 404

const createBuildList = useCreateBuildList()
const addToCart = useAddBuildToCart()
const actionError = ref<unknown>(null)
// 組長 PR #35 review, item 3: canCopy／canAddToCart are backend-computed flags meant for real
// actions — BuildListsController's AddToCart only accepts the *owner's own* build list, and
// there is no share-scoped "copy"/"buy this" endpoint, so both actions here materialize the
// shared build as the viewer's own list first (via the existing, already-tested
// POST /build-lists), then — for add-to-cart — call the existing owner-only add-to-cart on that
// new list. Neither action needs a new backend endpoint. `/cart` isn't a route on this branch
// yet (feature/cart-frontend, PR #29, isn't merged into this branch's `dev` base), so both land
// on the new list's own detail page rather than the cart.
const isBusy = ref(false)

function requireLoginThenRetry(): void {
  void router.push({ path: '/login', query: { redirect: route.fullPath } })
}

async function copyToMyLists(): Promise<void> {
  if (!sharedBuild.value) {
    return
  }
  if (!sessionStore.isAuthenticated) {
    requireLoginThenRetry()
    return
  }

  actionError.value = null
  isBusy.value = true
  try {
    const copy = await createBuildList.mutateAsync({
      name: `${sharedBuild.value.name}（複製）`,
      items: sharedBuild.value.items.map((item) => ({ skuPublicId: item.skuPublicId, quantity: item.quantity })),
    })
    await router.push(`/builds/${copy.publicId}`)
  } catch (caught) {
    actionError.value = caught
  } finally {
    isBusy.value = false
  }
}

async function addSharedBuildToCart(): Promise<void> {
  if (!sharedBuild.value) {
    return
  }
  if (!sessionStore.isAuthenticated) {
    requireLoginThenRetry()
    return
  }

  actionError.value = null
  isBusy.value = true
  try {
    const copy = await createBuildList.mutateAsync({
      name: `${sharedBuild.value.name}（複製）`,
      items: sharedBuild.value.items.map((item) => ({ skuPublicId: item.skuPublicId, quantity: item.quantity })),
    })
    await addToCart.mutateAsync({
      publicId: copy.publicId,
      request: { quantity: 1, buildRowVersion: copy.rowVersion },
      idempotencyKey: crypto.randomUUID(),
    })
    await router.push(`/builds/${copy.publicId}`)
  } catch (caught) {
    actionError.value = caught
  } finally {
    isBusy.value = false
  }
}
</script>

<template>
  <section aria-labelledby="shared-build-page-title">
    <LoadingState
      v-if="isPending"
      label="分享清單載入中"
    />
    <EmptyState
      v-else-if="isError && isUnavailable()"
      title="此組裝清單目前無法使用"
      description="連結可能已撤銷、清單已刪除，或擁有者帳號已停用。"
    >
      <RouterLink to="/">
        回組裝清單首頁
      </RouterLink>
    </EmptyState>
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />

    <template v-else-if="sharedBuild">
      <h1 id="shared-build-page-title">
        {{ sharedBuild.name }}
      </h1>
      <p class="shared-build-page__hint">
        這是別人分享給你的組裝清單，僅供瀏覽。
      </p>

      <ul
        class="shared-build-page__items"
        aria-label="零件清單"
      >
        <li
          v-for="item in sharedBuild.items"
          :key="item.publicId"
        >
          {{ item.name }}（{{ item.skuCode }}）× {{ item.quantity }}
        </li>
      </ul>

      <CompatibilityFindingsList
        :overall="sharedBuild.compatibility.overall"
        :results="sharedBuild.compatibility.results"
      />

      <dl class="shared-build-page__totals">
        <dt>合計</dt>
        <dd>NT${{ sharedBuild.totals.grandTotal.toLocaleString('zh-Hant-TW') }}</dd>
      </dl>

      <div class="shared-build-page__actions">
        <button
          v-if="sharedBuild.canCopy"
          type="button"
          :disabled="isBusy"
          @click="copyToMyLists"
        >
          複製為我的清單
        </button>
        <button
          v-if="sharedBuild.canAddToCart"
          type="button"
          :disabled="isBusy"
          @click="addSharedBuildToCart"
        >
          整套加入購物車
        </button>
      </div>
      <p
        v-if="!sessionStore.isAuthenticated && (sharedBuild.canCopy || sharedBuild.canAddToCart)"
        class="shared-build-page__note"
      >
        需要先登入才能複製或加入購物車。
      </p>
      <ErrorState
        v-if="actionError"
        title="操作失敗"
        :correlation-id="isApiError(actionError) ? actionError.correlationId : undefined"
        :description="isApiError(actionError) ? actionError.message : undefined"
      />
    </template>
  </section>
</template>

<style scoped>
.shared-build-page__actions {
  display: flex;
  gap: 0.75rem;
  margin-block: 1rem;
}

.shared-build-page__hint {
  color: #4b5563;
  margin-block-end: 1.5rem;
}

.shared-build-page__items {
  list-style: none;
  margin: 0 0 1.5rem;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.shared-build-page__totals {
  display: grid;
  grid-template-columns: auto auto;
  gap: 0.25rem 1rem;
  margin-block: 1.5rem;
}

.shared-build-page__totals dd {
  margin: 0;
  font-weight: 700;
}

.shared-build-page__note {
  color: #4b5563;
  font-size: 0.875rem;
}
</style>
