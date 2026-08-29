<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { isApiError } from '@doselect/web-shared/api'
import { useCouponAction, useCouponList, useCreateCoupon, useUpdateCoupon } from '../../features/coupons/useCoupons'
import {
  actionLabels,
  availableActions,
  describeDiscount,
  describeScope,
  describeUsage,
  formatDate,
  formatMoney,
  statusLabels,
} from '../../features/coupons/labels'
import type { CouponAction, CouponDto, CouponScopeType, CouponStatus } from '../../features/coupons/types'
import { describeScopeProblem, toScopeRequestFields } from '../../features/coupons/scope'
import CouponScopePicker from '../../components/coupons/CouponScopePicker.vue'
import { useSearchFilters } from '../../features/shared/useSearchFilters'
import { describeApiError } from '../../features/shared/errorMessages'

const statusOptions = Object.keys(statusLabels) as CouponStatus[]

const { filters, listParams, search, goToPage } = useSearchFilters(20)
const selectedStatuses = ref<CouponStatus[]>([])

const queryParams = computed(() => ({
  ...listParams.value,
  statuses: selectedStatuses.value,
}))

const { data: result, isPending, isError, error, refetch } = useCouponList(queryParams)
const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

const createMutation = useCreateCoupon()
const updateMutation = useUpdateCoupon()
const actionMutation = useCouponAction()

/** 展開中的優惠券，用來顯示規則預覽。 */
const expandedId = ref<string | null>(null)

/** 編輯中的優惠券；`null` 代表沒有在編輯。 */
const editing = ref<CouponDto | null>(null)
const showCreate = ref(false)

/**
 * 數值欄位型別是 `string | number`，不是 `string`。
 *
 * Vue 的 `v-model` 綁在 `<input type="number">` 上會自動把值轉成 number，
 * 空白時才是空字串。先前宣告成 `string` 並直接呼叫 `value.trim()`，
 * 使用者只要填了任何一個數值欄位就會在送出時炸掉。
 */
interface CouponFormState {
  code: string
  nameZhTw: string
  discountType: 'fixedAmount' | 'percentage'
  discountValue: string | number
  minimumSpend: string | number
  maximumDiscount: string | number
  startsAt: string
  endsAt: string
  totalUsageLimit: string | number
  perMemberLimit: string | number
  memberOnly: boolean
  excludeSaleItems: boolean
  scopeType: CouponScopeType
  categoryPublicIds: string[]
  productPublicIds: string[]
  excludedProductPublicIds: string[]
}

const form = reactive<CouponFormState>(emptyForm())

function emptyForm(): CouponFormState {
  return {
    code: '',
    nameZhTw: '',
    discountType: 'fixedAmount',
    discountValue: '',
    minimumSpend: '',
    maximumDiscount: '',
    startsAt: '',
    endsAt: '',
    totalUsageLimit: '',
    perMemberLimit: '',
    memberOnly: false,
    excludeSaleItems: false,
    scopeType: 'all',
    categoryPublicIds: [],
    productPublicIds: [],
    excludedProductPublicIds: [],
  }
}

function toggleStatus(status: CouponStatus) {
  const next = new Set(selectedStatuses.value)
  if (next.has(status)) {
    next.delete(status)
  }
  else {
    next.add(status)
  }
  selectedStatuses.value = [...next]
  filters.pageNumber = 1
}

function startCreate() {
  Object.assign(form, emptyForm())
  editing.value = null
  showCreate.value = true
  createMutation.reset()
}

function startEdit(coupon: CouponDto) {
  Object.assign(form, {
    code: coupon.code,
    nameZhTw: coupon.nameZhTw,
    discountType: coupon.discountType,
    // 百分比在 Domain 是 0～1 的比例；表單以百分點呈現比較好填。
    discountValue: coupon.discountValue === null
      ? ''
      : String(coupon.discountType === 'percentage'
        ? Number(coupon.discountValue) * 100
        : Number(coupon.discountValue)),
    minimumSpend: coupon.minimumSpend === null ? '' : String(Number(coupon.minimumSpend)),
    maximumDiscount: coupon.maximumDiscount === null ? '' : String(Number(coupon.maximumDiscount)),
    startsAt: coupon.startsAtUtc.slice(0, 16),
    endsAt: coupon.endsAtUtc.slice(0, 16),
    totalUsageLimit: coupon.usage.totalUsageLimit === null ? '' : String(Number(coupon.usage.totalUsageLimit)),
    perMemberLimit: coupon.usage.perMemberLimit === null ? '' : String(Number(coupon.usage.perMemberLimit)),
    memberOnly: coupon.memberOnly,
    excludeSaleItems: coupon.excludeSaleItems,
    scopeType: coupon.scope.scopeType,
    // 挑選器一律 emit 新陣列、不會就地改，這裡仍然各複製一份，
    // 免得表單狀態與 vue-query 快取裡的 DTO 共用同一個陣列實例。
    categoryPublicIds: [...coupon.scope.categoryPublicIds],
    productPublicIds: [...coupon.scope.productPublicIds],
    excludedProductPublicIds: [...coupon.scope.excludedProductPublicIds],
  })
  editing.value = coupon
  showCreate.value = false
  updateMutation.reset()
}

function cancelForm() {
  showCreate.value = false
  editing.value = null
}

function optionalNumber(value: string | number): number | null {
  if (typeof value === 'number') {
    return Number.isNaN(value) ? null : value
  }

  return value.trim() === '' ? null : Number(value)
}

/** 百分比表單填的是百分點，送出前換回 Domain 要的 0～1 比例。 */
function discountValueForApi(): number | null {
  const raw = optionalNumber(form.discountValue)
  if (raw === null) {
    return null
  }

  return form.discountType === 'percentage' ? raw / 100 : raw
}

function buildRuleFields() {
  return {
    code: form.code.trim(),
    nameZhTw: form.nameZhTw.trim(),
    discountType: form.discountType,
    discountValue: discountValueForApi(),
    minimumSpend: optionalNumber(form.minimumSpend),
    maximumDiscount: optionalNumber(form.maximumDiscount),
    startsAtUtc: new Date(form.startsAt).toISOString(),
    endsAtUtc: new Date(form.endsAt).toISOString(),
    totalUsageLimit: optionalNumber(form.totalUsageLimit),
    perMemberLimit: optionalNumber(form.perMemberLimit),
    memberOnly: form.memberOnly,
    excludeSaleItems: form.excludeSaleItems,
    ...toScopeRequestFields(form),
  }
}

/**
 * 範圍設定違反後端規則時的訊息；沒問題時為 `null`。
 *
 * 拿來擋送出按鈕，而不是等後端回一句英文 `validation_failed`。
 * 這**不是安全邊界** —— 後端仍會擋。
 */
const scopeProblem = computed(() => describeScopeProblem(form))

function submitCreate() {
  if (scopeProblem.value !== null) {
    return
  }

  createMutation.mutate(buildRuleFields(), {
    onSuccess: () => {
      showCreate.value = false
    },
  })
}

function submitUpdate() {
  const coupon = editing.value
  if (!coupon || scopeProblem.value !== null) {
    return
  }

  updateMutation.mutate({
    publicId: coupon.publicId,
    // rowVersion 一定要送回原值：後端以它做條件更新，過期就回 concurrency_conflict，
    // 不會靜默覆蓋別人的修改。
    request: { ...buildRuleFields(), rowVersion: coupon.rowVersion },
  }, {
    onSuccess: () => {
      editing.value = null
    },
  })
}

function runAction(coupon: CouponDto, action: CouponAction) {
  actionMutation.reset()
  actionMutation.mutate({
    publicId: coupon.publicId,
    action,
    request: {
      reasonCode: `coupon_${action}`,
      note: null,
      rowVersion: coupon.rowVersion,
    },
  })
}

function describeError(candidate: unknown): string {
  return isApiError(candidate) ? describeApiError(candidate) : '請稍後再試。'
}
</script>

<template>
  <section class="coupons">
    <header class="coupons-header">
      <h1>優惠券管理</h1>
      <button
        type="button"
        @click="startCreate"
      >
        新增優惠券
      </button>
    </header>

    <form
      class="coupons-filters"
      aria-label="優惠券搜尋"
      @submit.prevent="search"
    >
      <input
        v-model="filters.q"
        type="search"
        placeholder="搜尋優惠碼或名稱"
        aria-label="關鍵字"
      >
      <button type="submit">
        搜尋
      </button>
    </form>

    <fieldset class="coupons-statuses">
      <legend>狀態篩選</legend>
      <label
        v-for="status in statusOptions"
        :key="status"
      >
        <input
          type="checkbox"
          :checked="selectedStatuses.includes(status)"
          @change="toggleStatus(status)"
        >
        {{ statusLabels[status] }}
      </label>
    </fieldset>

    <p
      v-if="actionMutation.isError.value"
      class="coupons-error"
      role="alert"
    >
      {{ describeError(actionMutation.error.value) }}
    </p>

    <p v-if="isPending">
      載入中…
    </p>
    <div
      v-else-if="isError"
      role="alert"
    >
      <p>{{ describeError(error) }}</p>
      <button
        type="button"
        @click="refetch()"
      >
        重試
      </button>
    </div>
    <p v-else-if="!result?.items.length">
      沒有符合條件的優惠券
    </p>
    <table v-else>
      <caption class="sr-only">
        優惠券列表
      </caption>
      <thead>
        <tr>
          <th scope="col">
            優惠碼
          </th>
          <th scope="col">
            名稱
          </th>
          <th scope="col">
            狀態
          </th>
          <th scope="col">
            折扣
          </th>
          <th scope="col">
            期間
          </th>
          <th scope="col">
            使用量
          </th>
          <th scope="col">
            操作
          </th>
        </tr>
      </thead>
      <tbody>
        <template
          v-for="coupon in result.items"
          :key="coupon.publicId"
        >
          <tr>
            <td>{{ coupon.code }}</td>
            <td>{{ coupon.nameZhTw }}</td>
            <td>{{ statusLabels[coupon.status] }}</td>
            <td>{{ describeDiscount(coupon) }}</td>
            <td>{{ formatDate(coupon.startsAtUtc) }}～{{ formatDate(coupon.endsAtUtc) }}</td>
            <td>{{ describeUsage(coupon) }}</td>
            <td class="coupons-actions">
              <button
                type="button"
                @click="expandedId = expandedId === coupon.publicId ? null : coupon.publicId"
              >
                {{ expandedId === coupon.publicId ? '收合規則' : '規則預覽' }}
              </button>
              <button
                type="button"
                @click="startEdit(coupon)"
              >
                修改
              </button>
              <button
                v-for="action in availableActions(coupon.status)"
                :key="action"
                type="button"
                :disabled="actionMutation.isPending.value"
                @click="runAction(coupon, action)"
              >
                {{ actionLabels[action] }}
              </button>
            </td>
          </tr>
          <tr v-if="expandedId === coupon.publicId">
            <td colspan="7">
              <dl class="coupons-rule">
                <dt>最低消費</dt>
                <dd>{{ formatMoney(coupon.minimumSpend) }}</dd>
                <dt>每人限用</dt>
                <dd>{{ coupon.usage.perMemberLimit === null ? '不限' : coupon.usage.perMemberLimit }}</dd>
                <dt>剩餘名額</dt>
                <dd>{{ coupon.usage.remainingCount === null ? '不限' : coupon.usage.remainingCount }}</dd>
                <dt>限會員</dt>
                <dd>{{ coupon.memberOnly ? '是' : '否' }}</dd>
                <dt>排除特價品</dt>
                <dd>{{ coupon.excludeSaleItems ? '是' : '否' }}</dd>
                <dt>適用範圍</dt>
                <dd>{{ describeScope(coupon) }}</dd>
                <dt>規則版本</dt>
                <dd>{{ coupon.ruleVersion }}</dd>
              </dl>
            </td>
          </tr>
        </template>
      </tbody>
    </table>

    <nav
      v-if="totalPages > 1"
      class="coupons-pagination"
      aria-label="分頁"
    >
      <button
        type="button"
        :disabled="filters.pageNumber <= 1"
        @click="goToPage(filters.pageNumber - 1)"
      >
        上一頁
      </button>
      <span>第 {{ filters.pageNumber }} / {{ totalPages }} 頁</span>
      <button
        type="button"
        :disabled="filters.pageNumber >= totalPages"
        @click="goToPage(filters.pageNumber + 1)"
      >
        下一頁
      </button>
    </nav>

    <form
      v-if="showCreate || editing"
      class="coupons-form"
      :aria-label="showCreate ? '新增優惠券' : '修改優惠券'"
      @submit.prevent="showCreate ? submitCreate() : submitUpdate()"
    >
      <h2>{{ showCreate ? '新增優惠券' : `修改 ${editing?.code}` }}</h2>

      <label>優惠碼
        <input
          v-model="form.code"
          name="code"
          required
          maxlength="64"
        >
      </label>
      <label>名稱
        <input
          v-model="form.nameZhTw"
          name="nameZhTw"
          required
          maxlength="160"
        >
      </label>
      <label>折扣類型
        <select
          v-model="form.discountType"
          name="discountType"
        >
          <option value="fixedAmount">
            固定金額
          </option>
          <option value="percentage">
            百分比
          </option>
        </select>
      </label>
      <label>{{ form.discountType === 'percentage' ? '折扣百分比' : '折扣金額' }}
        <input
          v-model="form.discountValue"
          name="discountValue"
          type="number"
          step="any"
          required
        >
      </label>
      <label>最低消費
        <input
          v-model="form.minimumSpend"
          name="minimumSpend"
          type="number"
          step="any"
        >
      </label>
      <label>最高折抵
        <input
          v-model="form.maximumDiscount"
          name="maximumDiscount"
          type="number"
          step="any"
        >
      </label>
      <label>開始時間
        <input
          v-model="form.startsAt"
          name="startsAt"
          type="datetime-local"
          required
        >
      </label>
      <label>結束時間
        <input
          v-model="form.endsAt"
          name="endsAt"
          type="datetime-local"
          required
        >
      </label>
      <label>總名額
        <input
          v-model="form.totalUsageLimit"
          name="totalUsageLimit"
          type="number"
        >
      </label>
      <label>每人限用
        <input
          v-model="form.perMemberLimit"
          name="perMemberLimit"
          type="number"
        >
      </label>
      <label>
        <input
          v-model="form.memberOnly"
          type="checkbox"
        >
        限會員使用
      </label>
      <label>
        <input
          v-model="form.excludeSaleItems"
          type="checkbox"
        >
        排除特價品
      </label>

      <CouponScopePicker
        v-model:scope-type="form.scopeType"
        v-model:category-public-ids="form.categoryPublicIds"
        v-model:product-public-ids="form.productPublicIds"
        v-model:excluded-product-public-ids="form.excludedProductPublicIds"
      />

      <p
        v-if="scopeProblem !== null"
        class="coupons-error"
        role="alert"
      >
        {{ scopeProblem }}
      </p>

      <p
        v-if="createMutation.isError.value || updateMutation.isError.value"
        class="coupons-error"
        role="alert"
      >
        {{ describeError(createMutation.error.value ?? updateMutation.error.value) }}
      </p>

      <div class="coupons-form-actions">
        <button
          type="submit"
          :disabled="createMutation.isPending.value || updateMutation.isPending.value || scopeProblem !== null"
        >
          儲存
        </button>
        <button
          type="button"
          @click="cancelForm"
        >
          取消
        </button>
      </div>
    </form>
  </section>
</template>

<style scoped>
.coupons-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.coupons-filters {
  display: flex;
  gap: 0.75rem;
  margin-block: 1rem;
}

.coupons-filters input {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.coupons-statuses {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-block-end: 1.5rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
  padding: 0.75rem;
}

.coupons-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.coupons-rule {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.25rem 1rem;
  margin: 0;
}

.coupons-rule dt {
  font-weight: 600;
}

.coupons-rule dd {
  margin: 0;
}

.coupons-error {
  color: #b91c1c;
}

.coupons-form {
  display: grid;
  gap: 0.75rem;
  margin-block-start: 2rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
  padding: 1rem;
  max-width: 32rem;
}

.coupons-form-actions {
  display: flex;
  gap: 0.75rem;
}

.coupons-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  margin-block-start: 1.5rem;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip-path: inset(50%);
}
</style>
