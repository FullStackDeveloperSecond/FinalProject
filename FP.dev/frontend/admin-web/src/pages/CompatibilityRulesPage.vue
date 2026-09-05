<script setup lang="ts">
import { ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { describeCompatibilityMessage } from '@doselect/web-shared/compatibility'
import { computed, reactive, ref } from 'vue'
import ConfirmDialog from '../features/compatibilityRules/components/ConfirmDialog.vue'
import { describeApiError } from '../features/shared/errorMessages'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'
import {
  useCompatibilityRuleList,
  useSetRuleActivation,
  useTestCompatibilityRules,
  useUpdateWarningSetting,
} from '../features/compatibilityRules/useCompatibilityRules'
import type { BuildItemInput, CompatibilityRuleAdminDto } from '../features/compatibilityRules/types'

function describeError(error: unknown, fallback: string): string {
  return isApiError(error) ? describeApiError(error) : fallback
}

// 組長 PR #35 round-3 review, P2-3: 相容性規則後台設計.md — "規則整體啟停只允許 SuperAdmin" — but the
// activation column's 啟用／停用 button was shown to every role that could reach this page
// (CatalogManager included), even though the backend Policy (`CompatibilityRule.ManageActivation`)
// would ultimately reject anyone else. The backend Policy is still the real gate (never trust
// front-end role checks alone); this only stops a CatalogManager from seeing a button that would
// just fail, which is confusing UX at best and an accidental invitation to try circumventing the
// Policy at worst.
const adminAuth = useAdminAuthStore()
const canManageActivation = computed(() => (adminAuth.currentUser?.roles ?? []).includes('SuperAdmin'))

const { data: ruleList, isPending, isError, error, refetch } = useCompatibilityRuleList()
const updateWarningSetting = useUpdateWarningSetting()
const setActivation = useSetRuleActivation()
const testRules = useTestCompatibilityRules()

const ruleLabels: Record<string, string> = {
  CPU_SOCKET: 'CPU 腳位',
  CHIPSET_CPU_GENERATION: '晶片組與 CPU 世代',
  RAM_GENERATION: '記憶體世代（DDR）',
  RAM_SLOT_COUNT: '記憶體插槽數',
  RAM_CAPACITY: '記憶體最大容量',
  CASE_FORM_FACTOR: '機殼與主機板尺寸',
  GPU_LENGTH: '顯卡長度／機殼淨空',
  COOLER_SOCKET: '散熱器支援腳位',
  COOLER_HEIGHT: '散熱器高度／機殼淨空',
  STORAGE_INTERFACE: '儲存裝置介面',
  PSU_CAPACITY: '電源供應器瓦數',
  PSU_CONNECTORS: '電源供應器接頭',
}

// 組長 PR #35 round-2 review, P2-8: the `<input min max>` attributes are a UI hint only — nothing
// stopped a shopper-facing consequence like this admin page from actually *submitting* a value
// outside them (typing over the max, or a value a browser's number-input UI doesn't police at
// all), and only the reason field, never the value, gated the "更新門檻" button. One shared
// validator used at every point a bounded number reaches a submit button, instead of trusting the
// backend to be the only real gate.
function isValidBoundedNumber(value: number, min: number, max: number, requireInteger = false): boolean {
  if (!Number.isFinite(value) || value < min || value > max) {
    return false
  }
  return !requireInteger || Number.isInteger(value)
}

interface WarningDraft {
  value: number
  reason: string
  /** The server value/RowVersion this draft was actually initialized or last resynced from. */
  baseValue: number
  baseRowVersion: string | null
}

const warningDrafts = reactive<Record<string, WarningDraft>>({})

/**
 * 組長 PR #35 round-3 review, P1-1: this used to only ever initialize a rule's draft on first
 * access — a refetch that brought in *another admin's* just-landed update (new value, new
 * RowVersion) never touched an already-initialized draft. `submitWarningSetting` then paired the
 * *stale* local `draft.value` with the *fresh* `rule.warningSetting.rowVersion` it read straight
 * off the refetched `ruleList` — the backend's optimistic-concurrency check only ever compares
 * RowVersions, so this looked like a perfectly legitimate update and would silently clobber the
 * other admin's change (e.g. their 25 back down to this admin's stale local 20).
 *
 * A draft now remembers which server value/RowVersion it was actually based on:
 * - If the draft is still "clean" (untouched: value === baseValue and no reason typed) when the
 *   server's RowVersion moves on, it resyncs silently to the new server state — nothing to lose.
 * - If the draft is dirty (the admin has actually started editing) and the server's RowVersion
 *   moves on underneath it, that's a real conflict — `isWarningDraftConflicted` below flags it,
 *   the submit button disables, and the admin must explicitly reload (`reloadWarningDraft`)
 *   before they can submit again. Never silently resynced (would discard their in-progress edit)
 *   and never submitted with the stale pairing (would silently overwrite the other admin's change).
 */
function draftFor(rule: CompatibilityRuleAdminDto): WarningDraft {
  const setting = rule.warningSetting!
  const serverValue = Number(setting.value)
  const existing = warningDrafts[rule.ruleCode]
  if (!existing) {
    warningDrafts[rule.ruleCode] = { value: serverValue, reason: '', baseValue: serverValue, baseRowVersion: setting.rowVersion }
    return warningDrafts[rule.ruleCode]
  }

  const isDirty = existing.value !== existing.baseValue || existing.reason.trim().length > 0
  if (!isDirty && existing.baseRowVersion !== setting.rowVersion) {
    warningDrafts[rule.ruleCode] = { value: serverValue, reason: '', baseValue: serverValue, baseRowVersion: setting.rowVersion }
  }
  return warningDrafts[rule.ruleCode]
}

function isWarningDraftConflicted(rule: CompatibilityRuleAdminDto): boolean {
  const draft = warningDrafts[rule.ruleCode]
  if (!draft || !rule.warningSetting) {
    return false
  }
  const isDirty = draft.value !== draft.baseValue || draft.reason.trim().length > 0
  return isDirty && draft.baseRowVersion !== rule.warningSetting.rowVersion
}

/** Discards the local draft and re-initializes it fresh from the current (post-conflict) server state. */
function reloadWarningDraft(rule: CompatibilityRuleAdminDto): void {
  delete warningDrafts[rule.ruleCode]
  draftFor(rule)
}

// 組長 PR #35 round-2 review, P2-8: clearing the formal-threshold input used to write `Number('')`
// (= 0) straight into the draft, silently substituting a real value the admin never chose. Since
// 0 happens to be a *valid* value for two of the five tunable settings (RemainingRamSlotWarningCount/
// RemainingStoragePortWarningCount both allow a 0 minimum), relying on isValidBoundedNumber alone
// wouldn't have reliably caught an emptied field for those two — writing NaN for a blank field
// makes it fail Number.isFinite regardless of where 0 happens to sit in that rule's own range.
function updateWarningDraftValue(rule: CompatibilityRuleAdminDto, rawValue: string): void {
  draftFor(rule).value = rawValue.trim() === '' ? Number.NaN : Number(rawValue)
}

function isWarningDraftValid(rule: CompatibilityRuleAdminDto): boolean {
  if (!rule.warningSetting) {
    return false
  }
  const draft = draftFor(rule)
  return isValidBoundedNumber(draft.value, Number(rule.warningSetting.minValue), Number(rule.warningSetting.maxValue))
}

const warningError = ref<Record<string, unknown>>({})

// DEC-BATCH-026 (DEC-P309): concurrency moved from the whole-ruleset `settingsVersion` (still
// shown below as a reporting/generation label, no longer submitted) to a per-(rule,setting)
// RowVersion — each write must send the specific row's own RowVersion it read, not a global one.
async function submitWarningSetting(rule: CompatibilityRuleAdminDto): Promise<void> {
  const draft = warningDrafts[rule.ruleCode]
  if (!rule.warningSetting || !draft || draft.reason.trim().length === 0
    || !isWarningDraftValid(rule) || isWarningDraftConflicted(rule)) {
    return
  }

  warningError.value = { ...warningError.value, [rule.ruleCode]: null }
  try {
    await updateWarningSetting.mutateAsync({
      ruleCode: rule.ruleCode,
      request: { value: draft.value, rowVersion: rule.warningSetting.rowVersion, reason: draft.reason.trim() },
    })
    // 組長 PR #35 round-3 review: reset the local baseline immediately on success rather than
    // just clearing `reason` — the refetch this mutation triggers (see useUpdateWarningSetting's
    // invalidateQueries) will re-initialize a fresh, clean draft from the confirmed server state,
    // instead of trusting this optimistic guess to stay in sync.
    delete warningDrafts[rule.ruleCode]
  } catch (submitError) {
    warningError.value = { ...warningError.value, [rule.ruleCode]: submitError }
  }
}

const activationDialog = ref<{ ruleCode: string, targetIsActive: boolean, activationRowVersion: string | null } | null>(null)
const activationError = ref<unknown>(null)

function openActivationDialog(ruleCode: string, targetIsActive: boolean, activationRowVersion: string | null): void {
  if (!canManageActivation.value) {
    return
  }
  activationDialog.value = { ruleCode, targetIsActive, activationRowVersion }
  activationError.value = null
}

async function confirmActivation(reason: string): Promise<void> {
  if (!activationDialog.value) {
    return
  }
  const { ruleCode, targetIsActive, activationRowVersion } = activationDialog.value
  try {
    await setActivation.mutateAsync({
      ruleCode,
      request: { isActive: targetIsActive, rowVersion: activationRowVersion, reason },
    })
    activationDialog.value = null
  } catch (submitError) {
    activationError.value = submitError
  }
}

// 相容性檢查測試工具：目前沒有 Catalog API 搜尋選擇器（僅 customer-web 的自由組裝流程本輪換成
// 正式選擇器，見 BuildItemsEditor.vue）——這個管理端測試工具刻意保留手動輸入 SKU PublicId，因為
// 用途是「已知特定 SKU，驗證規則邏輯」而非「挑選商品」，跟自由組裝的挑選情境不同，暫不視為本輪
// P1 finding 的一部分（僅該 finding 明確指的是自由組裝流程）。
const testItems = ref<BuildItemInput[]>([])
const testDraftSku = reactive({ skuPublicId: '', quantity: 1 })
const testUseDraftSettings = ref(false)
const testDraftWarningSettings = reactive<Record<string, number>>({})
const testSelectedRuleCodes = ref<string[]>([])

// 組長 PR #35 round-2 review, P2-8: the quantity `<input min="1" max="8">` never actually stopped
// a value outside that range (or a non-integer) from reaching this handler and being pushed
// straight into testItems.
//
// Self-review finding (送出前文件核對): the 1–8 quantity bound was enforced here, but the
// documented 1–20 *item count* ceiling (相容性規則後台設計.md「items 1～20 筆」, and the same bound
// customer-web already enforces in features/builds/types.ts) was not — an admin could keep adding
// rows past 20 and only find out when the backend rejected the whole test run.
const MAX_TEST_ITEMS = 20
const MAX_TEST_ITEM_QUANTITY = 8
const isTestQuantityValid = computed(() => isValidBoundedNumber(testDraftSku.quantity, 1, MAX_TEST_ITEM_QUANTITY, true))
const isTestItemCountValid = computed(() => testItems.value.length <= MAX_TEST_ITEMS)

// 組長 PR #35 round-7 review: GUID 不分大小寫，但這裡原本用原始字串 === 比對——同一顆 SKU 用小寫
// 貼一次、大寫貼一次，畫面上會被當成兩個不同項目，各自都符合 1–8 卻在送出後被後端的
// EfCompatibilityCheckService.MergeAndValidateItems 合併成同一列，可能超過每項上限而整批被拒。
// normalizeSkuPublicId 把新增時要儲存的值正規化成小寫（對齊 .NET Guid 預設的 ToString() 格式），
// 讓畫面顯示與送出後端的 items 使用同一個 canonical 字串；比對時仍額外對兩邊都轉小寫，不假設
// testItems 裡的既有資料一定已經是正規化過的（防禦寫法，不依賴這支函式是唯一入口這個隱性前提）。
function normalizeSkuPublicId(value: string): string {
  return value.trim().toLowerCase()
}

const existingTestItemIndex = computed(() => testItems.value.findIndex(
  (item) => item.skuPublicId.toLowerCase() === normalizeSkuPublicId(testDraftSku.skuPublicId),
))

// 組長 PR #35 round-6 review, P2-3: adding the same SKU twice (e.g. 5 + 5, each individually
// within 1–8) used to create two separate rows — but the backend merges test items by SkuPublicId
// *before* validating (the same EfCompatibilityCheckService.MergeAndValidateItems that
// customer-web's BuildItemsEditor.vue already mirrors for the shopper-facing editor), so a merged
// 10 rejects the whole test run. Merging here instead of appending a duplicate row keeps the
// displayed list showing exactly what the backend will evaluate, and lets the 1–8 bound be checked
// against the post-merge total rather than each entry in isolation.
const mergedTestQuantity = computed(() => {
  const existingIndex = existingTestItemIndex.value
  const existingQuantity = existingIndex === -1 ? 0 : Number(testItems.value[existingIndex]!.quantity)
  return existingQuantity + testDraftSku.quantity
})
const isMergedTestQuantityValid = computed(() => mergedTestQuantity.value <= MAX_TEST_ITEM_QUANTITY)

const isAddTestItemValid = computed(() =>
  testDraftSku.skuPublicId.trim().length > 0
  && isTestQuantityValid.value
  && isMergedTestQuantityValid.value
  // Merging into an existing row never grows the list, so the 20-row ceiling only applies to a
  // genuinely new SKU.
  && (existingTestItemIndex.value !== -1 || testItems.value.length < MAX_TEST_ITEMS))

function addTestItem(): void {
  if (!isAddTestItemValid.value) {
    return
  }
  const skuPublicId = normalizeSkuPublicId(testDraftSku.skuPublicId)
  const existingIndex = existingTestItemIndex.value
  if (existingIndex === -1) {
    testItems.value = [...testItems.value, { skuPublicId, quantity: testDraftSku.quantity }]
  } else {
    const next = [...testItems.value]
    next[existingIndex] = { ...next[existingIndex]!, quantity: Number(next[existingIndex]!.quantity) + testDraftSku.quantity }
    testItems.value = next
  }
  testDraftSku.skuPublicId = ''
  testDraftSku.quantity = 1
}

function removeTestItem(index: number): void {
  testItems.value = testItems.value.filter((_, i) => i !== index)
}

/**
 * 組長 PR #35 round-3 review, P2-4: an invalid draft threshold used to be silently dropped from
 * `testDraftWarningSettings` (falling back to the rule's real value), but the `<input>` itself
 * was uncontrolled (no `:value` binding) — it kept showing whatever the admin had typed, and
 * "執行測試" stayed enabled. An admin who typed 999, saw 999 still sitting in the box, and clicked
 * "執行測試" would get a result that silently tested the *current production* threshold instead —
 * indistinguishable on screen from an override actually taking effect. `testDraftWarningInputs`
 * tracks the raw string and whether it's currently valid *per settingCode*, so the input can stay
 * controlled (reflects exactly what's being tested, not just what was last successfully typed)
 * and `isTestDraftSettingsValid` below can block "執行測試" outright. An empty field is valid — it
 * explicitly means "don't override this one", not an error.
 */
const testDraftWarningInputs = reactive<Record<string, { raw: string, isValid: boolean }>>({})

function setDraftWarningSetting(settingCode: string, rawValue: string, min: number, max: number): void {
  const trimmed = rawValue.trim()
  const parsed = Number(rawValue)
  const isValid = trimmed === '' || isValidBoundedNumber(parsed, min, max)
  testDraftWarningInputs[settingCode] = { raw: rawValue, isValid }

  if (trimmed === '' || !isValid) {
    delete testDraftWarningSettings[settingCode]
    return
  }
  testDraftWarningSettings[settingCode] = parsed
}

const isTestDraftSettingsValid = computed(() =>
  !testUseDraftSettings.value || Object.values(testDraftWarningInputs).every((input) => input.isValid),
)

const isRunTestValid = computed(() =>
  testItems.value.length > 0 && isTestItemCountValid.value && isTestDraftSettingsValid.value)

async function runTest(): Promise<void> {
  if (!isRunTestValid.value) {
    return
  }
  await testRules.mutateAsync({
    items: testItems.value,
    ruleCodes: testSelectedRuleCodes.value.length > 0 ? testSelectedRuleCodes.value : null,
    useDraftSettings: testUseDraftSettings.value,
    draftWarningSettings: testUseDraftSettings.value ? { ...testDraftWarningSettings } : null,
  })
}

const severityLabels: Record<string, string> = {
  compatible: '相容', warning: '警告', blocked: '不相容', insufficientData: '資料不足', ruleDisabled: '規則已停用',
}
</script>

<template>
  <section aria-labelledby="compatibility-rules-page-title">
    <h1 id="compatibility-rules-page-title">
      相容性規則管理
    </h1>

    <LoadingState
      v-if="isPending"
      label="規則載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />

    <template v-else-if="ruleList">
      <p class="compatibility-rules-page__version">
        目前設定版本：{{ ruleList.settingsVersion }}
      </p>

      <table class="compatibility-rules-page__table">
        <thead>
          <tr>
            <th>規則</th>
            <th>狀態</th>
            <th>警告門檻</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <template
            v-for="rule in ruleList.rules"
            :key="rule.ruleCode"
          >
            <tr>
              <td>
                {{ ruleLabels[rule.ruleCode] ?? rule.ruleCode }}
                <br>
                <code class="compatibility-rules-page__code">{{ rule.ruleCode }}</code>
              </td>
              <td>{{ rule.isActive ? '啟用中' : '已停用' }}</td>
              <td>
                <template v-if="rule.warningSetting">
                  <div class="compatibility-rules-page__warning-editor">
                    <input
                      type="number"
                      :min="rule.warningSetting.minValue"
                      :max="rule.warningSetting.maxValue"
                      :value="draftFor(rule).value"
                      aria-label="警告門檻數值"
                      @input="updateWarningDraftValue(rule, ($event.target as HTMLInputElement).value)"
                    >
                    <span class="compatibility-rules-page__range">
                      （允許範圍 {{ rule.warningSetting.minValue }}–{{ rule.warningSetting.maxValue }}，預設 {{ rule.warningSetting.defaultValue }}）
                    </span>
                    <input
                      v-model="draftFor(rule).reason"
                      type="text"
                      placeholder="調整理由（必填，寫入稽核紀錄）"
                      maxlength="500"
                      aria-label="調整理由"
                    >
                    <button
                      type="button"
                      :disabled="updateWarningSetting.isPending.value
                        || !draftFor(rule).reason.trim()
                        || !isWarningDraftValid(rule)
                        || isWarningDraftConflicted(rule)"
                      @click="submitWarningSetting(rule)"
                    >
                      更新門檻
                    </button>
                    <p
                      v-if="!isWarningDraftValid(rule)"
                      class="compatibility-rules-page__validation-error"
                    >
                      請輸入 {{ rule.warningSetting.minValue }}–{{ rule.warningSetting.maxValue }} 範圍內的數值。
                    </p>
                    <p
                      v-else-if="isWarningDraftConflicted(rule)"
                      class="compatibility-rules-page__validation-error"
                    >
                      此門檻已被其他管理員更新（目前伺服器值：{{ rule.warningSetting.value }}），請重新載入後再編輯。
                      <button
                        type="button"
                        @click="reloadWarningDraft(rule)"
                      >
                        重新載入最新值
                      </button>
                    </p>
                  </div>
                  <ErrorState
                    v-if="warningError[rule.ruleCode]"
                    :title="describeError(warningError[rule.ruleCode], '更新失敗')"
                    retry-label=""
                  />
                </template>
                <span
                  v-else
                  class="compatibility-rules-page__no-threshold"
                >
                  無可調門檻（硬性規則）
                </span>
              </td>
              <td>
                <button
                  v-if="canManageActivation"
                  type="button"
                  @click="openActivationDialog(rule.ruleCode, !rule.isActive, rule.activationRowVersion)"
                >
                  {{ rule.isActive ? '停用' : '啟用' }}
                </button>
                <span
                  v-else
                  class="compatibility-rules-page__no-threshold"
                >
                  僅 SuperAdmin 可啟用／停用
                </span>
              </td>
            </tr>
            <tr v-if="activationDialog && activationDialog.ruleCode === rule.ruleCode">
              <td colspan="4">
                <ConfirmDialog
                  :title="`確認${activationDialog.targetIsActive ? '啟用' : '停用'}規則`"
                  :resource-label="`${ruleLabels[rule.ruleCode] ?? rule.ruleCode}（${rule.ruleCode}）`"
                  :impact-label="activationDialog.targetIsActive
                    ? '啟用後，此規則會恢復正常擋下或警告不相容組合。'
                    : '停用後，此規則的檢查結果會回報「規則已停用」；受影響的組裝清單仍會在加入購物車或分享時，因需要人工確認而被擋下，並非不再擋下購買。'"
                  :current-state-label="rule.isActive ? '目前啟用中' : '目前已停用'"
                  irreversible-label="可再次切換，但切換前後已完成的訂單不會回溯檢查"
                  :pending="setActivation.isPending.value"
                  @confirm="confirmActivation"
                  @cancel="activationDialog = null"
                />
                <ErrorState
                  v-if="activationError"
                  :title="describeError(activationError, '操作失敗')"
                  retry-label=""
                />
              </td>
            </tr>
          </template>
        </tbody>
      </table>

      <section
        class="compatibility-rules-page__test"
        aria-labelledby="compatibility-rules-test-title"
      >
        <h2 id="compatibility-rules-test-title">
          測試工具（不寫入設定）
        </h2>

        <table
          v-if="testItems.length > 0"
          class="compatibility-rules-page__table compatibility-rules-page__test-items"
        >
          <thead>
            <tr>
              <th>SKU PublicId</th>
              <th>數量</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(item, index) in testItems"
              :key="index"
            >
              <td class="compatibility-rules-page__code">
                {{ item.skuPublicId }}
              </td>
              <td>{{ item.quantity }}</td>
              <td>
                <button
                  type="button"
                  @click="removeTestItem(index)"
                >
                  移除
                </button>
              </td>
            </tr>
          </tbody>
        </table>

        <div class="compatibility-rules-page__add-row">
          <input
            v-model="testDraftSku.skuPublicId"
            type="text"
            placeholder="SKU PublicId（GUID）"
            aria-label="SKU PublicId"
          >
          <input
            v-model.number="testDraftSku.quantity"
            type="number"
            min="1"
            max="8"
            aria-label="數量"
          >
          <button
            type="button"
            :disabled="!isAddTestItemValid"
            @click="addTestItem"
          >
            加入項目
          </button>
          <p
            v-if="!isTestQuantityValid"
            class="compatibility-rules-page__validation-error"
          >
            數量須為 1–8 之間的整數。
          </p>
          <p
            v-else-if="!isMergedTestQuantityValid"
            class="compatibility-rules-page__validation-error"
          >
            此 SKU 已在清單中，合併後數量為 {{ mergedTestQuantity }}，超過每項上限 8，請減少數量或先移除既有項目。
          </p>
          <p
            v-else-if="testItems.length >= 20 && existingTestItemIndex === -1"
            class="compatibility-rules-page__validation-error"
          >
            測試項目最多 20 筆，請先移除部分項目。
          </p>
        </div>

        <label class="compatibility-rules-page__checkbox">
          <input
            v-model="testUseDraftSettings"
            type="checkbox"
          >
          使用草稿門檻設定（不影響實際生效設定）
        </label>

        <fieldset class="compatibility-rules-page__rule-select">
          <legend>限定測試規則（不勾選＝測試全部規則）</legend>
          <label
            v-for="rule in ruleList.rules"
            :key="rule.ruleCode"
          >
            <input
              v-model="testSelectedRuleCodes"
              type="checkbox"
              :value="rule.ruleCode"
            >
            {{ ruleLabels[rule.ruleCode] ?? rule.ruleCode }}
          </label>
        </fieldset>

        <div
          v-if="testUseDraftSettings"
          class="compatibility-rules-page__draft-settings"
        >
          <label
            v-for="rule in ruleList.rules.filter((r) => r.warningSetting)"
            :key="rule.ruleCode"
          >
            {{ ruleLabels[rule.ruleCode] ?? rule.ruleCode }}
            <input
              type="number"
              :min="rule.warningSetting!.minValue"
              :max="rule.warningSetting!.maxValue"
              :placeholder="String(rule.warningSetting!.value)"
              :value="testDraftWarningInputs[rule.warningSetting!.settingCode]?.raw ?? ''"
              @input="setDraftWarningSetting(
                rule.warningSetting!.settingCode,
                ($event.target as HTMLInputElement).value,
                Number(rule.warningSetting!.minValue),
                Number(rule.warningSetting!.maxValue),
              )"
            >
            <span
              v-if="testDraftWarningInputs[rule.warningSetting!.settingCode]?.isValid === false"
              class="compatibility-rules-page__validation-error"
            >
              須為 {{ rule.warningSetting!.minValue }}–{{ rule.warningSetting!.maxValue }} 範圍內的數值，或留空以不覆寫。
            </span>
          </label>
        </div>

        <button
          type="button"
          :disabled="!isRunTestValid || testRules.isPending.value"
          @click="runTest"
        >
          執行測試
        </button>

        <div
          v-if="testRules.data.value"
          class="compatibility-rules-page__test-result"
        >
          <p><strong>整體結果：</strong>{{ severityLabels[testRules.data.value.overall] ?? testRules.data.value.overall }}</p>
          <ul>
            <li
              v-for="(finding, index) in testRules.data.value.results"
              :key="index"
            >
              [{{ severityLabels[finding.severity] ?? finding.severity }}] {{ finding.ruleCode }} — {{ describeCompatibilityMessage(finding.messageKey, finding.facts) }}
            </li>
          </ul>
        </div>
        <ErrorState
          v-else-if="testRules.isError.value"
          :title="describeError(testRules.error.value, '測試失敗')"
          @retry="runTest"
        />
      </section>
    </template>
  </section>
</template>

<style scoped>
.compatibility-rules-page__version {
  color: var(--color-text-muted);
  margin-block-end: 1rem;
}

.compatibility-rules-page__table {
  width: 100%;
  border-collapse: collapse;
  margin-block-end: 2rem;
}

.compatibility-rules-page__table th,
.compatibility-rules-page__table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid var(--color-border);
  text-align: left;
  vertical-align: top;
}

.compatibility-rules-page__code {
  font-family: ui-monospace, SFMono-Regular, monospace;
  font-size: 0.75rem;
  color: var(--color-text-muted);
}

.compatibility-rules-page__warning-editor {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
}

.compatibility-rules-page__warning-editor input[type='number'] {
  width: 6rem;
}

.compatibility-rules-page__warning-editor input[type='text'] {
  flex: 1 1 12rem;
}

.compatibility-rules-page__range {
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}

.compatibility-rules-page__validation-error {
  margin: 0.25rem 0 0;
  font-size: 0.8125rem;
  color: var(--color-danger);
}

.compatibility-rules-page__no-threshold {
  color: var(--color-text-muted);
  font-size: 0.875rem;
}

.compatibility-rules-page__table input {
  min-height: 2.5rem;
  padding: 0.375rem 0.625rem;
  border: 1px solid var(--color-border);
  border-radius: 0.375rem;
  font: inherit;
}

.compatibility-rules-page__test {
  padding-block-start: 1.5rem;
  border-top: 1px solid var(--color-border);
}

.compatibility-rules-page__add-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-block-end: 1rem;
}

.compatibility-rules-page__add-row input {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  font: inherit;
}

.compatibility-rules-page__checkbox {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-block-end: 1rem;
}

.compatibility-rules-page__draft-settings {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-block-end: 1rem;
}

.compatibility-rules-page__rule-select {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem 1rem;
  padding: 0.75rem;
  margin-block-end: 1rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
}

.compatibility-rules-page__rule-select legend {
  padding-inline: 0.25rem;
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}

.compatibility-rules-page__draft-settings input {
  min-height: 2.5rem;
  margin-inline-start: 0.5rem;
  padding: 0.375rem 0.625rem;
  border: 1px solid var(--color-border);
  border-radius: 0.375rem;
  font: inherit;
  width: 8rem;
}

.compatibility-rules-page__test-result {
  margin-block-start: 1rem;
  padding: 1rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  background: var(--color-surface-strong);
}

.compatibility-rules-page__test-result ul {
  margin: 0.5rem 0 0;
  padding-inline-start: 1.25rem;
}
</style>
