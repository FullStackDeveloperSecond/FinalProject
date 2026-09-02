<script setup lang="ts">
/** A-09 (M功能桌面UI與Route規格.md): 分類規格範本、Option、排序與受保護 Semantic Key。 */
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref, watch } from 'vue'
import { useFullCategoryList } from '../features/categories/useCategories'
import {
  useCreateSpecificationDefinition,
  useDisableSpecificationDefinition,
  useSpecificationDefinitionList,
  useUpdateSpecificationDefinition,
} from '../features/specificationDefinitions/useSpecificationDefinitions'
import type {
  SpecificationDefinitionDto,
  SpecificationOptionInput,
} from '../features/specificationDefinitions/types'
import { describeApiError } from '../features/shared/errorMessages'

const VALUE_TYPES = ['String', 'Decimal', 'Boolean', 'Option'] as const

/**
 * 組長 PR #37 round-2 review, item 3 的同一條規則：表單元件只綁「草稿」，查詢 key 只讀「已套用」
 * 的值，搜尋送出時一次把草稿複製過去並把頁碼歸 1。這樣不會出現「新條件配舊頁碼」的查詢。
 */
const draftFilters = reactive({ categoryPublicId: '', q: '', activeOnly: false })
const appliedFilters = reactive({ categoryPublicId: '', q: '', activeOnly: false })
const pageNumber = ref(1)
const pageSize = 20

const listParams = computed(() => ({
  categoryPublicId: appliedFilters.categoryPublicId || undefined,
  q: appliedFilters.q || undefined,
  isActive: appliedFilters.activeOnly ? true : undefined,
  pageNumber: pageNumber.value,
  pageSize,
}))
const { data: result, isPending, isError, error, refetch } = useSpecificationDefinitionList(listParams)
const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

/**
 * 組長 PR #77 review item 4：在「只顯示啟用中」的最後一頁停用最後一筆，該頁就不存在了——畫面
 * 停在空白頁，而 EmptyState 又把分頁控制一起藏掉，使用者沒有任何回到有效頁面的入口。停在超出
 * 範圍的頁碼時自動退回最後一個有效頁（沒有資料就回第 1 頁）。
 */
watch([totalPages, result], () => {
  if (!result.value) {
    return
  }
  const lastPage = Math.max(1, totalPages.value)
  if (pageNumber.value > lastPage) {
    pageNumber.value = lastPage
  }
})

const { data: categoriesResult } = useFullCategoryList()
const categories = computed(() => categoriesResult.value?.items ?? [])

function search() {
  appliedFilters.categoryPublicId = draftFilters.categoryPublicId
  appliedFilters.q = draftFilters.q
  appliedFilters.activeOnly = draftFilters.activeOnly
  pageNumber.value = 1
}

function goToPage(next: number) {
  pageNumber.value = next
}

const createMutation = useCreateSpecificationDefinition()
const updateMutation = useUpdateSpecificationDefinition()
const disableMutation = useDisableSpecificationDefinition()

/**
 * 組長 PR #77 review item 3：舊版用「目前 code 是否等於某個既有 code」判斷唯讀，於是新列一打出
 * 重複代碼就立刻被鎖住、再也改不掉。改用明確的既有列標記——`isExisting` 由資料來源決定，不隨
 * 使用者輸入變動。
 */
interface OptionRow {
  code: string
  displayNameZhTw: string
  sortOrder: number
  isActive: boolean
  isExisting: boolean
}

const createForm = reactive({
  categoryPublicId: '',
  semanticKey: '',
  displayNameZhTw: '',
  valueType: 'String' as (typeof VALUE_TYPES)[number],
  unitCode: '',
  isRequired: false,
  allowsMultiple: false,
  sortOrder: 0,
  options: [] as OptionRow[],
})
const isCreating = ref(false)

const editingId = ref<string | null>(null)
const editForm = reactive({
  displayNameZhTw: '',
  isRequired: false,
  sortOrder: 0,
  options: [] as OptionRow[],
})

function startCreate() {
  isCreating.value = true
  editingId.value = null
  createForm.categoryPublicId = appliedFilters.categoryPublicId
  createForm.semanticKey = ''
  createForm.displayNameZhTw = ''
  createForm.valueType = 'String'
  createForm.unitCode = ''
  createForm.isRequired = false
  createForm.allowsMultiple = false
  createForm.sortOrder = 0
  createForm.options = []
}

function startEdit(definition: SpecificationDefinitionDto) {
  isCreating.value = false
  editingId.value = definition.publicId
  editForm.displayNameZhTw = definition.displayNameZhTw
  editForm.isRequired = definition.isRequired
  editForm.sortOrder = Number(definition.sortOrder)
  editForm.options = definition.options.map((option) => ({
    code: option.code,
    displayNameZhTw: option.displayNameZhTw,
    sortOrder: Number(option.sortOrder),
    isActive: option.isActive,
    isExisting: true,
  }))
}

function cancel() {
  isCreating.value = false
  editingId.value = null
}

function addOption(target: OptionRow[]) {
  target.push({ code: '', displayNameZhTw: '', sortOrder: target.length, isActive: true, isExisting: false })
}

function toOptionInputs(rows: OptionRow[]): SpecificationOptionInput[] {
  return rows.map((row) => ({
    code: row.code,
    displayNameZhTw: row.displayNameZhTw,
    sortOrder: row.sortOrder,
    isActive: row.isActive,
  }))
}

function submitCreate() {
  createMutation.mutate({
    categoryPublicId: createForm.categoryPublicId,
    semanticKey: createForm.semanticKey,
    displayNameZhTw: createForm.displayNameZhTw,
    valueType: createForm.valueType,
    unitCode: createForm.unitCode || null,
    isRequired: createForm.isRequired,
    allowsMultiple: createForm.valueType === 'Option' && createForm.allowsMultiple,
    sortOrder: createForm.sortOrder,
    options: createForm.valueType === 'Option' ? toOptionInputs(createForm.options) : [],
  }, { onSuccess: () => { isCreating.value = false } })
}

function submitEdit(definition: SpecificationDefinitionDto) {
  updateMutation.mutate({
    publicId: definition.publicId,
    request: {
      displayNameZhTw: editForm.displayNameZhTw,
      isRequired: editForm.isRequired,
      sortOrder: editForm.sortOrder,
      options: definition.valueType === 'Option' ? toOptionInputs(editForm.options) : [],
      rowVersion: definition.rowVersion,
    },
  }, { onSuccess: () => { editingId.value = null } })
}

function confirmDisable(definition: SpecificationDefinitionDto) {
  // 組長 PR #77 review item 5：目前沒有重新啟用端點（Endpoint 目錄只列了 disable），確認文字
  // 不可以暗示之後可以自己開回來。
  if (!globalThis.confirm(`確定要停用規格「${definition.displayNameZhTw}」（${definition.semanticKey}）嗎？停用後其選項一併停用，且目前沒有提供重新啟用的功能，需要時請新增一個規格。`)) {
    return
  }
  disableMutation.mutate({ publicId: definition.publicId, rowVersion: definition.rowVersion })
}

const mutationError = computed(() => {
  for (const mutation of [createMutation, updateMutation, disableMutation]) {
    if (isApiError(mutation.error.value)) {
      return describeApiError(mutation.error.value)
    }
  }
  return null
})
</script>

<template>
  <section aria-labelledby="specification-definitions-title">
    <h1 id="specification-definitions-title">
      分類規格範本
    </h1>

    <form
      class="spec-filters"
      aria-label="規格範本篩選"
      @submit.prevent="search"
    >
      <label>
        分類
        <select
          v-model="draftFilters.categoryPublicId"
          aria-label="分類"
        >
          <option value="">
            全部分類
          </option>
          <option
            v-for="category in categories"
            :key="category.publicId"
            :value="category.publicId"
          >
            {{ category.nameZhTw }}
          </option>
        </select>
      </label>
      <label>
        關鍵字
        <input
          v-model="draftFilters.q"
          aria-label="關鍵字"
          maxlength="160"
        >
      </label>
      <label>
        <input
          v-model="draftFilters.activeOnly"
          type="checkbox"
          aria-label="只顯示啟用中"
        >
        只顯示啟用中
      </label>
      <button type="submit">
        搜尋
      </button>
      <button
        type="button"
        @click="startCreate"
      >
        新增規格
      </button>
    </form>

    <p
      v-if="mutationError"
      class="spec-error"
      role="alert"
    >
      {{ mutationError }}
    </p>

    <form
      v-if="isCreating"
      class="spec-form"
      aria-label="新增規格"
      @submit.prevent="submitCreate"
    >
      <label>
        分類
        <select
          v-model="createForm.categoryPublicId"
          required
          aria-label="新增分類"
        >
          <option
            v-for="category in categories"
            :key="category.publicId"
            :value="category.publicId"
          >
            {{ category.nameZhTw }}
          </option>
        </select>
      </label>
      <label>
        語意鍵
        <input
          v-model="createForm.semanticKey"
          required
          maxlength="64"
          aria-label="語意鍵"
        >
      </label>
      <label>
        顯示名稱
        <input
          v-model="createForm.displayNameZhTw"
          required
          maxlength="160"
          aria-label="顯示名稱"
        >
      </label>
      <label>
        值型別
        <select
          v-model="createForm.valueType"
          aria-label="值型別"
        >
          <option
            v-for="valueType in VALUE_TYPES"
            :key="valueType"
            :value="valueType"
          >
            {{ valueType }}
          </option>
        </select>
      </label>
      <label v-if="createForm.valueType === 'Decimal'">
        單位代碼
        <input
          v-model="createForm.unitCode"
          maxlength="64"
          aria-label="單位代碼"
        >
      </label>
      <label>
        <input
          v-model="createForm.isRequired"
          type="checkbox"
          aria-label="必填"
        >
        必填
      </label>
      <label v-if="createForm.valueType === 'Option'">
        <input
          v-model="createForm.allowsMultiple"
          type="checkbox"
          aria-label="可多選"
        >
        可多選
      </label>
      <label>
        排序
        <input
          v-model.number="createForm.sortOrder"
          type="number"
          aria-label="排序"
        >
      </label>

      <fieldset v-if="createForm.valueType === 'Option'">
        <legend>選項</legend>
        <div
          v-for="(option, index) in createForm.options"
          :key="index"
          class="spec-option-row"
        >
          <input
            v-model="option.code"
            :aria-label="`選項代碼 ${index + 1}`"
            maxlength="64"
            required
          >
          <input
            v-model="option.displayNameZhTw"
            :aria-label="`選項名稱 ${index + 1}`"
            maxlength="160"
            required
          >
          <input
            v-model.number="option.sortOrder"
            type="number"
            :aria-label="`選項排序 ${index + 1}`"
          >
        </div>
        <button
          type="button"
          @click="addOption(createForm.options)"
        >
          新增選項
        </button>
      </fieldset>

      <div class="spec-form__actions">
        <button
          type="submit"
          :disabled="createMutation.isPending.value"
        >
          建立
        </button>
        <button
          type="button"
          @click="cancel"
        >
          取消
        </button>
      </div>
    </form>

    <LoadingState
      v-if="isPending"
      label="規格範本載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />
    <EmptyState
      v-else-if="(result?.items.length ?? 0) === 0"
      title="沒有符合條件的規格範本"
    />
    <template v-else>
      <table class="spec-table">
        <thead>
          <tr>
            <th>分類</th>
            <th>語意鍵</th>
            <th>顯示名稱</th>
            <th>型別</th>
            <th>單位</th>
            <th>必填</th>
            <th>多選</th>
            <th>選項</th>
            <th>排序</th>
            <th>狀態</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <template
            v-for="definition in result!.items"
            :key="definition.publicId"
          >
            <tr>
              <td>{{ definition.categoryCode }}</td>
              <td>
                {{ definition.semanticKey }}
                <span
                  v-if="definition.isProtected"
                  class="spec-badge"
                  title="相容性規則依賴此規格，不可停用或改為非必填"
                >受保護</span>
              </td>
              <td>{{ definition.displayNameZhTw }}</td>
              <td>{{ definition.valueType }}</td>
              <td>{{ definition.unitCode ?? '—' }}</td>
              <td>{{ definition.isRequired ? '是' : '否' }}</td>
              <td>{{ definition.allowsMultiple ? '是' : '否' }}</td>
              <td>{{ definition.options.length }}</td>
              <td>{{ definition.sortOrder }}</td>
              <td>{{ definition.isActive ? '啟用' : '停用' }}</td>
              <td>
                <button
                  type="button"
                  @click="startEdit(definition)"
                >
                  編輯
                </button>
                <button
                  v-if="definition.isActive"
                  type="button"
                  :disabled="definition.isProtected || disableMutation.isPending.value"
                  :title="definition.isProtected ? '相容性規則依賴此規格，不可停用' : undefined"
                  @click="confirmDisable(definition)"
                >
                  停用
                </button>
              </td>
            </tr>
            <tr v-if="editingId === definition.publicId">
              <td colspan="11">
                <form
                  class="spec-form"
                  aria-label="編輯規格"
                  @submit.prevent="submitEdit(definition)"
                >
                  <p class="spec-form__note">
                    分類、語意鍵、型別、單位與是否多選建立後不可修改（資料字典：被使用後不可改）；
                    需要改型別請新增一個規格並停用這一個。
                  </p>
                  <label>
                    顯示名稱
                    <input
                      v-model="editForm.displayNameZhTw"
                      required
                      maxlength="160"
                      aria-label="編輯顯示名稱"
                    >
                  </label>
                  <label>
                    <input
                      v-model="editForm.isRequired"
                      type="checkbox"
                      :disabled="definition.isProtected"
                      aria-label="編輯必填"
                    >
                    必填
                    <span v-if="definition.isProtected">（受保護規格必須維持必填）</span>
                  </label>
                  <label>
                    排序
                    <input
                      v-model.number="editForm.sortOrder"
                      type="number"
                      aria-label="編輯排序"
                    >
                  </label>

                  <fieldset v-if="definition.valueType === 'Option'">
                    <legend>選項</legend>
                    <p class="spec-form__note">
                      選項代碼建立後不可修改；移除選項的方式是取消勾選「啟用」，不是刪除。
                    </p>
                    <div
                      v-for="(option, index) in editForm.options"
                      :key="option.code || index"
                      class="spec-option-row"
                    >
                      <input
                        v-model="option.code"
                        :aria-label="`編輯選項代碼 ${index + 1}`"
                        maxlength="64"
                        :readonly="option.isExisting"
                        required
                      >
                      <input
                        v-model="option.displayNameZhTw"
                        :aria-label="`編輯選項名稱 ${index + 1}`"
                        maxlength="160"
                        required
                      >
                      <input
                        v-model.number="option.sortOrder"
                        type="number"
                        :aria-label="`編輯選項排序 ${index + 1}`"
                      >
                      <label>
                        <input
                          v-model="option.isActive"
                          type="checkbox"
                          :aria-label="`編輯選項啟用 ${index + 1}`"
                        >
                        啟用
                      </label>
                    </div>
                    <button
                      type="button"
                      @click="addOption(editForm.options)"
                    >
                      新增選項
                    </button>
                  </fieldset>

                  <div class="spec-form__actions">
                    <button
                      type="submit"
                      :disabled="updateMutation.isPending.value"
                    >
                      儲存
                    </button>
                    <button
                      type="button"
                      @click="cancel"
                    >
                      取消
                    </button>
                  </div>
                </form>
              </td>
            </tr>
          </template>
        </tbody>
      </table>

      <div
        v-if="totalPages > 1"
        class="spec-pagination"
      >
        <button
          type="button"
          :disabled="pageNumber <= 1"
          @click="goToPage(pageNumber - 1)"
        >
          上一頁
        </button>
        <span>{{ pageNumber }} / {{ totalPages }}</span>
        <button
          type="button"
          :disabled="pageNumber >= totalPages"
          @click="goToPage(pageNumber + 1)"
        >
          下一頁
        </button>
      </div>
    </template>
  </section>
</template>

<style scoped>
.spec-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.75rem;
  margin-block-end: 1.5rem;
}

.spec-filters label,
.spec-form label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
}

.spec-form {
  display: flex;
  flex-wrap: wrap;
  align-items: end;
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
  margin-block-end: 1.5rem;
}

.spec-form__note {
  flex-basis: 100%;
  margin: 0;
  color: #6b7280;
  font-size: 0.8125rem;
}

.spec-form__actions {
  display: flex;
  gap: 0.5rem;
}

.spec-option-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-block-end: 0.5rem;
}

.spec-table {
  width: 100%;
  border-collapse: collapse;
}

.spec-table th,
.spec-table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}

.spec-badge {
  margin-inline-start: 0.375rem;
  padding: 0.125rem 0.375rem;
  border-radius: 0.25rem;
  background: #fef3c7;
  color: #92400e;
  font-size: 0.75rem;
}

.spec-error {
  color: #b91c1c;
  margin-block-end: 1rem;
}

.spec-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  margin-block-start: 1.5rem;
}
</style>
