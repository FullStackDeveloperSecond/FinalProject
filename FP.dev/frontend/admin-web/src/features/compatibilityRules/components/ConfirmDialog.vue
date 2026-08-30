<script setup lang="ts">
/**
 * Local to this feature, not `@doselect/web-shared` — this is the first admin action needing
 * a reason-collecting confirm dialog (相容性規則後台設計.md's "二次確認" requirement for rule
 * deactivation); extract to shared only once a second feature needs the same shape
 * (M功能桌面UI與Route規格.md's "先建立大型共用 Component Package" guidance).
 */
import { ref } from 'vue'

const { title, resourceLabel, impactLabel, currentStateLabel, irreversibleLabel, pending } = defineProps<{
  title: string
  resourceLabel: string
  impactLabel: string
  currentStateLabel: string
  irreversibleLabel: string
  pending: boolean
}>()

const emit = defineEmits<{
  confirm: [reason: string]
  cancel: []
}>()

const reason = ref('')

function submit(): void {
  if (reason.value.trim().length === 0) {
    return
  }
  emit('confirm', reason.value.trim())
}

defineExpose({ reset: () => { reason.value = '' } })
</script>

<template>
  <div
    class="confirm-dialog"
    role="alertdialog"
    :aria-label="title"
  >
    <h3>{{ title }}</h3>
    <dl class="confirm-dialog__facts">
      <dt>資源</dt>
      <dd>{{ resourceLabel }}</dd>
      <dt>影響</dt>
      <dd>{{ impactLabel }}</dd>
      <dt>目前狀態</dt>
      <dd>{{ currentStateLabel }}</dd>
      <dt>可逆性</dt>
      <dd>{{ irreversibleLabel }}</dd>
    </dl>
    <label for="confirm-dialog-reason">請輸入理由（將寫入稽核紀錄）</label>
    <textarea
      id="confirm-dialog-reason"
      v-model="reason"
      maxlength="500"
      rows="3"
    />
    <div class="confirm-dialog__actions">
      <button
        type="button"
        :disabled="pending || reason.trim().length === 0"
        @click="submit"
      >
        確認
      </button>
      <button
        type="button"
        @click="emit('cancel')"
      >
        取消
      </button>
    </div>
  </div>
</template>

<style scoped>
.confirm-dialog {
  padding: 1rem;
  border: 1px solid #fca5a5;
  border-radius: 0.5rem;
  background: #fef2f2;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.confirm-dialog__facts {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.25rem 0.75rem;
  margin: 0;
}

.confirm-dialog__facts dt {
  font-weight: 700;
}

.confirm-dialog__facts dd {
  margin: 0;
}

.confirm-dialog textarea {
  padding: 0.5rem 0.75rem;
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
  font: inherit;
  resize: vertical;
}

.confirm-dialog__actions {
  display: flex;
  gap: 0.5rem;
}
</style>
