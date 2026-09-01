<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { useAdminAiUsageQuery } from '../features/aiUsage/queries'

const query = useAdminAiUsageQuery()

function formatNumber(value: number | string) {
  return Number(value).toLocaleString('zh-TW')
}

function formatCost(value: number | string | null) {
  return value === null ? '無權限' : `US$${Number(value).toFixed(6)}`
}
</script>

<template>
  <section aria-labelledby="ai-usage-title">
    <h1 id="ai-usage-title">
      AI 用量與成本
    </h1>
    <p class="view-lede">
      最近 30 天依功能、模型與結果彙總；成本金額只提供 FinanceManager 與 SuperAdmin。
    </p>

    <LoadingState v-if="query.isPending.value" />
    <ErrorState
      v-else-if="query.isError.value"
      title="無法載入 AI 用量"
      :description="isApiError(query.error.value) ? query.error.value.message : '請稍後再試。'"
      @retry="query.refetch()"
    />
    <template v-else-if="query.data.value">
      <div
        v-if="query.data.value.budgetProtectionActive"
        class="ai-budget ai-budget--danger"
        role="alert"
      >
        累計成本已達 US$90，非 Demo AI 流量已停止。
      </div>
      <div
        v-else-if="query.data.value.budgetWarningActive"
        class="ai-budget ai-budget--warning"
        role="status"
      >
        累計成本已達 US$70 警告門檻，請檢查剩餘預算。
      </div>

      <div class="card ai-summary">
        <span>累計估算成本</span>
        <strong>{{ formatCost(query.data.value.cumulativeCostUsd) }}</strong>
        <small>資料截至 {{ new Date(query.data.value.dataAsOfUtc).toLocaleString('zh-TW') }}</small>
      </div>

      <EmptyState
        v-if="query.data.value.rows.length === 0"
        title="目前沒有 AI 互動紀錄"
        description="啟用 AI 並完成互動後，用量會顯示在這裡。"
      />
      <div
        v-else
        class="table-scroll"
      >
        <table>
          <thead>
            <tr>
              <th>功能</th>
              <th>模型</th>
              <th>結果</th>
              <th>互動數</th>
              <th>輸入 Token</th>
              <th>輸出 Token</th>
              <th>估算成本</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="row in query.data.value.rows"
              :key="`${row.feature}-${row.model}-${row.status}`"
            >
              <td>{{ row.feature }}</td>
              <td>{{ row.model }}</td>
              <td>{{ row.status }}</td>
              <td>{{ formatNumber(row.interactionCount) }}</td>
              <td>{{ formatNumber(row.inputTokens) }}</td>
              <td>{{ formatNumber(row.outputTokens) }}</td>
              <td>{{ formatCost(row.estimatedCostUsd) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </section>
</template>

<style scoped>
.ai-budget { margin: 1rem 0; padding: .9rem 1rem; border-radius: var(--radius-sm); font-weight: 700; }
.ai-budget--warning { background: var(--color-warning-bg); color: var(--color-warning); border: 1px solid var(--color-warning-border); }
.ai-budget--danger { background: var(--color-danger-bg); color: var(--color-danger); border: 1px solid var(--color-danger-border); }
.ai-summary { display: grid; gap: .35rem; margin: 1rem 0; }
.ai-summary strong { font-size: 1.6rem; }
.ai-summary small { color: var(--color-text-muted); }
</style>
