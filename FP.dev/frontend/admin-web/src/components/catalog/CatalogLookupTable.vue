<script setup lang="ts">
/**
 * Deliberately not a generic SFC (`generic="TItem, TEdit"`): vue-tsc's ref-typing for
 * generic components (`InstanceType<typeof Comp<A, B>>`) doesn't resolve reliably with
 * this toolchain version. Item/edit-state shapes are loosely typed here and cast back
 * to the concrete per-resource types in each page (BrandsPage/CategoriesPage/TagsPage),
 * which already own those types and pass in typed `makeEditState`/`makeCreateState`.
 */
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { reactive, ref } from 'vue'
import { describeApiError } from '../../features/shared/errorMessages'

export interface CatalogLookupItem {
  publicId: string
  code: string
  nameZhTw: string
  sortOrder: number | string
  isActive: boolean
  rowVersion: string
}

const props = withDefaults(defineProps<{
  items: CatalogLookupItem[] | undefined
  isPending: boolean
  isError: boolean
  error: unknown
  emptyTitle: string
  creating: boolean
  createError: unknown
  savingId: string | null
  updateError: unknown
  makeEditState: (item: CatalogLookupItem) => Record<string, unknown>
  makeCreateState: () => Record<string, unknown>
  operationsDisabled?: boolean
}>(), {
  operationsDisabled: false,
})

const emit = defineEmits<{
  retry: []
  create: [state: Record<string, unknown>]
  update: [publicId: string, rowVersion: string, state: Record<string, unknown>]
  /**
   * PR #24 review round 10 (P3): `createError`/`updateError` are owned by the parent's mutation
   * objects, which don't reset themselves just because this table moved on to editing/creating a
   * different row — a failed update on item A left `updateError` set, and reopening edit on item
   * B (still `editingId === B.publicId && updateError`) showed A's stale error underneath B's
   * form. Fired at the start of a new create/edit session so the parent can reset its mutation
   * before this table renders that session's own (so far nonexistent) error.
   */
  editStarted: []
  createStarted: []
}>()

const editingId = ref<string | null>(null)
const editState = reactive<Record<string, unknown>>({})
// PR #24 review round 7 (P1): captured once at startEdit alongside editState — submitEdit() must
// send the token read when editing began, not item.rowVersion re-read live at submit time (item
// comes from the parent's list query, which can refetch in the background — e.g. another admin
// editing the same row, or window-refocus — while this row is mid-edit, silently swapping in a
// newer token and defeating the optimistic-concurrency check).
const editRowVersion = ref<string | null>(null)

const creatingRow = ref(false)
const createState = reactive<Record<string, unknown>>({})

function startEdit(item: CatalogLookupItem) {
  if (props.operationsDisabled) {
    return
  }
  editingId.value = item.publicId
  editRowVersion.value = item.rowVersion
  Object.assign(editState, props.makeEditState(item))
  emit('editStarted')
}

function cancelEdit() {
  editingId.value = null
}

function submitEdit(item: CatalogLookupItem) {
  if (props.operationsDisabled || !editRowVersion.value) {
    return
  }
  emit('update', item.publicId, editRowVersion.value, editState)
}

function startCreate() {
  if (props.operationsDisabled) {
    return
  }
  creatingRow.value = true
  Object.assign(createState, props.makeCreateState())
  emit('createStarted')
}

function cancelCreate() {
  creatingRow.value = false
}

function submitCreate() {
  if (props.operationsDisabled) {
    return
  }
  emit('create', createState)
}

defineExpose({ closeEditRow: cancelEdit, closeCreateRow: cancelCreate })
</script>

<template>
  <div class="catalog-lookup-table">
    <div class="catalog-lookup-table__toolbar">
      <button
        v-if="!creatingRow"
        type="button"
        :disabled="operationsDisabled"
        @click="startCreate"
      >
        新增
      </button>
    </div>

    <LoadingState
      v-if="isPending"
      label="資料載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="emit('retry')"
    />
    <EmptyState
      v-else-if="items && items.length === 0 && !creatingRow"
      :title="emptyTitle"
    />
    <table
      v-else
      class="catalog-lookup-table__table"
    >
      <thead>
        <tr>
          <th>代碼</th>
          <th>名稱</th>
          <slot name="extra-headers" />
          <th>排序</th>
          <th>啟用</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-if="creatingRow">
          <td>
            <input
              v-model="createState.code as string"
              aria-label="代碼"
            >
          </td>
          <td>
            <input
              v-model="createState.nameZhTw as string"
              aria-label="名稱"
            >
          </td>
          <slot
            name="extra-fields"
            :state="createState"
          />
          <td>
            <input
              v-model.number="createState.sortOrder as number"
              type="number"
              aria-label="排序"
            >
          </td>
          <td>
            <input
              v-model="createState.isActive as boolean"
              type="checkbox"
              aria-label="啟用"
            >
          </td>
          <td>
            <button
              type="button"
              :disabled="creating || operationsDisabled"
              @click="submitCreate"
            >
              儲存
            </button>
            <button
              type="button"
              @click="cancelCreate"
            >
              取消
            </button>
          </td>
        </tr>
        <tr
          v-if="creatingRow && createError"
          class="catalog-lookup-table__row-error"
        >
          <td colspan="6">
            {{ isApiError(createError) ? describeApiError(createError) : '建立失敗' }}
          </td>
        </tr>
        <template
          v-for="item in items"
          :key="item.publicId"
        >
          <tr v-if="editingId === item.publicId">
            <td>{{ item.code }}</td>
            <td>
              <input
                v-model="editState.nameZhTw as string"
                aria-label="名稱"
              >
            </td>
            <slot
              name="extra-fields"
              :state="editState"
            />
            <td>
              <input
                v-model.number="editState.sortOrder as number"
                type="number"
                aria-label="排序"
              >
            </td>
            <td>
              <input
                v-model="editState.isActive as boolean"
                type="checkbox"
                aria-label="啟用"
              >
            </td>
            <td>
              <button
                type="button"
                :disabled="savingId === item.publicId || operationsDisabled"
                @click="submitEdit(item)"
              >
                儲存
              </button>
              <button
                type="button"
                @click="cancelEdit"
              >
                取消
              </button>
            </td>
          </tr>
          <tr v-else>
            <td>{{ item.code }}</td>
            <td>{{ item.nameZhTw }}</td>
            <slot
              name="extra-columns"
              :item="item"
            />
            <td>{{ item.sortOrder }}</td>
            <td>{{ item.isActive ? '是' : '否' }}</td>
            <td>
              <button
                type="button"
                :disabled="operationsDisabled"
                @click="startEdit(item)"
              >
                編輯
              </button>
            </td>
          </tr>
          <tr
            v-if="editingId === item.publicId && updateError"
            class="catalog-lookup-table__row-error"
          >
            <td colspan="6">
              {{ isApiError(updateError) ? describeApiError(updateError) : '更新失敗' }}
            </td>
          </tr>
        </template>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.catalog-lookup-table__toolbar {
  margin-block-end: 1rem;
}

.catalog-lookup-table__table {
  width: 100%;
  border-collapse: collapse;
}

.catalog-lookup-table__table th,
.catalog-lookup-table__table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #e5e7eb;
  text-align: left;
}

.catalog-lookup-table__table input[type='text'],
.catalog-lookup-table__table input:not([type]) {
  min-height: 2.25rem;
  padding: 0.25rem 0.5rem;
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
  font: inherit;
}

.catalog-lookup-table__row-error {
  color: #b91c1c;
  font-size: 0.875rem;
}
</style>
