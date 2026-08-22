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
    skuPublicId: item.skuPublicId, quantity: item.quantity, name: item.name,
  }))
  hasConcurrencyConflict.value = false
  saveError.value = null
}

async function reloadAndDiscardEdits(): Promise<void> {
  await refetch()
  resetFromServer()
}

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
 * `BuildListDto` (the single-item GET) does not carry share state — only
 * `BuildListSummaryDto` (the list page) has `isShared`. So an existing share created in a
 * past session is not rediscoverable here on reload; this local ref only reflects a share
 * created/revoked during the current page visit. Flagged for 組長 alongside the other
 * cross-slice contract gaps found this session.
 */
const activeShare = ref<BuildShareDto | null>(null)
const shareError = ref<unknown>(null)

async function share(): Promise<void> {
  shareError.value = null
  try {
    activeShare.value = await createShare.mutateAsync()
  } catch (error) {
    shareError.value = error
  }
}

async function revoke(): Promise<void> {
  shareError.value = null
  try {
    await revokeShare.mutateAsync()
    activeShare.value = null
  } catch (error) {
    shareError.value = error
  }
}

const cartQuantity = ref(1)
const cartResultMessage = ref<string | null>(null)
const cartError = ref<unknown>(null)
const isBlocked = computed(() => buildList.value?.compatibility.overall === 'blocked')

async function addBuildToCart(): Promise<void> {
  if (!buildList.value) {
    return
  }
  cartResultMessage.value = null
  cartError.value = null
  try {
    await addToCart.mutateAsync({
      publicId: props.buildId,
      request: { quantity: cartQuantity.value, buildRowVersion: buildList.value.rowVersion },
      idempotencyKey: crypto.randomUUID(),
    })
    cartResultMessage.value = '已加入購物車。'
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
        <template v-if="activeShare">
          <p>分享連結：<code>{{ activeShare.url }}</code></p>
          <button
            type="button"
            :disabled="revokeShare.isPending.value"
            @click="revoke"
          >
            撤銷分享
          </button>
        </template>
        <button
          v-else
          type="button"
          :disabled="createShare.isPending.value"
          @click="share"
        >
          建立分享連結
        </button>
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
        <p v-if="isBlocked">
          此組裝清單目前不相容，請先解決相容性問題才能加入購物車。
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
            :disabled="isBlocked || addToCart.isPending.value"
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
