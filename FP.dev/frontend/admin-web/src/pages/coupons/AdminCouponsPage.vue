<script setup lang="ts">
import { PagePager } from '@doselect/web-shared/components'
import { computed, nextTick, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { useCouponAction, useCouponList, useCreateCoupon, useUpdateCoupon } from '../../features/coupons/useCoupons'
import {
  actionLabels,
  availableActions,
  canEditRules,
  describeDiscount,
  discountTypeLabels,
  isAmountDiscount,
  describeScope,
  describeUsage,
  formatDate,
  formatMoney,
  statusLabels,
} from '../../features/coupons/labels'
import type {
  CouponAction,
  CouponDiscountType,
  CouponDto,
  CouponScopeType,
  CouponStatus,
} from '../../features/coupons/types'
import { describeScopeProblem, toScopeRequestFields } from '../../features/coupons/scope'
import { describeDiscountProblem } from '../../features/coupons/discountRules'
import { toLocalInputValue, toUtcInstant } from '../../features/coupons/dateTime'
import CouponScopePicker from '../../components/coupons/CouponScopePicker.vue'
import { useSearchFilters } from '../../features/shared/useSearchFilters'
import { describeApiError } from '../../features/shared/errorMessages'

const statusOptions = Object.keys(statusLabels) as CouponStatus[]

const route = useRoute()
const router = useRouter()

const { filters, listParams, search, goToPage, restore: restoreSearchFilters } = useSearchFilters(20)
const selectedStatuses = ref<CouponStatus[]>([])

type CouponSortField = 'code' | 'status' | 'period'
type CouponSortDirection = 'Asc' | 'Desc'

const sortField = ref<CouponSortField | null>(null)
const sortDirection = ref<CouponSortDirection>('Asc')

const sortOption = computed(() => {
  if (sortField.value === null) return undefined
  const apiField = sortField.value === 'period' ? 'endsAt' : sortField.value
  return `${apiField}${sortDirection.value}`
})

function setSort(field: CouponSortField) {
  if (sortField.value === field) {
    sortDirection.value = sortDirection.value === 'Asc' ? 'Desc' : 'Asc'
  }
  else {
    sortField.value = field
    sortDirection.value = 'Asc'
  }
  filters.pageNumber = 1
}

function ariaSort(field: CouponSortField): 'none' | 'ascending' | 'descending' {
  if (sortField.value !== field) return 'none'
  return sortDirection.value === 'Asc' ? 'ascending' : 'descending'
}

function sortIndicator(field: CouponSortField): string {
  if (sortField.value !== field) return '↕'
  return sortDirection.value === 'Asc' ? '↑' : '↓'
}

/**
 * 從網址還原列表條件。
 *
 * `restoreSearchFilters` 會把已套用關鍵字與頁碼當成同一個狀態還原，避免一般輸入
 * watcher 把網址中的頁碼重設為 1。
 */
function restoreFromQuery() {
  const q = typeof route.query.q === 'string' ? route.query.q : ''
  // route.query 的值可能是字串、字串陣列，或含 null 的陣列（?status= 這種空值）。
  const raw = route.query.status
  const statuses = (Array.isArray(raw) ? raw : [raw])
    .filter((value): value is CouponStatus => statusOptions.includes(value as CouponStatus))
  const page = Number(route.query.page)

  restoreSearchFilters(q, Number.isInteger(page) && page > 0 ? page : 1)
  selectedStatuses.value = statuses
}

// 不能只在 setup 時還原：瀏覽器返回／前進會重用同一個頁面元件，只改變 route。
// 監看 fullPath 也涵蓋同一路徑下的 Query 導覽，並避免不必要的 deep watcher。
watch(() => route.fullPath, restoreFromQuery, { immediate: true })

/**
 * 把已套用的條件寫回網址。
 *
 * 用 `replace` 不用 `push`：每打一個字就堆一筆瀏覽記錄，返回鍵會變成一次退一個字元。
 * 同步的是**已套用**的關鍵字（debounce 之後）而不是輸入框的即時值 —— 網址代表的是
 * 目前這份清單的查詢條件。
 */
watch(
  () => ({
    q: listParams.value.q,
    status: selectedStatuses.value,
    page: filters.pageNumber,
  }),
  (applied) => {
    const query: Record<string, string | string[]> = {}
    if (applied.q !== '') {
      query.q = applied.q
    }
    if (applied.status.length > 0) {
      query.status = [...applied.status]
    }
    if (applied.page > 1) {
      query.page = String(applied.page)
    }

    // 內容相同就不要再 replace 一次，否則 watch 會被自己觸發的導覽再喚醒。
    if (JSON.stringify(query) === JSON.stringify(route.query)) {
      return
    }

    void router.replace({ query })
  },
  { deep: true },
)

const queryParams = computed(() => ({
  ...listParams.value,
  statuses: selectedStatuses.value,
  sort: sortOption.value,
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
const formDialog = ref<HTMLDialogElement | null>(null)

async function openFormDialog() {
  await nextTick()
  const dialog = formDialog.value
  if (dialog && !dialog.open) {
    dialog.showModal?.()
  }
}

function closeFormDialog() {
  const dialog = formDialog.value
  if (dialog?.open) {
    dialog.close()
  }
}

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
  discountType: CouponDiscountType
  discountValue: string | number
  minimumSpend: string | number
  maximumDiscount: string | number
  multiItemDiscountValue: string | number
  memberValidityMonths: string | number
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
    multiItemDiscountValue: '',
    memberValidityMonths: '',
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

/**
 * 清掉兩個寫入 mutation 的錯誤。
 *
 * 錯誤區同時讀 `createMutation` 與 `updateMutation`，只清目前這一個的話，
 * 修改失敗後取消、再開新增表單，還會掛著上一次的修改錯誤（反向亦然）。
 */
function resetFormErrors() {
  createMutation.reset()
  updateMutation.reset()
}

function startCreate() {
  Object.assign(form, emptyForm())
  editing.value = null
  showCreate.value = true
  resetFormErrors()
  void openFormDialog()
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
    multiItemDiscountValue: coupon.multiItemDiscountValue == null ? '' : Number(coupon.multiItemDiscountValue) * 100,
    memberValidityMonths: coupon.memberValidityMonths ?? '',
    startsAt: toLocalInputValue(coupon.startsAtUtc),
    endsAt: toLocalInputValue(coupon.endsAtUtc),
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
  resetFormErrors()
  void openFormDialog()
}

function cancelForm() {
  closeFormDialog()
  showCreate.value = false
  editing.value = null
}

function discountSegments(coupon: CouponDto): string[] {
  const description = describeDiscount(coupon)
  return description.match(/[^、，,；;]+[、，,；;]?/gu) ?? [description]
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
    // 免運券不帶折扣金額。切換類型時只把欄位藏起來是不夠的 —— 先填了金額
    // 再改成免運，那個數字還在表單狀態裡，照樣會被送出去。
    discountValue: isAmountDiscount(form.discountType) ? discountValueForApi() : null,
    minimumSpend: optionalNumber(form.minimumSpend),
    maximumDiscount: isAmountDiscount(form.discountType)
      ? optionalNumber(form.maximumDiscount)
      : null,
    // 帶上原值：欄位沒被動過就原樣送回，不讓時段因為經過表單而被改寫。
    startsAtUtc: toUtcInstant(form.startsAt, editing.value?.startsAtUtc),
    endsAtUtc: toUtcInstant(form.endsAt, editing.value?.endsAtUtc),
    totalUsageLimit: optionalNumber(form.totalUsageLimit),
    perMemberLimit: optionalNumber(form.perMemberLimit),
    memberOnly: form.memberOnly,
    multiItemDiscountValue: form.discountType === 'percentage' && optionalNumber(form.multiItemDiscountValue) !== null
      ? Number(form.multiItemDiscountValue) / 100 : null,
    memberValidityMonths: optionalNumber(form.memberValidityMonths),
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

/**
 * 表單目前違反的第一條規則；沒有問題時為 `null`。
 *
 * 範圍與折扣兩組規則合成一個判斷，送出按鈕與錯誤訊息都只看它 ——
 * 分成兩個各自控制的話，很容易出現「訊息顯示了但按鈕沒鎖」這種漏掉一邊的情況。
 */
const formProblem = computed(() =>
  scopeProblem.value
  ?? (editing.value?.multiItemDiscountValue != null && optionalNumber(form.multiItemDiscountValue) === null
    ? '既有件數折扣不可清空；如需移除規則，請另建優惠券。' : null)
  ?? (optionalNumber(form.memberValidityMonths) !== null && !form.memberOnly ? '入會期限優惠必須限定會員使用。' : null)
  ?? describeDiscountProblem(
    form.discountType,
    discountValueForApi(),
    optionalNumber(form.maximumDiscount),
    optionalNumber(form.multiItemDiscountValue) === null ? null : Number(form.multiItemDiscountValue) / 100))

function submitCreate() {
  if (formProblem.value !== null) {
    return
  }

  createMutation.mutate(buildRuleFields(), {
    onSuccess: () => {
      closeFormDialog()
      showCreate.value = false
    },
  })
}

function submitUpdate() {
  const coupon = editing.value
  if (!coupon || formProblem.value !== null) {
    return
  }

  updateMutation.mutate({
    publicId: coupon.publicId,
    // rowVersion 一定要送回原值：後端以它做條件更新，過期就回 concurrency_conflict，
    // 不會靜默覆蓋別人的修改。
    request: { ...buildRuleFields(), rowVersion: coupon.rowVersion },
  }, {
    onSuccess: () => {
      closeFormDialog()
      editing.value = null
    },
  })
}

/** 等待確認的停用動作；`null` 代表沒有待確認的動作。 */
const pendingDisable = ref<CouponDto | null>(null)

/**
 * 按下狀態動作。
 *
 * `disable` 走確認對話框：`disabled` 是終態，誤觸之後沒有任何方式可以重新啟用。
 * `activate` 與 `pause` 都可以再切回來，不需要擋一層。
 */
function requestAction(coupon: CouponDto, action: CouponAction) {
  if (action !== 'disable') {
    runAction(coupon, action)
    return
  }

  actionMutation.reset()
  pendingDisable.value = coupon
}

function confirmDisable() {
  const coupon = pendingDisable.value
  if (coupon === null) {
    return
  }

  pendingDisable.value = null
  runAction(coupon, 'disable')
}

/** 取消**不能**呼叫 API —— 這正是對話框存在的理由。 */
function cancelDisable() {
  pendingDisable.value = null
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

    <div
      v-if="pendingDisable !== null"
      class="coupons-confirm"
      role="alertdialog"
      aria-label="確認停用優惠券"
    >
      <h2>確認停用「{{ pendingDisable.code }}」？</h2>
      <dl>
        <dt>名稱</dt>
        <dd>{{ pendingDisable.nameZhTw }}</dd>
        <dt>目前狀態</dt>
        <dd>{{ statusLabels[pendingDisable.status] }}</dd>
        <dt>已使用</dt>
        <dd>{{ describeUsage(pendingDisable) }}</dd>
      </dl>
      <p>
        停用後這張券立即無法再被套用，已完成的訂單不受影響。
      </p>
      <p class="coupons-confirm-warning">
        <strong>「已停用」是終態，無法再啟用或修改規則。</strong>
        需要暫時停售請改用「暫停」。
      </p>
      <div class="coupons-confirm-actions">
        <button
          type="button"
          :disabled="actionMutation.isPending.value"
          @click="confirmDisable"
        >
          確認停用
        </button>
        <button
          type="button"
          @click="cancelDisable"
        >
          取消
        </button>
      </div>
    </div>

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
    <div
      v-else
      class="table-scroll coupons-table"
      role="region"
      aria-label="優惠券清單表格"
      tabindex="0"
    >
      <table>
        <caption class="sr-only">
          優惠券列表
        </caption>
        <thead>
          <tr>
            <th
              scope="col"
              :aria-sort="ariaSort('code')"
            >
              <button
                type="button"
                class="coupons-sort-button"
                aria-label="依優惠碼排序"
                @click="setSort('code')"
              >
                優惠碼 <span aria-hidden="true">{{ sortIndicator('code') }}</span>
              </button>
            </th>
            <th scope="col">
              名稱
            </th>
            <th
              scope="col"
              :aria-sort="ariaSort('status')"
            >
              <button
                type="button"
                class="coupons-sort-button"
                aria-label="依狀態排序"
                @click="setSort('status')"
              >
                狀態 <span aria-hidden="true">{{ sortIndicator('status') }}</span>
              </button>
            </th>
            <th scope="col">
              折扣
            </th>
            <th
              scope="col"
              :aria-sort="ariaSort('period')"
            >
              <button
                type="button"
                class="coupons-sort-button"
                aria-label="依期間排序"
                @click="setSort('period')"
              >
                期間 <span aria-hidden="true">{{ sortIndicator('period') }}</span>
              </button>
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
              <td class="coupons-cell--nowrap">
                {{ coupon.nameZhTw }}
              </td>
              <td class="coupons-cell--nowrap">
                {{ statusLabels[coupon.status] }}
              </td>
              <td class="coupons-discount">
                <span
                  v-for="(segment, index) in discountSegments(coupon)"
                  :key="`${segment}-${index}`"
                  class="coupons-discount__segment"
                >{{ segment }}</span>
              </td>
              <td class="coupons-period">
                <template v-if="coupon.memberValidityMonths === 12">
                  <span class="coupons-cell--nowrap">入會日起 1 年內（滿周年到期）</span>
                </template>
                <template v-else>
                  <span class="coupons-period__start">{{ formatDate(coupon.startsAtUtc) }}～</span><br>
                  <span class="coupons-cell--nowrap">{{ formatDate(new Date(new Date(coupon.endsAtUtc).getTime() - 1).toISOString()) }}</span>
                </template>
              </td>
              <td class="coupons-cell--nowrap">
                {{ describeUsage(coupon) }}
              </td>
              <td>
                <div class="coupons-actions">
                  <button
                    type="button"
                    @click="expandedId = expandedId === coupon.publicId ? null : coupon.publicId"
                  >
                    {{ expandedId === coupon.publicId ? '收合規則' : '規則預覽' }}
                  </button>
                  <button
                    v-if="canEditRules(coupon.status)"
                    type="button"
                    @click="startEdit(coupon)"
                  >
                    修改
                  </button>
                  <button
                    v-for="action in availableActions(coupon.status)"
                    :key="action"
                    type="button"
                    :class="{ 'coupons-action--danger': action === 'disable' }"
                    :disabled="actionMutation.isPending.value"
                    @click="requestAction(coupon, action)"
                  >
                    {{ actionLabels[action] }}
                  </button>
                </div>
              </td>
            </tr>
            <tr v-if="expandedId === coupon.publicId">
              <td colspan="7">
                <dl class="coupons-rule">
                  <dt>最低消費</dt>
                  <dd>{{ coupon.minimumSpend == null || Number(coupon.minimumSpend) === 0 ? '無限制' : formatMoney(coupon.minimumSpend) }}</dd>
                  <dt>合併使用</dt>
                  <dd>不可合併，每筆訂單限用一張優惠券</dd>
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
    </div>

    <PagePager
      v-if="totalPages > 1"
      :page="filters.pageNumber"
      :page-size="1"
      :total-records="totalPages"
      aria-label="列表分頁"
      @update:page="goToPage"
    />

    <dialog
      v-if="showCreate || editing"
      ref="formDialog"
      class="coupons-modal"
      :aria-label="showCreate ? '新增優惠券' : '修改優惠券'"
      @cancel.prevent="cancelForm"
    >
      <form
        class="coupons-form coupons-form--aligned"
        @submit.prevent="showCreate ? submitCreate() : submitUpdate()"
      >
        <h2>{{ showCreate ? '新增優惠券' : `修改 ${editing?.code}` }}</h2>

        <label class="coupons-field">優惠碼
          <input
            v-model="form.code"
            class="coupons-control"
            name="code"
            required
            maxlength="64"
          >
        </label>
        <label class="coupons-field">名稱
          <input
            v-model="form.nameZhTw"
            class="coupons-control"
            name="nameZhTw"
            required
            maxlength="160"
          >
        </label>
        <label class="coupons-field">折扣類型
          <select
            v-model="form.discountType"
            class="coupons-control"
            name="discountType"
          >
            <option
              v-for="(label, value) in discountTypeLabels"
              :key="value"
              :value="value"
            >
              {{ label }}
            </option>
          </select>
        </label>
        <label
          v-if="isAmountDiscount(form.discountType)"
          class="coupons-field"
        >{{ form.discountType === 'percentage' ? '折扣百分比' : '折扣金額' }}
          <input
            v-model="form.discountValue"
            class="coupons-control"
            name="discountValue"
            type="number"
            step="any"
            required
          >
        </label>
        <label class="coupons-field">最低消費
          <input
            v-model="form.minimumSpend"
            class="coupons-control"
            name="minimumSpend"
            type="number"
            step="any"
          >
        </label>
        <label
          v-if="form.discountType === 'percentage'"
          class="coupons-field"
        >2 件以上折扣百分比（選填）
          <input
            v-model="form.multiItemDiscountValue"
            class="coupons-control"
            name="multiItemDiscountValue"
            type="number"
            min="0"
            max="100"
            step="any"
          >
          <span>以適用商品總數量計算，同商品多件也計入。5 表示九五折，10 表示九折。</span>
        </label>
        <label
          v-if="isAmountDiscount(form.discountType)"
          class="coupons-field"
        >
          {{ form.discountType === 'percentage' && optionalNumber(form.multiItemDiscountValue) === null ? '最高折抵（必填）' : '最高折抵（選填，留空為無上限）' }}
          <input
            v-model="form.maximumDiscount"
            class="coupons-control"
            name="maximumDiscount"
            type="number"
            step="any"
            :required="form.discountType === 'percentage' && optionalNumber(form.multiItemDiscountValue) === null"
          >
        </label>
        <label class="coupons-field">開始時間
          <input
            v-model="form.startsAt"
            class="coupons-control"
            name="startsAt"
            type="datetime-local"
            required
          >
        </label>
        <label class="coupons-field">結束時間
          <input
            v-model="form.endsAt"
            class="coupons-control"
            name="endsAt"
            type="datetime-local"
            required
          >
        </label>
        <label class="coupons-field">總名額
          <input
            v-model="form.totalUsageLimit"
            class="coupons-control"
            name="totalUsageLimit"
            type="number"
          >
        </label>
        <label class="coupons-field">每人限用
          <input
            v-model="form.perMemberLimit"
            class="coupons-control"
            name="perMemberLimit"
            type="number"
          >
        </label>
        <label class="coupons-field">會員入會期限
          <select
            v-model="form.memberValidityMonths"
            class="coupons-control"
            name="memberValidityMonths"
          >
            <option
              value=""
              :disabled="editing?.memberValidityMonths != null"
            >不限制</option>
            <option :value="12">入會日起 1 年內（包含既有會員）</option>
          </select>
          <span v-if="editing?.memberValidityMonths != null">既有入會期限不可移除；如需不同規則請另建優惠券。</span>
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
          v-if="formProblem !== null"
          class="coupons-error"
          role="alert"
        >
          {{ formProblem }}
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
            :disabled="createMutation.isPending.value || updateMutation.isPending.value || formProblem !== null"
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
    </dialog>
  </section>
</template>

<style scoped>
.coupons-confirm {
  border: 2px solid #b00020;
  border-radius: 4px;
  margin-bottom: 1rem;
  padding: 1rem;
}

.coupons-confirm-warning {
  color: #b00020;
}

.coupons-confirm-actions {
  display: flex;
  gap: 0.5rem;
}

.coupons-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.coupons-filters {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-block: 1rem;
}

.coupons-filters input {
  flex: 1 1 16rem;
  min-width: 0;
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  font: inherit;
}

.coupons-statuses {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-block-end: 1.5rem;
  border: 1px solid var(--color-border);
  background: var(--color-surface);
  border-radius: 0.5rem;
  padding: 0.75rem;
}

.coupons-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.coupons-action--danger {
  border-color: var(--color-danger);
  color: var(--color-danger);
}

.coupons-action--danger:hover,
.coupons-action--danger:focus-visible {
  background: var(--color-danger-bg);
}

.coupons-sort-button {
  padding: 0;
  border: 0;
  background: transparent;
  color: inherit;
  font: inherit;
  font-weight: inherit;
  cursor: pointer;
}

.coupons-sort-button:hover,
.coupons-sort-button:focus-visible {
  color: var(--color-primary);
  text-decoration: underline;
}

.coupons-rule {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 1px;
  padding: 1px;
  background: var(--color-border);
  margin: 0;
}

.coupons-rule dt {
  font-weight: 600;
  padding: .65rem 1rem;
  background: var(--color-section);
}

.coupons-rule dd {
  margin: 0;
  padding: .65rem 1rem;
  background: var(--color-surface);
}

.coupons-error {
  color: #b91c1c;
}

.coupons-form {
  display: grid;
  gap: 0.75rem;
  margin: 0;
  border: 0;
  background: var(--color-surface);
  border-radius: 0.5rem;
  padding: 1rem;
  max-width: none;
}

.coupons-modal {
  width: min(48rem, calc(100vw - 2rem));
  max-height: calc(100vh - 2rem);
  padding: 0;
  overflow-y: auto;
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius-md);
  background: var(--color-surface);
  color: var(--color-text);
  box-shadow: var(--shadow-lg);
}

.coupons-modal::backdrop {
  background: rgb(9 30 45 / 55%);
}

.coupons-form--aligned {
  gap: 1rem;
}

.coupons-field {
  display: grid;
  grid-template-columns: 11rem minmax(0, 1fr);
  align-items: center;
  gap: 0.4rem 1rem;
  font-weight: 700;
}

.coupons-control {
  width: 100%;
  min-width: 0;
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  background: var(--color-surface);
  color: var(--color-text);
  font: inherit;
  font-weight: 400;
}

.coupons-field > span {
  grid-column: 2;
  color: var(--color-text-muted);
  font-size: 0.875rem;
  font-weight: 400;
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

.coupons-table { border: 1px solid var(--color-border); border-radius: var(--radius-sm); }
.coupons-table table { min-width: 58rem; margin: 0; }
.coupons-table th, .coupons-table td { padding: .85rem 1rem; border-color: var(--color-border-line); vertical-align: top; }
.coupons-cell--nowrap,
.coupons-period__start,
.coupons-discount__segment { white-space: nowrap; }
.coupons-discount__segment { display: inline-block; }

@media (max-width: 48rem) {
  .coupons-field {
    grid-template-columns: minmax(0, 1fr);
  }

  .coupons-field > span {
    grid-column: 1;
  }
}
.coupons-table th { color: var(--color-text); }
.coupons-statuses label { display: inline-flex; align-items: center; gap: .4rem; padding: .25rem; }

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip-path: inset(50%);
}
</style>
