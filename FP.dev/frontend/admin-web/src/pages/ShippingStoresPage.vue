<script setup lang="ts">
/** A-17 (M功能桌面UI與Route規格.md): 100 筆虛構門市、搜尋、新增、修改與停用（UC-ADM-STORE-01）。 */
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref } from 'vue'
import {
  useConvenienceStoreList,
  useCreateConvenienceStore,
  useUpdateConvenienceStore,
} from '../features/shipping/useShipping'
import type { ConvenienceStoreDto } from '../features/shipping/types'
import { describeApiError } from '../features/shared/errorMessages'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'

/** UC-ADM-STORE-01 的品牌代碼。門市的 ProviderCode 是 CVS 品牌，不是物流 Profile 類別
 * （組長 PR #73 A1 裁定）——這頁只處理品牌，不要和包裹限制頁的 StorePickup／HomeDelivery 混用。 */
const STORE_PROVIDERS = [
  { code: '7-11', label: '7-ELEVEN' },
  { code: 'FamilyMart', label: '全家' },
]

/**
 * UC-ADM-STORE-01：「CatalogManager 只有檢視權限」。後端 Policy 才是真正的邊界
 * （ShippingManage 只給 OrderManager／SuperAdmin），這裡只是不要給一個按下去必定 403 的入口。
 */
const auth = useAdminAuthStore()
const canManage = computed(() => {
  const roles = auth.session?.user?.roles ?? []
  return roles.includes('OrderManager') || roles.includes('SuperAdmin')
})

// 篩選輸入只綁草稿，送出時才一起套用並把頁碼歸 1（組長 PR #37 round-2 review, item 3 的同一條
// 規則）——否則會出現「新條件配舊頁碼」的查詢。
const draftFilters = reactive({ providerCode: '', city: '', district: '', activeOnly: false })
const appliedFilters = reactive({ providerCode: '', city: '', district: '', activeOnly: false })
const pageNumber = ref(1)
const pageSize = 20

const listParams = computed(() => ({
  providerCode: appliedFilters.providerCode || undefined,
  city: appliedFilters.city || undefined,
  district: appliedFilters.district || undefined,
  isActive: appliedFilters.activeOnly ? true : undefined,
  pageNumber: pageNumber.value,
  pageSize,
}))
const { data: result, isPending, isError, error, refetch } = useConvenienceStoreList(listParams)
const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

function search() {
  appliedFilters.providerCode = draftFilters.providerCode
  appliedFilters.city = draftFilters.city
  appliedFilters.district = draftFilters.district
  appliedFilters.activeOnly = draftFilters.activeOnly
  pageNumber.value = 1
}

function goToPage(next: number) {
  pageNumber.value = next
}

const createMutation = useCreateConvenienceStore()
const updateMutation = useUpdateConvenienceStore()

const isCreating = ref(false)
const createForm = reactive({
  providerCode: STORE_PROVIDERS[0].code,
  storeCode: '',
  storeName: '',
  address: '',
  city: '',
  district: '',
})

const editingId = ref<string | null>(null)
const editForm = reactive({ storeName: '', address: '', city: '', district: '', isActive: true })

function startCreate() {
  isCreating.value = true
  editingId.value = null
  createForm.providerCode = STORE_PROVIDERS[0].code
  createForm.storeCode = ''
  createForm.storeName = ''
  createForm.address = ''
  createForm.city = ''
  createForm.district = ''
}

function startEdit(store: ConvenienceStoreDto) {
  isCreating.value = false
  editingId.value = store.publicId
  editForm.storeName = store.storeName
  editForm.address = store.address
  editForm.city = store.city
  editForm.district = store.district
  editForm.isActive = store.isActive
}

function cancel() {
  isCreating.value = false
  editingId.value = null
}

function submitCreate() {
  createMutation.mutate({ ...createForm }, { onSuccess: () => { isCreating.value = false } })
}

function submitEdit(store: ConvenienceStoreDto) {
  updateMutation.mutate({
    publicId: store.publicId,
    request: { ...editForm, rowVersion: store.rowVersion },
  }, { onSuccess: () => { editingId.value = null } })
}

/**
 * UC-ADM-STORE-01：「拒絕實體刪除，只允許停用」，而且既有訂單仍顯示成立時的門市快照。停用是
 * 不可從這頁復原的營運動作（顧客立刻選不到這家門市），所以要二次確認。
 */
function confirmDeactivate(store: ConvenienceStoreDto) {
  if (!globalThis.confirm(`確定要停用門市「${store.storeName}」（${store.storeCode}）嗎？停用後顧客結帳時將無法選取這家門市；已成立的訂單仍保留當時的門市快照。`)) {
    return
  }
  updateMutation.mutate({
    publicId: store.publicId,
    request: {
      storeName: store.storeName,
      address: store.address,
      city: store.city,
      district: store.district,
      isActive: false,
      rowVersion: store.rowVersion,
    },
  })
}

const mutationError = computed(() => {
  for (const mutation of [createMutation, updateMutation]) {
    if (isApiError(mutation.error.value)) {
      return describeApiError(mutation.error.value)
    }
  }
  return null
})

function providerLabel(code: string): string {
  return STORE_PROVIDERS.find((provider) => provider.code === code)?.label ?? code
}
</script>

<template>
  <section aria-labelledby="shipping-stores-title">
    <h1 id="shipping-stores-title">
      示範超商門市
    </h1>
    <p class="stores-note">
      本頁門市為專題展示用的虛構資料（UC-ADM-STORE-01），不對應真實門市。
    </p>

    <form
      class="stores-filters"
      aria-label="門市篩選"
      @submit.prevent="search"
    >
      <label>
        品牌
        <select
          v-model="draftFilters.providerCode"
          aria-label="品牌"
        >
          <option value="">
            全部品牌
          </option>
          <option
            v-for="provider in STORE_PROVIDERS"
            :key="provider.code"
            :value="provider.code"
          >
            {{ provider.label }}
          </option>
        </select>
      </label>
      <label>
        縣市
        <input
          v-model="draftFilters.city"
          aria-label="縣市"
          maxlength="60"
        >
      </label>
      <label>
        行政區
        <input
          v-model="draftFilters.district"
          aria-label="行政區"
          maxlength="60"
        >
      </label>
      <label>
        <input
          v-model="draftFilters.activeOnly"
          type="checkbox"
          aria-label="只顯示啟用中"
        >
        只顯示啟用中
      </label>
      <button type="submit">
        搜尋
      </button>
      <button
        v-if="canManage"
        type="button"
        @click="startCreate"
      >
        新增門市
      </button>
    </form>

    <p
      v-if="mutationError"
      class="stores-error"
      role="alert"
    >
      {{ mutationError }}
    </p>

    <form
      v-if="isCreating"
      class="stores-form"
      aria-label="新增門市"
      @submit.prevent="submitCreate"
    >
      <label>
        品牌
        <select
          v-model="createForm.providerCode"
          aria-label="新增品牌"
        >
          <option
            v-for="provider in STORE_PROVIDERS"
            :key="provider.code"
            :value="provider.code"
          >
            {{ provider.label }}
          </option>
        </select>
      </label>
      <label>
        門市代碼
        <input
          v-model="createForm.storeCode"
          required
          maxlength="64"
          aria-label="門市代碼"
        >
      </label>
      <label>
        門市名稱
        <input
          v-model="createForm.storeName"
          required
          maxlength="160"
          aria-label="門市名稱"
        >
      </label>
      <label>
        縣市
        <input
          v-model="createForm.city"
          required
          maxlength="60"
          aria-label="新增縣市"
        >
      </label>
      <label>
        行政區
        <input
          v-model="createForm.district"
          required
          maxlength="60"
          aria-label="新增行政區"
        >
      </label>
      <label>
        地址
        <input
          v-model="createForm.address"
          required
          maxlength="500"
          aria-label="地址"
        >
      </label>
      <div class="stores-form__actions">
        <button
          type="submit"
          :disabled="createMutation.isPending.value"
        >
          建立
        </button>
        <button
          type="button"
          @click="cancel"
        >
          取消
        </button>
      </div>
    </form>

    <LoadingState
      v-if="isPending && !result"
      label="門市載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />
    <EmptyState
      v-else-if="(result?.items.length ?? 0) === 0"
      title="沒有符合條件的門市"
    />
    <template v-else>
      <table class="stores-table">
        <thead>
          <tr>
            <th>品牌</th>
            <th>門市代碼</th>
            <th>名稱</th>
            <th>縣市</th>
            <th>行政區</th>
            <th>地址</th>
            <th>狀態</th>
            <th v-if="canManage" />
          </tr>
        </thead>
        <tbody>
          <template
            v-for="store in result!.items"
            :key="store.publicId"
          >
            <tr>
              <td>{{ providerLabel(store.providerCode) }}</td>
              <td>{{ store.storeCode }}</td>
              <td>
                {{ store.storeName }}
                <span
                  v-if="store.isDemoData"
                  class="stores-badge"
                >展示資料</span>
              </td>
              <td>{{ store.city }}</td>
              <td>{{ store.district }}</td>
              <td>{{ store.address }}</td>
              <td>{{ store.isActive ? '啟用' : '停用' }}</td>
              <td v-if="canManage">
                <button
                  type="button"
                  @click="startEdit(store)"
                >
                  編輯
                </button>
                <button
                  v-if="store.isActive"
                  type="button"
                  :disabled="updateMutation.isPending.value"
                  @click="confirmDeactivate(store)"
                >
                  停用
                </button>
              </td>
            </tr>
            <tr v-if="editingId === store.publicId">
              <td :colspan="canManage ? 8 : 7">
                <form
                  class="stores-form"
                  aria-label="編輯門市"
                  @submit.prevent="submitEdit(store)"
                >
                  <p class="stores-form__note">
                    品牌與門市代碼建立後不可修改；門市不提供刪除，請以停用處理（UC-ADM-STORE-01）。
                  </p>
                  <label>
                    門市名稱
                    <input
                      v-model="editForm.storeName"
                      required
                      maxlength="160"
                      aria-label="編輯門市名稱"
                    >
                  </label>
                  <label>
                    縣市
                    <input
                      v-model="editForm.city"
                      required
                      maxlength="60"
                      aria-label="編輯縣市"
                    >
                  </label>
                  <label>
                    行政區
                    <input
                      v-model="editForm.district"
                      required
                      maxlength="60"
                      aria-label="編輯行政區"
                    >
                  </label>
                  <label>
                    地址
                    <input
                      v-model="editForm.address"
                      required
                      maxlength="500"
                      aria-label="編輯地址"
                    >
                  </label>
                  <label>
                    <input
                      v-model="editForm.isActive"
                      type="checkbox"
                      aria-label="編輯啟用"
                    >
                    啟用
                  </label>
                  <div class="stores-form__actions">
                    <button
                      type="submit"
                      :disabled="updateMutation.isPending.value"
                    >
                      儲存
                    </button>
                    <button
                      type="button"
                      @click="cancel"
                    >
                      取消
                    </button>
                  </div>
                </form>
              </td>
            </tr>
          </template>
        </tbody>
      </table>

      <div
        v-if="totalPages > 1"
        class="stores-pagination"
      >
        <button
          type="button"
          :disabled="pageNumber <= 1"
          @click="goToPage(pageNumber - 1)"
        >
          上一頁
        </button>
        <span>{{ pageNumber }} / {{ totalPages }}</span>
        <button
          type="button"
          :disabled="pageNumber >= totalPages"
          @click="goToPage(pageNumber + 1)"
        >
          下一頁
        </button>
      </div>
    </template>
  </section>
</template>

<style scoped>
.stores-note {
  color: #6b7280;
  font-size: 0.875rem;
  margin-block-end: 1rem;
}

.stores-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.75rem;
  margin-block-end: 1.5rem;
}

.stores-filters label,
.stores-form label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
}

.stores-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
  margin-block-end: 1.5rem;
}

.stores-form__note {
  flex-basis: 100%;
  margin: 0;
  color: #6b7280;
  font-size: 0.8125rem;
}

.stores-form__actions {
  display: flex;
  gap: 0.5rem;
}

.stores-table {
  width: 100%;
  border-collapse: collapse;
}

.stores-table th,
.stores-table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}

.stores-badge {
  margin-inline-start: 0.375rem;
  padding: 0.125rem 0.375rem;
  border-radius: 0.25rem;
  background: #e0f2fe;
  color: #075985;
  font-size: 0.75rem;
}

.stores-error {
  color: #b91c1c;
  margin-block-end: 1rem;
}

.stores-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  margin-block-start: 1.5rem;
}
</style>
