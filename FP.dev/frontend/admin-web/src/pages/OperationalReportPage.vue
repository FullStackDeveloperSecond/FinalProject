<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'
import { cellsFor, formatMetric, headersFor, metricLabel } from '../features/operationalReports/presentation'
import {
  isOperationalReportKey,
  operationalReportDefinitions,
  reportDefinition,
  type OperationalReportFilters,
  type OperationalReportKey,
} from '../features/operationalReports/types'
import { useOperationalReport } from '../features/operationalReports/useOperationalReport'

const route = useRoute()
const auth = useAdminAuthStore()
const report = useOperationalReport()

function taipeiCalendarDate(date: Date): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Taipei',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(date)
  const value = (type: Intl.DateTimeFormatPartTypes) => parts.find((part) => part.type === type)?.value ?? ''
  return `${value('year')}-${value('month')}-${value('day')}`
}

function addCalendarDays(value: string, days: number): string {
  const date = new Date(`${value}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

function initialFilters(): OperationalReportFilters {
  const toDate = taipeiCalendarDate(new Date())
  return {
    fromDate: addCalendarDays(toDate, -30),
    toDate,
    timeZone: 'Asia/Taipei',
    categoryCode: '',
    brandCode: '',
    orderStatuses: [],
    granularity: 'day',
    pageSize: 20,
  }
}

const draft = reactive({
  ...initialFilters(),
  orderStatusesText: '',
})
const appliedFilters = ref<OperationalReportFilters>(initialFilters())
const validationMessage = ref('')

const reportKey = computed<OperationalReportKey>(() => {
  const value = route.params.reportKey
  return isOperationalReportKey(value) ? value : 'sales-overview'
})
const definition = computed(() => reportDefinition(reportKey.value))
const canViewFinancialReports = computed(() => {
  const roles = auth.currentUser?.roles ?? []
  return roles.includes('FinanceManager') || roles.includes('SuperAdmin')
})
const visibleReports = computed(() => operationalReportDefinitions.filter(
  (candidate) => !candidate.financial || canViewFinancialReports.value,
))
const chartPoints = computed(() => report.data.value?.series.map((point) => {
  const metric = point.metrics[0]
  return {
    bucket: point.bucket,
    label: metric ? metricLabel(metric.metricKey) : '指標',
    value: metric?.value === null || metric?.value === undefined ? 0 : Number(metric.value),
    unit: metric?.unit ?? '',
  }
}) ?? [])
const chartMaximum = computed(() => Math.max(0, ...chartPoints.value.map((point) => Math.abs(point.value))))

function barWidth(value: number): string {
  return chartMaximum.value === 0 ? '0%' : `${Math.max(2, Math.abs(value) / chartMaximum.value * 100)}%`
}

function normalizedFilters(): OperationalReportFilters {
  return {
    fromDate: draft.fromDate,
    toDate: draft.toDate,
    timeZone: 'Asia/Taipei',
    categoryCode: draft.categoryCode.trim(),
    brandCode: draft.brandCode.trim(),
    orderStatuses: draft.orderStatusesText.split(',').map((value) => value.trim()).filter(Boolean),
    granularity: draft.granularity,
    pageSize: draft.pageSize,
  }
}

async function applyFilters(): Promise<void> {
  validationMessage.value = ''
  if (!draft.fromDate || !draft.toDate || draft.fromDate >= draft.toDate) {
    validationMessage.value = '結束日期（不含）必須晚於開始日期。'
    return
  }
  appliedFilters.value = normalizedFilters()
  await report.load(reportKey.value, appliedFilters.value)
}

watch(reportKey, async () => {
  validationMessage.value = ''
  await report.load(reportKey.value, appliedFilters.value)
}, { immediate: true })
</script>

<template>
  <section aria-labelledby="operational-report-title">
    <div class="report-heading">
      <div>
        <span class="demo-badge">DEMO DATA</span>
        <h1 id="operational-report-title">
          {{ definition.title }}
        </h1>
        <p class="view-lede">
          七個營運報表共用查詢殼層；所有日期以 Asia/Taipei 的左閉右開區間計算。
        </p>
      </div>
      <div class="report-export-actions">
        <button
          type="button"
          :disabled="report.isExporting.value || report.isLoading.value"
          @click="report.download(reportKey, appliedFilters, 'csv')"
        >
          {{ report.isExporting.value ? '匯出中…' : '匯出 CSV' }}
        </button>
        <button
          type="button"
          :disabled="report.isExporting.value || report.isLoading.value"
          @click="report.download(reportKey, appliedFilters, 'xlsx')"
        >
          {{ report.isExporting.value ? '匯出中…' : '匯出 XLSX' }}
        </button>
      </div>
    </div>

    <nav
      class="report-tabs"
      aria-label="營運報表"
    >
      <RouterLink
        v-for="item in visibleReports"
        :key="item.key"
        :to="`/reports/${item.key}`"
      >
        {{ item.title }}
      </RouterLink>
    </nav>

    <form
      class="card report-filters"
      aria-label="報表篩選"
      @submit.prevent="applyFilters"
    >
      <label>
        <span>開始日期</span>
        <input
          v-model="draft.fromDate"
          type="date"
          required
        >
      </label>
      <label>
        <span>結束日期（不含）</span>
        <input
          v-model="draft.toDate"
          type="date"
          required
        >
      </label>
      <label>
        <span>分類代碼</span>
        <input
          v-model="draft.categoryCode"
          type="text"
          placeholder="全部分類"
        >
      </label>
      <label>
        <span>品牌代碼</span>
        <input
          v-model="draft.brandCode"
          type="text"
          placeholder="全部品牌"
        >
      </label>
      <label>
        <span>訂單狀態（逗號分隔）</span>
        <input
          v-model="draft.orderStatusesText"
          type="text"
          placeholder="Completed,Cancelled"
        >
      </label>
      <label>
        <span>粒度</span>
        <select v-model="draft.granularity">
          <option value="day">日</option>
          <option value="week">週</option>
          <option value="month">月</option>
        </select>
      </label>
      <button type="submit">
        套用篩選
      </button>
      <p
        v-if="validationMessage"
        class="form-error report-filters__error"
        role="alert"
      >
        {{ validationMessage }}
      </p>
    </form>

    <p
      v-if="report.actionError.value"
      class="report-action-error"
      role="alert"
    >
      {{ isApiError(report.actionError.value) ? report.actionError.value.message : '操作失敗，請稍後再試。' }}
    </p>

    <LoadingState v-if="report.isLoading.value" />
    <ErrorState
      v-else-if="report.error.value"
      title="無法載入營運報表"
      :description="isApiError(report.error.value) ? report.error.value.message : '請稍後再試。'"
      :correlation-id="isApiError(report.error.value) ? report.error.value.correlationId : undefined"
      :trace-id="isApiError(report.error.value) ? report.error.value.traceId : undefined"
      @retry="report.load(reportKey, appliedFilters)"
    />
    <template v-else-if="report.data.value">
      <div class="report-metadata">
        <span>時間基準：{{ report.data.value.timeBasis }}</span>
        <span>資料截至：{{ new Date(report.data.value.asOfUtc).toLocaleString('zh-TW') }}</span>
      </div>

      <div class="report-summary">
        <article
          v-for="metric in report.data.value.summary"
          :key="metric.metricKey"
          class="card report-metric"
        >
          <span>{{ metricLabel(metric.metricKey) }}</span>
          <strong>{{ formatMetric(metric.value, metric.unit) }}</strong>
          <small>{{ metric.unit }}</small>
        </article>
      </div>

      <section
        v-if="chartPoints.length > 0"
        class="card report-chart"
        aria-labelledby="report-chart-title"
      >
        <h2 id="report-chart-title">
          趨勢
        </h2>
        <div
          v-for="point in chartPoints"
          :key="point.bucket"
          class="report-chart__row"
        >
          <span>{{ point.bucket }}</span>
          <div class="report-chart__track">
            <span :style="{ width: barWidth(point.value) }" />
          </div>
          <strong>{{ formatMetric(point.value, point.unit) }}</strong>
        </div>
      </section>

      <EmptyState
        v-if="report.data.value.rows.items.length === 0"
        title="目前沒有符合條件的資料"
        description="請保留或調整篩選條件後再查詢。"
      />
      <div
        v-else
        class="table-scroll report-table"
      >
        <table>
          <thead>
            <tr>
              <th
                v-for="header in headersFor(reportKey)"
                :key="header"
                scope="col"
              >
                {{ header }}
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(row, rowIndex) in report.data.value.rows.items"
              :key="rowIndex"
            >
              <td
                v-for="(cell, cellIndex) in cellsFor(reportKey, row)"
                :key="cellIndex"
              >
                {{ cell }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <button
        v-if="report.data.value.rows.hasMore"
        type="button"
        :disabled="report.isLoadingMore.value"
        @click="report.loadMore(reportKey, appliedFilters)"
      >
        {{ report.isLoadingMore.value ? '載入中…' : '載入更多' }}
      </button>
    </template>
  </section>
</template>

<style scoped>
.report-heading { display: flex; align-items: start; justify-content: space-between; gap: 1rem; }
.report-heading h1 { margin-top: .5rem; }
.report-tabs { display: flex; flex-wrap: wrap; gap: .5rem; margin: 0 0 1rem; }
.report-tabs a { padding: .45rem .75rem; border: 1px solid var(--color-border); border-radius: var(--radius-sm); color: var(--color-text); text-decoration: none; }
.report-tabs a.router-link-active { background: var(--color-primary); border-color: var(--color-primary); color: var(--color-on-primary); }
.report-filters { display: grid; grid-template-columns: repeat(3, minmax(10rem, 1fr)); gap: .75rem; margin-bottom: 1.25rem; }
.report-filters label { display: grid; gap: .25rem; font-size: .85rem; font-weight: 700; }
.report-filters button { align-self: end; }
.report-filters__error { grid-column: 1 / -1; }
.report-action-error { padding: .75rem 1rem; background: var(--color-danger-bg); color: var(--color-danger); border-radius: var(--radius-sm); }
.report-metadata { display: flex; flex-wrap: wrap; gap: .5rem 1.5rem; margin-bottom: 1rem; color: var(--color-text-muted); font-size: .85rem; }
.report-summary { display: grid; grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr)); gap: .75rem; margin-bottom: 1rem; }
.report-metric { display: grid; gap: .25rem; }
/* 參考圖 06：重點數字用亮藍，讓趨勢與金額成為視覺焦點 */
.report-metric strong { font-size: 1.35rem; color: var(--color-primary); }
.report-metric small { color: var(--color-text-muted); }
.report-chart { margin-bottom: 1rem; }
.report-chart h2 { margin-top: 0; }
.report-chart__row { display: grid; grid-template-columns: 7rem minmax(8rem, 1fr) 8rem; gap: .75rem; align-items: center; margin: .5rem 0; }
/* 單一指標的時間序列＝單一系列，依參考圖 06「熱銷商品 TOP 5」用同一個藍。
   多系列圖表請改用 --chart-1 … --chart-6，並在圖例同時提供文字標籤與數值。 */
.report-chart__track { height: .75rem; background: var(--chart-track); border-radius: 999px; overflow: hidden; }
.report-chart__track span { display: block; height: 100%; background: var(--chart-1); border-radius: inherit; }
.report-table { margin-bottom: 1rem; overflow-x: auto; }
.report-table table { width: 100%; border-collapse: collapse; white-space: nowrap; }
.report-table th, .report-table td { padding: .65rem .75rem; border-bottom: 1px solid var(--color-border-soft); text-align: left; }
/* 參考圖 04／06：表頭是淡藍分區，不是白色 */
.report-table th { background: var(--color-section); }
@media (max-width: 900px) { .report-filters { grid-template-columns: repeat(2, minmax(10rem, 1fr)); } }
/* 375px：兩欄（2 × 10rem + gap）仍會撐破畫面，改為單欄；
   圖表列的 7rem + 8rem + 8rem 固定軌道同樣超寬，改成標籤在上、數值在下的兩行式。 */
@media (max-width: 560px) {
  .report-filters { grid-template-columns: minmax(0, 1fr); }
  .report-chart__row { grid-template-columns: minmax(0, 1fr) auto; }
  .report-chart__track { grid-column: 1 / -1; }
}
</style>
