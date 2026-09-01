<script setup lang="ts">
/** A-18 (M功能桌面UI與Route規格.md): 超商／宅配限制版本、草稿、排程發布及歷史（UC-ADM-SHIP-01）。 */
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref } from 'vue'
import {
  useCreatePackageLimitVersion,
  usePackageLimitVersionList,
  usePublishPackageLimitVersion,
} from '../features/shipping/useShipping'
import {
  PACKAGE_LIMIT_SAFE_RANGES,
  PROVIDER_LABELS,
  SHIPPING_PROVIDER_CODES,
  type PackageLimitVersionDto,
  type ShippingProviderCode,
} from '../features/shipping/types'
import { describeApiError } from '../features/shared/errorMessages'

const providerCode = ref<ShippingProviderCode>('StorePickup')
const safeRange = computed(() => PACKAGE_LIMIT_SAFE_RANGES[providerCode.value])

const { data: versions, isPending, isError, error, refetch } = usePackageLimitVersionList(providerCode)
const createMutation = useCreatePackageLimitVersion()
const publishMutation = usePublishPackageLimitVersion()

const isCreating = ref(false)
const draft = reactive({
  maxLengthCm: 45,
  maxWidthCm: 45,
  maxHeightCm: 45,
  maxTotalCm: 105,
  maxWeightKg: 5,
  maxDeclaredValue: 20000,
  effectiveFromUtc: '',
  effectiveToUtc: '',
})

function startCreate() {
  isCreating.value = true
  const range = safeRange.value
  draft.maxLengthCm = range.maxSideCm
  draft.maxWidthCm = range.maxSideCm
  draft.maxHeightCm = range.maxSideCm
  draft.maxTotalCm = range.maxTotalCm
  draft.maxWeightKg = range.maxWeightKg
  draft.maxDeclaredValue = 20000
  draft.effectiveFromUtc = ''
  draft.effectiveToUtc = ''
}

/**
 * `<input type="datetime-local">` 給的是沒有時區的本地時間字串。後端只接受 UTC 瞬間（沒有 Z 的值
 * 會被拒為 validation_failed），所以送出前要把它當成瀏覽器本地時間轉成真正的 UTC ISO 字串——不能
 * 直接把字串後面接一個 Z，那等於謊稱管理員輸入的是 UTC 時刻。
 */
function toUtcInstant(localValue: string): string | null {
  if (!localValue) {
    return null
  }
  const parsed = new Date(localValue)
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString()
}

/** 安全範圍是程式固定的（購物車、訂單、付款與物流.md），後端一定會再驗一次；這裡先擋是為了
 * 讓管理員在送出前就看到問題，而不是收到一個 400 才知道。 */
const draftProblems = computed(() => {
  const range = safeRange.value
  const problems: string[] = []
  const sides: [string, number][] = [
    ['長', draft.maxLengthCm],
    ['寬', draft.maxWidthCm],
    ['高', draft.maxHeightCm],
  ]
  for (const [label, value] of sides) {
    if (value < range.minSideCm || value > range.maxSideCm) {
      problems.push(`${label}需介於 ${range.minSideCm}～${range.maxSideCm} cm`)
    }
  }

  if (draft.maxTotalCm < range.minTotalCm || draft.maxTotalCm > range.maxTotalCm) {
    problems.push(`三邊和需介於 ${range.minTotalCm}～${range.maxTotalCm} cm`)
  }

  // 跨欄位規則：單邊不得大於三邊和（購物車、訂單、付款與物流.md）。
  const largestSide = Math.max(draft.maxLengthCm, draft.maxWidthCm, draft.maxHeightCm)
  if (largestSide > draft.maxTotalCm) {
    problems.push('單邊不得大於三邊和')
  }

  if (draft.maxWeightKg < range.minWeightKg || draft.maxWeightKg > range.maxWeightKg) {
    problems.push(`重量需介於 ${range.minWeightKg}～${range.maxWeightKg} kg`)
  }

  if (draft.maxDeclaredValue <= 0) {
    problems.push('申報價值需為正數')
  }

  const from = toUtcInstant(draft.effectiveFromUtc)
  const to = toUtcInstant(draft.effectiveToUtc)
  if (to !== null && new Date(to).getTime() <= new Date(from ?? new Date().toISOString()).getTime()) {
    problems.push('結束時間需晚於生效時間；立即生效的版本其結束時間必須在未來')
  }

  return problems
})

function submitCreate() {
  if (draftProblems.value.length > 0) {
    return
  }
  createMutation.mutate({
    providerCode: providerCode.value,
    request: {
      providerCode: providerCode.value,
      maxWeightKg: draft.maxWeightKg,
      maxLengthCm: draft.maxLengthCm,
      maxWidthCm: draft.maxWidthCm,
      maxHeightCm: draft.maxHeightCm,
      maxTotalCm: draft.maxTotalCm,
      maxDeclaredValue: draft.maxDeclaredValue,
      effectiveFromUtc: toUtcInstant(draft.effectiveFromUtc),
      effectiveToUtc: toUtcInstant(draft.effectiveToUtc),
    },
  }, { onSuccess: () => { isCreating.value = false } })
}

/**
 * 發布會把目前生效的版本收窗到新版本的生效時間，之後就無法回頭（UC-ADM-SHIP-01：不覆寫舊版本，
 * 但接班關係是既成事實），且立刻影響所有購物車的超取資格計算，所以要二次確認。
 */
function confirmPublish(version: PackageLimitVersionDto) {
  const takesEffect = version.effectiveFromUtc
    ? new Date(version.effectiveFromUtc).toLocaleString('zh-Hant-TW')
    : '立即'
  if (!globalThis.confirm(`確定要發布 ${PROVIDER_LABELS[providerCode.value]} 的版本 ${version.version} 嗎？生效時間：${takesEffect}。發布後目前生效的版本會在該時間點交棒，購物車的超取資格將依新版本重新計算。`)) {
    return
  }
  publishMutation.mutate({
    providerCode: providerCode.value,
    versionPublicId: version.publicId,
    rowVersion: version.rowVersion,
  })
}

const mutationError = computed(() => {
  for (const mutation of [createMutation, publishMutation]) {
    if (isApiError(mutation.error.value)) {
      return describeApiError(mutation.error.value)
    }
  }
  return null
})

function formatDateTime(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleString('zh-Hant-TW') : '—'
}

/** 目前這一刻真正生效的版本：不是 Draft，且落在自己的 [From, To) 窗內（組長 PR #73 裁定 B1——
 * 可用性看時間窗，不看 Published 這個狀態字，Superseded 在 cutoff 前仍然有效）。 */
function isEffectiveNow(version: PackageLimitVersionDto): boolean {
  if (version.status === 'Draft') {
    return false
  }
  const now = Date.now()
  const from = version.effectiveFromUtc ? new Date(version.effectiveFromUtc).getTime() : Number.NEGATIVE_INFINITY
  const to = version.effectiveToUtc ? new Date(version.effectiveToUtc).getTime() : Number.POSITIVE_INFINITY
  return from <= now && now < to
}
</script>

<template>
  <section aria-labelledby="package-limits-title">
    <h1 id="package-limits-title">
      包裹限制版本
    </h1>

    <form
      class="limits-toolbar"
      aria-label="物流服務選擇"
      @submit.prevent
    >
      <label>
        物流服務
        <select
          v-model="providerCode"
          aria-label="物流服務"
        >
          <option
            v-for="code in SHIPPING_PROVIDER_CODES"
            :key="code"
            :value="code"
          >
            {{ PROVIDER_LABELS[code] }}
          </option>
        </select>
      </label>
      <button
        type="button"
        @click="startCreate"
      >
        新增草稿
      </button>
      <p class="limits-range">
        安全範圍：單邊 {{ safeRange.minSideCm }}～{{ safeRange.maxSideCm }} cm、
        三邊和 {{ safeRange.minTotalCm }}～{{ safeRange.maxTotalCm }} cm、
        重量 {{ safeRange.minWeightKg }}～{{ safeRange.maxWeightKg }} kg（程式固定，不可突破）
      </p>
    </form>

    <p
      v-if="mutationError"
      class="limits-error"
      role="alert"
    >
      {{ mutationError }}
    </p>

    <form
      v-if="isCreating"
      class="limits-form"
      aria-label="新增包裹限制草稿"
      @submit.prevent="submitCreate"
    >
      <label>
        最長邊（cm）
        <input
          v-model.number="draft.maxLengthCm"
          type="number"
          step="0.01"
          aria-label="最長邊"
        >
      </label>
      <label>
        寬（cm）
        <input
          v-model.number="draft.maxWidthCm"
          type="number"
          step="0.01"
          aria-label="寬"
        >
      </label>
      <label>
        高（cm）
        <input
          v-model.number="draft.maxHeightCm"
          type="number"
          step="0.01"
          aria-label="高"
        >
      </label>
      <label>
        三邊和（cm）
        <input
          v-model.number="draft.maxTotalCm"
          type="number"
          step="0.01"
          aria-label="三邊和"
        >
      </label>
      <label>
        重量（kg）
        <input
          v-model.number="draft.maxWeightKg"
          type="number"
          step="0.01"
          aria-label="重量"
        >
      </label>
      <label>
        申報價值上限
        <input
          v-model.number="draft.maxDeclaredValue"
          type="number"
          step="1"
          aria-label="申報價值上限"
        >
      </label>
      <label>
        生效時間（留空＝發布後立即生效）
        <input
          v-model="draft.effectiveFromUtc"
          type="datetime-local"
          aria-label="生效時間"
        >
      </label>
      <label>
        結束時間（留空＝無限期）
        <input
          v-model="draft.effectiveToUtc"
          type="datetime-local"
          aria-label="結束時間"
        >
      </label>

      <ul
        v-if="draftProblems.length > 0"
        class="limits-problems"
        aria-label="草稿問題"
      >
        <li
          v-for="problem in draftProblems"
          :key="problem"
        >
          {{ problem }}
        </li>
      </ul>

      <div class="limits-form__actions">
        <button
          type="submit"
          :disabled="draftProblems.length > 0 || createMutation.isPending.value"
        >
          建立草稿
        </button>
        <button
          type="button"
          @click="isCreating = false"
        >
          取消
        </button>
      </div>
    </form>

    <LoadingState
      v-if="isPending && !versions"
      label="版本載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />
    <EmptyState
      v-else-if="(versions?.length ?? 0) === 0"
      title="這個物流服務還沒有任何限制版本"
    />
    <table
      v-else
      class="limits-table"
    >
      <thead>
        <tr>
          <th>版本</th>
          <th>狀態</th>
          <th>長／寬／高（cm）</th>
          <th>三邊和</th>
          <th>重量（kg）</th>
          <th>申報價值</th>
          <th>生效時間</th>
          <th>結束時間</th>
          <th />
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="version in versions ?? []"
          :key="version.publicId"
        >
          <td>{{ version.version }}</td>
          <td>
            {{ version.status }}
            <span
              v-if="isEffectiveNow(version)"
              class="limits-badge"
            >目前生效</span>
          </td>
          <td>{{ version.maxLengthCm }} / {{ version.maxWidthCm }} / {{ version.maxHeightCm }}</td>
          <td>{{ version.maxTotalCm }}</td>
          <td>{{ version.maxWeightKg }}</td>
          <td>{{ version.maxDeclaredValue }}</td>
          <td>{{ formatDateTime(version.effectiveFromUtc) }}</td>
          <td>{{ formatDateTime(version.effectiveToUtc) }}</td>
          <td>
            <button
              v-if="version.status === 'Draft'"
              type="button"
              :disabled="publishMutation.isPending.value"
              @click="confirmPublish(version)"
            >
              發布
            </button>
          </td>
        </tr>
      </tbody>
    </table>
  </section>
</template>

<style scoped>
.limits-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.75rem;
  margin-block-end: 1.5rem;
}

.limits-toolbar label,
.limits-form label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
}

.limits-range {
  flex-basis: 100%;
  margin: 0;
  color: #6b7280;
  font-size: 0.8125rem;
}

.limits-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
  margin-block-end: 1.5rem;
}

.limits-form__actions {
  display: flex;
  gap: 0.5rem;
}

.limits-problems {
  flex-basis: 100%;
  margin: 0;
  padding-inline-start: 1.25rem;
  color: #b91c1c;
  font-size: 0.8125rem;
}

.limits-table {
  width: 100%;
  border-collapse: collapse;
}

.limits-table th,
.limits-table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}

.limits-badge {
  margin-inline-start: 0.375rem;
  padding: 0.125rem 0.375rem;
  border-radius: 0.25rem;
  background: #dcfce7;
  color: #166534;
  font-size: 0.75rem;
}

.limits-error {
  color: #b91c1c;
  margin-block-end: 1rem;
}
</style>
