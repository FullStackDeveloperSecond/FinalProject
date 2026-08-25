<script setup lang="ts">
/** A-11 (M功能桌面UI與Route規格.md): SKU 庫存餘額、低庫存與異動明細。 */
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive } from 'vue'
import { useInventoryBalanceList, useInventoryMovementList } from '../features/inventory/useInventory'
import { endOfLocalDayExclusiveBoundary, startOfLocalDay } from '../features/inventory/dateRange'

const MOVEMENT_TYPE_OPTIONS = [
  'StockIn', 'Reserve', 'Release', 'Ship', 'ReturnToStock',
  'ManualIncrease', 'ManualDecrease', 'Damage', 'Adjustment',
]

const balanceFilters = reactive({ q: '', stockState: '', pageNumber: 1 })
const balancePageSize = 20
const balanceParams = computed(() => ({
  q: balanceFilters.q || undefined,
  stockState: balanceFilters.stockState || undefined,
  pageNumber: balanceFilters.pageNumber,
  pageSize: balancePageSize,
}))
const { data: balanceResult, isPending: isBalancePending, isError: isBalanceError, error: balanceError, refetch: refetchBalances } =
  useInventoryBalanceList(balanceParams)
const balanceTotalPages = computed(() => Number(balanceResult.value?.totalPages ?? 0))

function searchBalances() {
  balanceFilters.pageNumber = 1
}

function goToBalancePage(nextPage: number) {
  balanceFilters.pageNumber = nextPage
}

function stockRowClass(available: number, lowStockThreshold: number): string {
  if (available <= 0) {
    return 'inventory-table__row--out-of-stock'
  }
  if (available <= lowStockThreshold) {
    return 'inventory-table__row--low-stock'
  }
  return ''
}

const movementFilters = reactive({ movementTypes: [] as string[], from: '', to: '', pageNumber: 1 })
const movementPageSize = 20
const movementParams = computed(() => ({
  movementTypes: movementFilters.movementTypes.length > 0 ? movementFilters.movementTypes : undefined,
  // [from, to) against the browser's local calendar day, not UTC midnight — see dateRange.ts.
  from: movementFilters.from ? startOfLocalDay(movementFilters.from).toISOString() : undefined,
  to: movementFilters.to ? endOfLocalDayExclusiveBoundary(movementFilters.to).toISOString() : undefined,
  pageNumber: movementFilters.pageNumber,
  pageSize: movementPageSize,
}))
const { data: movementResult, isPending: isMovementPending, isError: isMovementError, error: movementError, refetch: refetchMovements } =
  useInventoryMovementList(movementParams)
const movementTotalPages = computed(() => Number(movementResult.value?.totalPages ?? 0))

function searchMovements() {
  movementFilters.pageNumber = 1
}

function goToMovementPage(nextPage: number) {
  movementFilters.pageNumber = nextPage
}

function formatDateTime(value: string): string {
  return new Date(value).toLocaleString('zh-Hant-TW')
}
</script>

<template>
  <section aria-labelledby="inventory-page-title">
    <h1 id="inventory-page-title">
      庫存管理
    </h1>

    <section aria-labelledby="inventory-balances-title">
      <h2 id="inventory-balances-title">
        庫存餘額
      </h2>

      <form
        class="inventory-filters"
        aria-label="庫存餘額篩選"
        @submit.prevent="searchBalances"
      >
        <input
          v-model="balanceFilters.q"
          type="search"
          placeholder="搜尋 SKU 代碼或名稱"
          aria-label="關鍵字"
        >
        <select
          v-model="balanceFilters.stockState"
          aria-label="庫存狀態"
        >
          <option value="">
            全部狀態
          </option>
          <option value="in_stock">
            現貨
          </option>
          <option value="low_stock">
            低庫存
          </option>
          <option value="out_of_stock">
            缺貨
          </option>
        </select>
        <button type="submit">
          搜尋
        </button>
      </form>

      <LoadingState
        v-if="isBalancePending"
        label="庫存資料載入中"
      />
      <ErrorState
        v-else-if="isBalanceError"
        :correlation-id="isApiError(balanceError) ? balanceError.correlationId : undefined"
        @retry="refetchBalances"
      />
      <EmptyState
        v-else-if="balanceResult && balanceResult.items.length === 0"
        title="沒有符合條件的庫存資料"
      />
      <template v-else-if="balanceResult">
        <table class="inventory-table">
          <thead>
            <tr>
              <th>SKU 代碼</th>
              <th>名稱</th>
              <th>在庫</th>
              <th>已保留</th>
              <th>可售</th>
              <th>低庫存門檻</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="balance in balanceResult.items"
              :key="balance.skuPublicId"
              :class="stockRowClass(balance.available, balance.lowStockThreshold)"
            >
              <td>{{ balance.skuCode }}</td>
              <td>{{ balance.skuNameZhTw }}</td>
              <td>{{ balance.onHand }}</td>
              <td>{{ balance.reserved }}</td>
              <td>{{ balance.available }}</td>
              <td>{{ balance.lowStockThreshold }}</td>
            </tr>
          </tbody>
        </table>
        <nav
          v-if="balanceTotalPages > 1"
          class="inventory-pagination"
          aria-label="庫存餘額分頁"
        >
          <button
            type="button"
            :disabled="balanceFilters.pageNumber <= 1"
            @click="goToBalancePage(balanceFilters.pageNumber - 1)"
          >
            上一頁
          </button>
          <span>第 {{ balanceFilters.pageNumber }} / {{ balanceTotalPages }} 頁</span>
          <button
            type="button"
            :disabled="balanceFilters.pageNumber >= balanceTotalPages"
            @click="goToBalancePage(balanceFilters.pageNumber + 1)"
          >
            下一頁
          </button>
        </nav>
      </template>
    </section>

    <section aria-labelledby="inventory-movements-title">
      <h2 id="inventory-movements-title">
        異動明細
      </h2>

      <form
        class="inventory-filters"
        aria-label="異動明細篩選"
        @submit.prevent="searchMovements"
      >
        <fieldset class="inventory-filters__types">
          <legend>異動類型</legend>
          <label
            v-for="type in MOVEMENT_TYPE_OPTIONS"
            :key="type"
          >
            <input
              v-model="movementFilters.movementTypes"
              type="checkbox"
              :value="type"
            >
            {{ type }}
          </label>
        </fieldset>
        <label>
          起始
          <input
            v-model="movementFilters.from"
            type="date"
            aria-label="起始日期"
          >
        </label>
        <label>
          結束
          <input
            v-model="movementFilters.to"
            type="date"
            aria-label="結束日期"
          >
        </label>
        <button type="submit">
          搜尋
        </button>
      </form>

      <LoadingState
        v-if="isMovementPending"
        label="異動明細載入中"
      />
      <ErrorState
        v-else-if="isMovementError"
        :correlation-id="isApiError(movementError) ? movementError.correlationId : undefined"
        @retry="refetchMovements"
      />
      <EmptyState
        v-else-if="movementResult && movementResult.items.length === 0"
        title="沒有符合條件的異動紀錄"
      />
      <template v-else-if="movementResult">
        <table class="inventory-table">
          <thead>
            <tr>
              <th>時間</th>
              <th>SKU</th>
              <th>類型</th>
              <th>在庫變化</th>
              <th>保留變化</th>
              <th>原因</th>
              <th>操作人</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="movement in movementResult.items"
              :key="movement.publicId"
            >
              <td>{{ formatDateTime(movement.occurredAtUtc) }}</td>
              <td>{{ movement.sku.skuCode }}</td>
              <td>{{ movement.movementType }}</td>
              <td>{{ movement.onHandDelta >= 0 ? '+' : '' }}{{ movement.onHandDelta }}</td>
              <td>{{ movement.reservedDelta >= 0 ? '+' : '' }}{{ movement.reservedDelta }}</td>
              <td>{{ movement.reasonCode }}</td>
              <td>{{ movement.actor?.email ?? '系統' }}</td>
            </tr>
          </tbody>
        </table>
        <nav
          v-if="movementTotalPages > 1"
          class="inventory-pagination"
          aria-label="異動明細分頁"
        >
          <button
            type="button"
            :disabled="movementFilters.pageNumber <= 1"
            @click="goToMovementPage(movementFilters.pageNumber - 1)"
          >
            上一頁
          </button>
          <span>第 {{ movementFilters.pageNumber }} / {{ movementTotalPages }} 頁</span>
          <button
            type="button"
            :disabled="movementFilters.pageNumber >= movementTotalPages"
            @click="goToMovementPage(movementFilters.pageNumber + 1)"
          >
            下一頁
          </button>
        </nav>
      </template>
    </section>
  </section>
</template>

<style scoped>
.inventory-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
  margin-block-end: 1rem;
}

.inventory-filters input[type='search'],
.inventory-filters input[type='date'],
.inventory-filters select {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.inventory-filters__types {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
  padding: 0.5rem 0.75rem;
  width: 100%;
}

.inventory-filters__types label {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  font-size: 0.8125rem;
}

.inventory-table {
  width: 100%;
  border-collapse: collapse;
  margin-block-end: 2rem;
}

.inventory-table th,
.inventory-table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}

.inventory-table__row--low-stock {
  background: #fef3c7;
}

.inventory-table__row--out-of-stock {
  background: #fee2e2;
}

.inventory-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  margin-block-end: 2rem;
}
</style>
