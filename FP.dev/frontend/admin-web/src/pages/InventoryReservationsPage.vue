<script setup lang="ts">
/** A-12 (M功能桌面UI與Route規格.md): Cursor 保留佇列、二次確認、理由及人工釋放。 */
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref } from 'vue'
import { useInventoryReservationList, useReleaseReservation } from '../features/inventory/useInventory'
import type { InventoryReservationDto } from '../features/inventory/types'
import { describeApiError } from '../features/shared/errorMessages'

// 組長 PR #37 review, item 4: released reservations no longer let admins free-type a reason
// code — must match the backend's controlled whitelist (InventoryReleaseReasonCodes.All on
// feature/inventory-reservation-api). Mirrors InventoryPage.vue's own inline
// MOVEMENT_TYPE_OPTIONS pattern rather than a shared contracts file, since there is still no
// generated OpenAPI client for this module (see types.ts's doc comment).
const RELEASE_REASON_CODE_OPTIONS = [
  { value: 'customer_cancelled', label: '顧客取消' },
  { value: 'duplicate_order', label: '重複訂單' },
  { value: 'risk_rejected', label: '風控拒絕' },
  { value: 'inventory_correction', label: '庫存校正' },
  { value: 'other', label: '其他' },
]

// 組長 PR #37 round-2 review, item 3: the <select> binds to a draft; only 搜尋 copies it into the
// applied status. Binding the query key straight to the form meant changing status on page two
// fired "new status + old cursor", which the backend rejects by contract (a cursor is bound to
// the filters that issued it). The applied status is part of the query key, so submitting swaps
// the key and the infinite query restarts from an empty first page — the old "clear the cursor
// on search" is now implicit and can't be forgotten.
const draftFilters = reactive({ status: '' })
const appliedStatus = ref('')

// 組長 PR #37 round-3 review (P2): the page no longer accumulates cursor pages in its own ref —
// that made TanStack Query observe only the *current* cursor, so refocus/invalidate refreshed the
// latest page and left earlier pages stale. useInfiniteQuery keeps every loaded page under one
// query key and refetches them ALL on invalidate (see useInventory.ts).
const listParams = computed(() => ({ status: appliedStatus.value || undefined, pageSize: 20 }))
const {
  data,
  isPending,
  isError,
  error,
  refetch,
  fetchNextPage,
  hasNextPage,
  isFetchingNextPage,
} = useInventoryReservationList(listParams)

// Flatten the pages for rendering, still upserting by publicId (round-2 review, item 2): a full
// refetch keeps pages internally consistent, but between a fetchNextPage and the pages fetched
// before it a row can move across page boundaries — later pages are the fresher fetch, so the
// last occurrence wins, and Map's insertion-order preservation keeps the list from reordering.
const loadedItems = computed<InventoryReservationDto[]>(() => {
  const byId = new Map<string, InventoryReservationDto>()
  for (const pageData of data.value?.pages ?? []) {
    for (const item of pageData.items) {
      byId.set(item.publicId, item)
    }
  }
  return [...byId.values()]
})

function search() {
  appliedStatus.value = draftFilters.status
}

function loadMore() {
  fetchNextPage()
}

const releaseMutation = useReleaseReservation()
const releasingId = ref<string | null>(null)
const releaseForm = reactive({ reasonCode: '', note: '' })

function startRelease(reservation: InventoryReservationDto) {
  releasingId.value = reservation.publicId
  releaseForm.reasonCode = ''
  releaseForm.note = ''
}

function cancelRelease() {
  releasingId.value = null
}

function confirmRelease(reservation: InventoryReservationDto) {
  if (!globalThis.confirm(`確定要人工釋放這筆保留（訂單 ${reservation.order.orderNumber}、SKU ${reservation.sku.skuCode}）嗎？此動作無法復原。`)) {
    return
  }
  releaseMutation.mutate({
    publicId: reservation.publicId,
    request: { reasonCode: releaseForm.reasonCode, note: releaseForm.note, rowVersion: reservation.rowVersion },
  }, {
    onSuccess: () => {
      // useReleaseReservation invalidates the reservations query and the infinite query refetches
      // every loaded page — the released row's fresh Status/RowVersion arrives without resetting
      // the admin's place in the queue.
      releasingId.value = null
    },
  })
}

function formatDateTime(value: string | null): string {
  return value ? new Date(value).toLocaleString('zh-Hant-TW') : '—'
}
</script>

<template>
  <section aria-labelledby="inventory-reservations-title">
    <h1 id="inventory-reservations-title">
      庫存保留佇列
    </h1>

    <form
      class="reservations-filters"
      aria-label="保留篩選"
      @submit.prevent="search"
    >
      <select
        v-model="draftFilters.status"
        aria-label="狀態"
      >
        <option value="">
          全部狀態
        </option>
        <option value="Active">
          Active
        </option>
        <option value="Consumed">
          Consumed
        </option>
        <option value="Released">
          Released
        </option>
        <option value="Expired">
          Expired
        </option>
      </select>
      <button type="submit">
        搜尋
      </button>
    </form>

    <LoadingState
      v-if="isPending && loadedItems.length === 0"
      label="保留佇列載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />
    <EmptyState
      v-else-if="loadedItems.length === 0"
      title="沒有符合條件的保留紀錄"
    />
    <template v-else>
      <table class="reservations-table">
        <thead>
          <tr>
            <th>訂單</th>
            <th>SKU</th>
            <th>數量</th>
            <th>狀態</th>
            <th>到期時間</th>
            <th>建立時間</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <template
            v-for="reservation in loadedItems"
            :key="reservation.publicId"
          >
            <tr>
              <td>{{ reservation.order.orderNumber }}</td>
              <td>{{ reservation.sku.skuCode }}</td>
              <td>{{ reservation.quantity }}</td>
              <td>{{ reservation.status }}</td>
              <td>{{ formatDateTime(reservation.expiresAtUtc) }}</td>
              <td>{{ formatDateTime(reservation.createdAtUtc) }}</td>
              <td>
                <button
                  v-if="reservation.availableActions.includes('release') && releasingId !== reservation.publicId"
                  type="button"
                  @click="startRelease(reservation)"
                >
                  釋放
                </button>
              </td>
            </tr>
            <tr
              v-if="releasingId === reservation.publicId"
              class="reservations-table__release-row"
            >
              <td colspan="7">
                <div class="release-form">
                  <label>
                    原因代碼
                    <select
                      v-model="releaseForm.reasonCode"
                      required
                      aria-label="原因代碼"
                    >
                      <option
                        value=""
                        disabled
                      >
                        請選擇原因
                      </option>
                      <option
                        v-for="option in RELEASE_REASON_CODE_OPTIONS"
                        :key="option.value"
                        :value="option.value"
                      >
                        {{ option.label }}
                      </option>
                    </select>
                  </label>
                  <label>
                    備註
                    <input
                      v-model="releaseForm.note"
                      maxlength="500"
                      required
                      aria-label="備註"
                    >
                  </label>
                  <button
                    type="button"
                    :disabled="releaseMutation.isPending.value || !releaseForm.reasonCode || !releaseForm.note"
                    @click="confirmRelease(reservation)"
                  >
                    確認釋放
                  </button>
                  <button
                    type="button"
                    @click="cancelRelease"
                  >
                    取消
                  </button>
                </div>
                <p
                  v-if="isApiError(releaseMutation.error.value)"
                  class="release-form__error"
                >
                  {{ describeApiError(releaseMutation.error.value) }}
                </p>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
      <div
        v-if="hasNextPage"
        class="reservations-load-more"
      >
        <button
          type="button"
          :disabled="isFetchingNextPage"
          @click="loadMore"
        >
          載入更多
        </button>
      </div>
    </template>
  </section>
</template>

<style scoped>
.reservations-filters {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-block-end: 1.5rem;
}

.reservations-filters select {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.reservations-table {
  width: 100%;
  border-collapse: collapse;
}

.reservations-table th,
.reservations-table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}

.reservations-table__release-row {
  background: #f9fafb;
}

.release-form {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
}

.release-form label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
}

.release-form input {
  min-height: 2.5rem;
  padding: 0.375rem 0.625rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.release-form__error {
  color: #b91c1c;
  font-size: 0.875rem;
  margin: 0.5rem 0 0;
}

.reservations-load-more {
  display: flex;
  justify-content: center;
  margin-block-start: 1.5rem;
}
</style>
