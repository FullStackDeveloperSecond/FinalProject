<script setup lang="ts">
/** A-12 (M功能桌面UI與Route規格.md): Cursor 保留佇列、二次確認、理由及人工釋放。 */
import { EmptyState, ErrorState, LoadingState, PagePager } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { formatTaipeiDateTime } from '@doselect/web-shared/datetime'
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

const RESERVATION_STATUS_OPTIONS = [
  { value: 'Active', label: '生效中' },
  { value: 'Consumed', label: '已使用' },
  { value: 'Released', label: '已釋放' },
  { value: 'Expired', label: '已到期' },
]

function reservationStatusLabel(value: string): string {
  return RESERVATION_STATUS_OPTIONS.find(option => option.value === value)?.label ?? '其他狀態'
}

// 篩選即時套用並回第一頁；不沿用上一組條件的頁碼。
const draftFilters = reactive({ status: '' })
const appliedStatus = ref('')

const pageNumber = ref(1)
const listParams = computed(() => ({ status: appliedStatus.value || undefined, pageSize: 20, pageNumber: pageNumber.value }))
const {
  data,
  isPending,
  isError,
  error,
  refetch,
} = useInventoryReservationList(listParams)

const loadedItems = computed<InventoryReservationDto[]>(() => data.value?.items ?? [])

function search() {
  pageNumber.value = 1
  appliedStatus.value = draftFilters.status
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
  return formatTaipeiDateTime(value)
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
        @change="search"
      >
        <option value="">
          全部狀態
        </option>
        <option
          v-for="option in RESERVATION_STATUS_OPTIONS"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
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
              <td>{{ reservationStatusLabel(reservation.status) }}</td>
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
    </template>
    <PagePager
      v-if="data?.totalCount != null && !isPending && !isError"
      v-model:page="pageNumber"
      :page-size="20"
      :total-records="Number(data.totalCount)"
      aria-label="庫存保留分頁"
    />
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
