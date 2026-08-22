<script setup lang="ts">
import { ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { reactive, ref } from 'vue'
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

const warningDrafts = reactive<Record<string, { value: number, reason: string }>>({})

function draftFor(ruleCode: string, currentValue: number) {
  if (!warningDrafts[ruleCode]) {
    warningDrafts[ruleCode] = { value: currentValue, reason: '' }
  }
  return warningDrafts[ruleCode]
}

const warningError = ref<Record<string, unknown>>({})

async function submitWarningSetting(ruleCode: string): Promise<void> {
  if (!ruleList.value) {
    return
  }
  const draft = warningDrafts[ruleCode]
  if (!draft || draft.reason.trim().length === 0) {
    return
  }

  warningError.value = { ...warningError.value, [ruleCode]: null }
  try {
    await updateWarningSetting.mutateAsync({
      ruleCode,
      request: { value: draft.value, settingsVersion: ruleList.value.settingsVersion, reason: draft.reason.trim() },
    })
    draft.reason = ''
  } catch (submitError) {
    warningError.value = { ...warningError.value, [ruleCode]: submitError }
  }
}

const activationDialog = ref<{ ruleCode: string, targetIsActive: boolean } | null>(null)
const activationError = ref<unknown>(null)

function openActivationDialog(ruleCode: string, targetIsActive: boolean): void {
  activationDialog.value = { ruleCode, targetIsActive }
  activationError.value = null
}

async function confirmActivation(reason: string): Promise<void> {
  if (!activationDialog.value || !ruleList.value) {
    return
  }
  const { ruleCode, targetIsActive } = activationDialog.value
  try {
    await setActivation.mutateAsync({
      ruleCode,
      request: { isActive: targetIsActive, settingsVersion: ruleList.value.settingsVersion, reason },
    })
    activationDialog.value = null
  } catch (submitError) {
    activationError.value = submitError
  }
}

// 相容性檢查測試工具：目前沒有 Catalog API（catalog-frontend 尚未併入），SKU 只能手動輸入
// PublicId，等 Catalog 併入後應改為搜尋選擇器（與 customer-web 的 BuildItemsEditor 同樣的限制）。
const testItems = ref<BuildItemInput[]>([])
const testDraftSku = reactive({ skuPublicId: '', quantity: 1 })
const testUseDraftSettings = ref(false)
const testDraftWarningSettings = reactive<Record<string, number>>({})
const testSelectedRuleCodes = ref<string[]>([])

function addTestItem(): void {
  if (!testDraftSku.skuPublicId.trim()) {
    return
  }
  testItems.value = [...testItems.value, { skuPublicId: testDraftSku.skuPublicId.trim(), quantity: testDraftSku.quantity }]
  testDraftSku.skuPublicId = ''
  testDraftSku.quantity = 1
}

function removeTestItem(index: number): void {
  testItems.value = testItems.value.filter((_, i) => i !== index)
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
                      @input="draftFor(rule.ruleCode, rule.warningSetting.value).value = Number(($event.target as HTMLInputElement).value)"
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
                      :disabled="updateWarningSetting.isPending.value || !draftFor(rule.ruleCode, rule.warningSetting.value).reason.trim()"
                      @click="submitWarningSetting(rule.ruleCode)"
                    >
                      更新門檻
                    </button>
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
                  @click="openActivationDialog(rule.ruleCode, !rule.isActive)"
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
                    : '停用後，此規則的檢查結果會回報「規則已停用」，不會再擋下購買。'"
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
            max="99"
            aria-label="數量"
          >
          <button
            type="button"
            @click="addTestItem"
          >
            加入項目
          </button>
        </div>

        <label class="compatibility-rules-page__checkbox">
          <input
            v-model="testUseDraftSettings"
            type="checkbox"
          >
          使用草稿門檻設定（不影響實際生效設定）
        </label>

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
              :placeholder="String(rule.warningSetting!.value)"
              @input="testDraftWarningSettings[rule.warningSetting!.settingCode] = Number(($event.target as HTMLInputElement).value)"
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
          <p><strong>整體結果：</strong>{{ severityLabels[testRules.data.value.overall] }}</p>
          <ul>
            <li
              v-for="(finding, index) in testRules.data.value.results"
              :key="index"
            >
              [{{ severityLabels[finding.severity] }}] {{ finding.ruleCode }} — {{ finding.messageKey }}
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
