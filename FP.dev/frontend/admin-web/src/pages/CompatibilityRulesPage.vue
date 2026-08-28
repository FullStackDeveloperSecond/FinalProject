<script setup lang="ts">
import { ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref } from 'vue'
import ConfirmDialog from '../features/compatibilityRules/components/ConfirmDialog.vue'
import { describeApiError } from '../features/shared/errorMessages'
import {
  useCompatibilityRuleList,
  useSetRuleActivation,
  useTestCompatibilityRules,
  useUpdateWarningSetting,
} from '../features/compatibilityRules/useCompatibilityRules'
import type { BuildItemInput } from '../features/compatibilityRules/types'

function describeError(error: unknown, fallback: string): string {
  return isApiError(error) ? describeApiError(error) : fallback
}

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

const warningDrafts = reactive<Record<string, { value: number, reason: string }>>({})

function draftFor(ruleCode: string, currentValue: number | string) {
  if (!warningDrafts[ruleCode]) {
    warningDrafts[ruleCode] = { value: Number(currentValue), reason: '' }
  }
  return warningDrafts[ruleCode]
}

// 組長 PR #35 round-2 review, P2-8: clearing the formal-threshold input used to write `Number('')`
// (= 0) straight into the draft, silently substituting a real value the admin never chose. Since
// 0 happens to be a *valid* value for two of the five tunable settings (RemainingRamSlotWarningCount/
// RemainingStoragePortWarningCount both allow a 0 minimum), relying on isValidBoundedNumber alone
// wouldn't have reliably caught an emptied field for those two — writing NaN for a blank field
// makes it fail Number.isFinite regardless of where 0 happens to sit in that rule's own range.
function updateWarningDraftValue(ruleCode: string, currentValue: number | string, rawValue: string): void {
  draftFor(ruleCode, currentValue).value = rawValue.trim() === '' ? Number.NaN : Number(rawValue)
}

function isWarningDraftValid(rule: {
  warningSetting: { value: number | string, minValue: number | string, maxValue: number | string } | null
}, ruleCode: string): boolean {
  if (!rule.warningSetting) {
    return false
  }
  const draft = draftFor(ruleCode, rule.warningSetting.value)
  return isValidBoundedNumber(draft.value, Number(rule.warningSetting.minValue), Number(rule.warningSetting.maxValue))
}

const warningError = ref<Record<string, unknown>>({})

// DEC-BATCH-026 (DEC-P309): concurrency moved from the whole-ruleset `settingsVersion` (still
// shown below as a reporting/generation label, no longer submitted) to a per-(rule,setting)
// RowVersion — each write must send the specific row's own RowVersion it read, not a global one.
async function submitWarningSetting(ruleCode: string): Promise<void> {
  const rule = ruleList.value?.rules.find((candidate) => candidate.ruleCode === ruleCode)
  const draft = warningDrafts[ruleCode]
  if (!rule?.warningSetting || !draft || draft.reason.trim().length === 0 || !isWarningDraftValid(rule, ruleCode)) {
    return
  }

  warningError.value = { ...warningError.value, [ruleCode]: null }
  try {
    await updateWarningSetting.mutateAsync({
      ruleCode,
      request: { value: draft.value, rowVersion: rule.warningSetting.rowVersion, reason: draft.reason.trim() },
    })
    draft.reason = ''
  } catch (submitError) {
    warningError.value = { ...warningError.value, [ruleCode]: submitError }
  }
}

const activationDialog = ref<{ ruleCode: string, targetIsActive: boolean, activationRowVersion: string | null } | null>(null)
const activationError = ref<unknown>(null)

function openActivationDialog(ruleCode: string, targetIsActive: boolean, activationRowVersion: string | null): void {
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
const isTestQuantityValid = computed(() => isValidBoundedNumber(testDraftSku.quantity, 1, 8, true))
const isAddTestItemValid = computed(() => testDraftSku.skuPublicId.trim().length > 0 && isTestQuantityValid.value)

function addTestItem(): void {
  if (!isAddTestItemValid.value) {
    return
  }
  testItems.value = [...testItems.value, { skuPublicId: testDraftSku.skuPublicId.trim(), quantity: testDraftSku.quantity }]
  testDraftSku.skuPublicId = ''
  testDraftSku.quantity = 1
}

function removeTestItem(index: number): void {
  testItems.value = testItems.value.filter((_, i) => i !== index)
}

// 組長 PR #35 review, item 6 (P2), tightened in round-2 review P2-8: the raw @input handler used
// to do Number(rawValue) with no guard — clearing the field sends NaN through to the request
// body. Only accepts a finite number within the rule's own min/max (the round-2 fix: this
// previously only checked Number.isFinite, despite the comment already claiming the min/max check
// existed); anything else (including a cleared field or an out-of-range value) drops the draft
// override for that setting entirely, falling back to the rule's real current value server-side.
function setDraftWarningSetting(settingCode: string, rawValue: string, min: number, max: number): void {
  const parsed = Number(rawValue)
  if (rawValue.trim() === '' || !isValidBoundedNumber(parsed, min, max)) {
    delete testDraftWarningSettings[settingCode]
    return
  }
  testDraftWarningSettings[settingCode] = parsed
}

async function runTest(): Promise<void> {
  if (testItems.value.length === 0) {
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
                      :value="draftFor(rule.ruleCode, rule.warningSetting.value).value"
                      aria-label="警告門檻數值"
                      @input="updateWarningDraftValue(rule.ruleCode, rule.warningSetting.value, ($event.target as HTMLInputElement).value)"
                    >
                    <span class="compatibility-rules-page__range">
                      （允許範圍 {{ rule.warningSetting.minValue }}–{{ rule.warningSetting.maxValue }}，預設 {{ rule.warningSetting.defaultValue }}）
                    </span>
                    <input
                      v-model="draftFor(rule.ruleCode, rule.warningSetting.value).reason"
                      type="text"
                      placeholder="調整理由（必填，寫入稽核紀錄）"
                      maxlength="500"
                      aria-label="調整理由"
                    >
                    <button
                      type="button"
                      :disabled="updateWarningSetting.isPending.value
                        || !draftFor(rule.ruleCode, rule.warningSetting.value).reason.trim()
                        || !isWarningDraftValid(rule, rule.ruleCode)"
                      @click="submitWarningSetting(rule.ruleCode)"
                    >
                      更新門檻
                    </button>
                    <p
                      v-if="!isWarningDraftValid(rule, rule.ruleCode)"
                      class="compatibility-rules-page__validation-error"
                    >
                      請輸入 {{ rule.warningSetting.minValue }}–{{ rule.warningSetting.maxValue }} 範圍內的數值。
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
                  type="button"
                  @click="openActivationDialog(rule.ruleCode, !rule.isActive, rule.activationRowVersion)"
                >
                  {{ rule.isActive ? '停用' : '啟用' }}
                </button>
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
          class="compatibility-rules-page__table"
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
              @input="setDraftWarningSetting(
                rule.warningSetting!.settingCode,
                ($event.target as HTMLInputElement).value,
                Number(rule.warningSetting!.minValue),
                Number(rule.warningSetting!.maxValue),
              )"
            >
          </label>
        </div>

        <button
          type="button"
          :disabled="testItems.length === 0 || testRules.isPending.value"
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
              [{{ severityLabels[finding.severity] ?? finding.severity }}] {{ finding.ruleCode }} — {{ finding.messageKey }}
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
  color: #4b5563;
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
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
  vertical-align: top;
}

.compatibility-rules-page__code {
  font-family: ui-monospace, SFMono-Regular, monospace;
  font-size: 0.75rem;
  color: #4b5563;
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
  color: #4b5563;
}

.compatibility-rules-page__validation-error {
  margin: 0.25rem 0 0;
  font-size: 0.8125rem;
  color: #b91c1c;
}

.compatibility-rules-page__no-threshold {
  color: #4b5563;
  font-size: 0.875rem;
}

.compatibility-rules-page__table input {
  min-height: 2.5rem;
  padding: 0.375rem 0.625rem;
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
  font: inherit;
}

.compatibility-rules-page__test {
  padding-block-start: 1.5rem;
  border-top: 1px solid #e5e7eb;
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
  border: 1px solid #d1d5db;
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
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
}

.compatibility-rules-page__rule-select legend {
  padding-inline: 0.25rem;
  font-size: 0.8125rem;
  color: #4b5563;
}

.compatibility-rules-page__draft-settings input {
  min-height: 2.5rem;
  margin-inline-start: 0.5rem;
  padding: 0.375rem 0.625rem;
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
  font: inherit;
  width: 8rem;
}

.compatibility-rules-page__test-result {
  margin-block-start: 1rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
  background: #f9fafb;
}

.compatibility-rules-page__test-result ul {
  margin: 0.5rem 0 0;
  padding-inline-start: 1.25rem;
}
</style>
