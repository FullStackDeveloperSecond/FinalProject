<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive } from 'vue'
import { useRoute } from 'vue-router'
import {
  useAddInternalNoteMutation,
  useAssignSupportTicketMutation,
  useCancelSupportTicketByAdminMutation,
  useChangeSupportTicketPriorityMutation,
  useChangeSupportTicketStatusMutation,
  useClaimSupportTicketMutation,
  useReopenSupportTicketMutation,
  useSupportTicketDetailQuery,
  useTransferSupportTicketMutation,
} from '../../features/support/queries'
import { categoryLabels, formatDateTime, priorityLabels, senderTypeLabels, statusLabels } from '../../features/support/labels'
import type { CasePriority, SupportTicketStatus } from '../../features/support/types'
import { apiBaseUrl } from '../../api/client'

const route = useRoute()
const ticketId = computed(() => String(route.params.ticketId))

const { data: ticket, isPending, isError, error, refetch } = useSupportTicketDetailQuery(ticketId)
const claimMutation = useClaimSupportTicketMutation(ticketId)
const assignMutation = useAssignSupportTicketMutation(ticketId)
const transferMutation = useTransferSupportTicketMutation(ticketId)
const changePriorityMutation = useChangeSupportTicketPriorityMutation(ticketId)
const changeStatusMutation = useChangeSupportTicketStatusMutation(ticketId)
const cancelMutation = useCancelSupportTicketByAdminMutation(ticketId)
const reopenMutation = useReopenSupportTicketMutation(ticketId)
const internalNoteMutation = useAddInternalNoteMutation(ticketId)

// Every gate below reads the backend's AvailableActions list (AdminSupportTicketService.
// ComputeAvailableActions) — a public-safe, usability-only hint computed from the ticket's own
// state and whether THIS caller can supervise. It is never the authorization source: each
// action's own Policy and the store's conditional/tracked mutation independently re-check
// eligibility server-side regardless of what this list contains. Guarded with Array.isArray so
// an older, malformed, or partial response that omits/nulls availableActions fails closed.
function hasAction(action: string): boolean {
  const actions = ticket.value?.availableActions
  return Array.isArray(actions) && actions.includes(action)
}

const canClaim = computed(() => hasAction('claim'))
const canAssign = computed(() => hasAction('assign'))
const canTransfer = computed(() => hasAction('transfer'))
const canChangePriority = computed(() => hasAction('change-priority'))
const canChangeStatus = computed(() => hasAction('change-status'))
const canCancel = computed(() => hasAction('cancel'))
const canReopen = computed(() => hasAction('reopen'))
const canAddInternalNote = computed(() => hasAction('internal-note'))

const priorityOptions: CasePriority[] = ['low', 'normal', 'high', 'urgent']
const statusOptions: SupportTicketStatus[] = [
  'inProgress',
  'waitingForCustomer',
  'waitingForInternal',
  'resolved',
  'closed',
]

const assignForm = reactive({ targetAdminPublicId: '', reason: '' })
const transferForm = reactive({ targetAdminPublicId: '', reason: '' })
const priorityForm = reactive<{ priority: CasePriority, reason: string }>({ priority: 'normal', reason: '' })
const statusForm = reactive<{ status: SupportTicketStatus, reason: string }>({ status: 'inProgress', reason: '' })
const cancelForm = reactive({ reason: '' })
const reopenForm = reactive({ reason: '' })
const internalNoteForm = reactive({ body: '' })

function isConflict(candidateError: unknown): boolean {
  return isApiError(candidateError) && candidateError.status === 409
}

function errorMessage(candidateError: unknown, conflictMessage: string): string {
  if (isConflict(candidateError)) {
    return conflictMessage
  }
  return isApiError(candidateError) ? candidateError.message : '操作失敗，請稍後再試一次。'
}

async function handleAssign() {
  if (!ticket.value || !assignForm.targetAdminPublicId || !assignForm.reason) {
    return
  }
  try {
    await assignMutation.mutateAsync({
      targetAdminPublicId: assignForm.targetAdminPublicId,
      reason: assignForm.reason,
      rowVersion: ticket.value.rowVersion,
    })
    assignForm.targetAdminPublicId = ''
    assignForm.reason = ''
  }
  catch {
    // Handled via assignMutation.isError/error below; onError already refreshed all
    // projections so the screen never keeps a stale assignee or RowVersion on a 409.
  }
}

async function handleTransfer() {
  if (!ticket.value || !transferForm.targetAdminPublicId || !transferForm.reason) {
    return
  }
  try {
    await transferMutation.mutateAsync({
      targetAdminPublicId: transferForm.targetAdminPublicId,
      reason: transferForm.reason,
      rowVersion: ticket.value.rowVersion,
    })
    transferForm.targetAdminPublicId = ''
    transferForm.reason = ''
  }
  catch {
    // See handleAssign.
  }
}

async function handleChangePriority() {
  if (!ticket.value || !priorityForm.reason) {
    return
  }
  try {
    await changePriorityMutation.mutateAsync({
      priority: priorityForm.priority,
      reason: priorityForm.reason,
      rowVersion: ticket.value.rowVersion,
    })
    priorityForm.reason = ''
  }
  catch {
    // See handleAssign.
  }
}

async function handleChangeStatus() {
  if (!ticket.value) {
    return
  }
  try {
    await changeStatusMutation.mutateAsync({
      status: statusForm.status,
      reason: statusForm.reason || undefined,
      rowVersion: ticket.value.rowVersion,
    })
    statusForm.reason = ''
  }
  catch {
    // See handleAssign.
  }
}

async function handleCancel() {
  if (!ticket.value || !cancelForm.reason) {
    return
  }
  try {
    await cancelMutation.mutateAsync({ reason: cancelForm.reason, rowVersion: ticket.value.rowVersion })
    cancelForm.reason = ''
  }
  catch {
    // See handleAssign.
  }
}

async function handleReopen() {
  if (!ticket.value || !reopenForm.reason) {
    return
  }
  try {
    await reopenMutation.mutateAsync({ reason: reopenForm.reason, rowVersion: ticket.value.rowVersion })
    reopenForm.reason = ''
  }
  catch {
    // See handleAssign.
  }
}

async function handleAddInternalNote() {
  if (!ticket.value || !internalNoteForm.body) {
    return
  }
  try {
    await internalNoteMutation.mutateAsync({ body: internalNoteForm.body, rowVersion: ticket.value.rowVersion })
    internalNoteForm.body = ''
  }
  catch {
    // See handleAssign.
  }
}

const errorTitle = computed(() => {
  if (!isApiError(error.value)) {
    return '無法載入案件'
  }

  switch (error.value.status) {
    case 401:
      return '需要登入'
    case 403:
      return '沒有權限查看這個案件'
    case 404:
      return '找不到這個案件'
    default:
      return '無法載入案件'
  }
})

const isClaimConflict = computed(() =>
  isApiError(claimMutation.error.value) && claimMutation.error.value.status === 409)

async function handleClaim() {
  if (!ticket.value) {
    return
  }

  try {
    await claimMutation.mutateAsync({ rowVersion: ticket.value.rowVersion })
  }
  catch {
    // Expected for 409 conflicts (and other claim failures): claimMutation.isError/error
    // already reflect the rejection and drive the safe UI below. Containing it here prevents
    // the rejected promise from escaping to Vue's application error handler.
  }
}
</script>

<template>
  <section aria-labelledby="support-ticket-detail-title">
    <RouterLink
      class="support-ticket-detail__back"
      to="/support"
    >
      ← 返回 SLA 佇列
    </RouterLink>

    <LoadingState v-if="isPending" />
    <ErrorState
      v-else-if="isError"
      :title="errorTitle"
      :description="isApiError(error) ? error.message : '請稍後再試一次。'"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      :trace-id="isApiError(error) ? error.traceId : undefined"
      @retry="refetch()"
    />

    <template v-else-if="ticket">
      <h1 id="support-ticket-detail-title">
        {{ ticket.ticketNumber }}｜{{ ticket.subject }}
      </h1>

      <dl class="support-ticket-detail__summary card">
        <div>
          <dt>分類</dt>
          <dd>{{ categoryLabels[ticket.category] }}</dd>
        </div>
        <div>
          <dt>狀態</dt>
          <dd>
            <span class="status-pill">{{ statusLabels[ticket.status] }}</span>
          </dd>
        </div>
        <div>
          <dt>優先度</dt>
          <dd>{{ priorityLabels[ticket.priority] }}</dd>
        </div>
        <div>
          <dt>受理人</dt>
          <dd>{{ ticket.assignee?.displayName ?? '未指派' }}</dd>
        </div>
        <div>
          <dt>SLA 狀態</dt>
          <dd>
            <span
              class="status-pill"
              :class="ticket.isOverdue ? 'status-pill--overdue' : 'status-pill--muted'"
            >
              {{ ticket.isOverdue ? '已逾時' : '未逾時' }}
            </span>
          </dd>
        </div>
        <div>
          <dt>首次人工回覆期限</dt>
          <dd>{{ formatDateTime(ticket.firstResponseDueAtUtc) }}</dd>
        </div>
        <div>
          <dt>目標結案期限</dt>
          <dd>{{ formatDateTime(ticket.resolutionDueAtUtc) }}</dd>
        </div>
        <div>
          <dt>建立時間</dt>
          <dd>{{ formatDateTime(ticket.createdAtUtc) }}</dd>
        </div>
        <div>
          <dt>最後活動時間</dt>
          <dd>{{ formatDateTime(ticket.lastActivityAtUtc) }}</dd>
        </div>
        <div>
          <dt>重開次數</dt>
          <dd>{{ ticket.reopenCount }}</dd>
        </div>
      </dl>

      <div
        v-if="canClaim"
        class="support-ticket-detail__claim"
      >
        <button
          type="button"
          :disabled="claimMutation.isPending.value"
          @click="handleClaim"
        >
          {{ claimMutation.isPending.value ? '受理中…' : '受理這個案件' }}
        </button>
        <p
          v-if="isClaimConflict"
          class="form-error"
          role="alert"
        >
          這個案件可能已被其他客服人員受理，或狀態已變更。請重新整理後再試一次。
          <button
            type="button"
            @click="refetch()"
          >
            重新整理
          </button>
        </p>
        <p
          v-else-if="claimMutation.isError.value"
          class="form-error"
          role="alert"
        >
          {{ isApiError(claimMutation.error.value) ? claimMutation.error.value.message : '受理失敗，請稍後再試一次。' }}
        </p>
      </div>

      <section
        v-if="canAssign || canTransfer || canChangePriority || canChangeStatus || canCancel || canReopen || canAddInternalNote"
        class="support-ticket-detail__actions card"
        aria-labelledby="support-ticket-actions-title"
      >
        <h2 id="support-ticket-actions-title">
          客服主管與案件操作
        </h2>

        <div
          v-if="canAssign"
          class="support-ticket-detail__action-form"
        >
          <h3>指派客服</h3>
          <label>
            目標客服 PublicId
            <input
              v-model="assignForm.targetAdminPublicId"
              type="text"
              placeholder="guid"
            >
          </label>
          <label>
            理由
            <input
              v-model="assignForm.reason"
              type="text"
            >
          </label>
          <button
            type="button"
            :disabled="assignMutation.isPending.value"
            @click="handleAssign"
          >
            {{ assignMutation.isPending.value ? '指派中…' : '指派' }}
          </button>
          <p
            v-if="assignMutation.isError.value"
            class="form-error"
            role="alert"
          >
            {{ errorMessage(assignMutation.error.value, '這個案件已被其他主管指派，畫面已更新為最新狀態，請重新確認。') }}
          </p>
        </div>

        <div
          v-if="canTransfer"
          class="support-ticket-detail__action-form"
        >
          <h3>轉派案件</h3>
          <label>
            目標客服 PublicId
            <input
              v-model="transferForm.targetAdminPublicId"
              type="text"
              placeholder="guid"
            >
          </label>
          <label>
            理由
            <input
              v-model="transferForm.reason"
              type="text"
            >
          </label>
          <button
            type="button"
            :disabled="transferMutation.isPending.value"
            @click="handleTransfer"
          >
            {{ transferMutation.isPending.value ? '轉派中…' : '轉派' }}
          </button>
          <p
            v-if="transferMutation.isError.value"
            class="form-error"
            role="alert"
          >
            {{ errorMessage(transferMutation.error.value, '這個案件的承辦人已變更，畫面已更新為最新狀態，請重新確認。') }}
          </p>
        </div>

        <div
          v-if="canChangePriority"
          class="support-ticket-detail__action-form"
        >
          <h3>調整優先度</h3>
          <label>
            優先度
            <select v-model="priorityForm.priority">
              <option
                v-for="option in priorityOptions"
                :key="option"
                :value="option"
              >
                {{ priorityLabels[option] }}
              </option>
            </select>
          </label>
          <label>
            理由
            <input
              v-model="priorityForm.reason"
              type="text"
            >
          </label>
          <button
            type="button"
            :disabled="changePriorityMutation.isPending.value"
            @click="handleChangePriority"
          >
            {{ changePriorityMutation.isPending.value ? '更新中…' : '更新優先度' }}
          </button>
          <p
            v-if="changePriorityMutation.isError.value"
            class="form-error"
            role="alert"
          >
            {{ errorMessage(changePriorityMutation.error.value, '案件狀態已變更，畫面已更新為最新狀態，請重新確認。') }}
          </p>
        </div>

        <div
          v-if="canChangeStatus"
          class="support-ticket-detail__action-form"
        >
          <h3>變更狀態</h3>
          <label>
            狀態
            <select v-model="statusForm.status">
              <option
                v-for="option in statusOptions"
                :key="option"
                :value="option"
              >
                {{ statusLabels[option] }}
              </option>
            </select>
          </label>
          <label>
            理由（選填）
            <input
              v-model="statusForm.reason"
              type="text"
            >
          </label>
          <button
            type="button"
            :disabled="changeStatusMutation.isPending.value"
            @click="handleChangeStatus"
          >
            {{ changeStatusMutation.isPending.value ? '更新中…' : '更新狀態' }}
          </button>
          <p
            v-if="changeStatusMutation.isError.value"
            class="form-error"
            role="alert"
          >
            {{ errorMessage(changeStatusMutation.error.value, '這個狀態轉換目前不允許，畫面已更新為最新狀態，請重新確認。') }}
          </p>
        </div>

        <div
          v-if="canCancel"
          class="support-ticket-detail__action-form"
        >
          <h3>取消案件</h3>
          <label>
            理由
            <input
              v-model="cancelForm.reason"
              type="text"
            >
          </label>
          <button
            type="button"
            :disabled="cancelMutation.isPending.value"
            @click="handleCancel"
          >
            {{ cancelMutation.isPending.value ? '取消中…' : '取消案件' }}
          </button>
          <p
            v-if="cancelMutation.isError.value"
            class="form-error"
            role="alert"
          >
            {{ errorMessage(cancelMutation.error.value, '這個案件目前不能取消，畫面已更新為最新狀態，請重新確認。') }}
          </p>
        </div>

        <div
          v-if="canReopen"
          class="support-ticket-detail__action-form"
        >
          <h3>重新開啟案件</h3>
          <label>
            理由
            <input
              v-model="reopenForm.reason"
              type="text"
            >
          </label>
          <button
            type="button"
            :disabled="reopenMutation.isPending.value"
            @click="handleReopen"
          >
            {{ reopenMutation.isPending.value ? '重開中…' : '重新開啟' }}
          </button>
          <p
            v-if="reopenMutation.isError.value"
            class="form-error"
            role="alert"
          >
            {{ errorMessage(reopenMutation.error.value, '這個案件目前不能重開，畫面已更新為最新狀態，請重新確認。') }}
          </p>
        </div>

        <div
          v-if="canAddInternalNote"
          class="support-ticket-detail__action-form"
        >
          <h3>新增內部備註</h3>
          <p class="support-ticket-detail__internal-note-hint">
            內部備註僅供客服人員查看，會員永遠看不到這則內容。
          </p>
          <label class="support-ticket-detail__internal-note-label">
            備註內容
            <textarea
              v-model="internalNoteForm.body"
              rows="3"
              maxlength="4000"
            />
          </label>
          <button
            type="button"
            :disabled="internalNoteMutation.isPending.value"
            @click="handleAddInternalNote"
          >
            {{ internalNoteMutation.isPending.value ? '新增中…' : '新增備註' }}
          </button>
          <p
            v-if="internalNoteMutation.isError.value"
            class="form-error"
            role="alert"
          >
            {{ errorMessage(internalNoteMutation.error.value, '案件已被其他操作變更，畫面已更新為最新狀態，請重新確認。') }}
          </p>
        </div>
      </section>

      <section
        class="support-ticket-detail__messages"
        aria-labelledby="support-ticket-messages-title"
      >
        <h2 id="support-ticket-messages-title">
          對話紀錄
        </h2>
        <EmptyState
          v-if="ticket.messages.length === 0"
          title="尚無訊息"
          description="會員或客服人員送出的第一則訊息會顯示在這裡。"
        />
        <ul
          v-else
          class="support-ticket-detail__message-list"
        >
          <li
            v-for="message in ticket.messages"
            :key="message.publicId"
            :class="[
              'support-ticket-detail__message',
              `support-ticket-detail__message--${message.senderType}`,
              { 'support-ticket-detail__message--internal': message.isInternal },
            ]"
          >
            <p class="support-ticket-detail__message-meta">
              {{ senderTypeLabels[message.senderType] }}
              ・{{ formatDateTime(message.sentAtUtc) }}
              <span
                v-if="message.isInternal"
                class="tag"
              >內部備註</span>
            </p>
            <p>{{ message.body }}</p>
          </li>
        </ul>
      </section>

      <section
        class="support-ticket-detail__attachments"
        aria-labelledby="support-ticket-attachments-title"
      >
        <h2 id="support-ticket-attachments-title">
          案件附件
        </h2>
        <EmptyState
          v-if="!ticket.attachments?.length"
          title="沒有可下載的附件"
          description="只有通過安全掃描且未刪除的附件會顯示在這裡。"
        />
        <ul
          v-else
          class="support-ticket-detail__attachment-list"
        >
          <li
            v-for="attachment in ticket.attachments"
            :key="attachment.publicId"
          >
            <a :href="`${apiBaseUrl}/api/v1/private-attachments/${attachment.publicId}/content`">
              {{ attachment.originalFileName }}
            </a>
            <span>・{{ Math.ceil(Number(attachment.fileSizeBytes) / 1024) }} KB</span>
          </li>
        </ul>
      </section>
    </template>
  </section>
</template>

<style scoped>
.support-ticket-detail__back {
  display: inline-block;
  margin-bottom: 16px;
  color: var(--color-primary-dark);
  font-weight: 600;
  text-decoration: none;
}

.support-ticket-detail__back:hover {
  text-decoration: underline;
}

.support-ticket-detail__summary {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
  gap: 1rem;
  margin-block: 1.5rem;
}

.support-ticket-detail__summary dt {
  font-size: 0.75rem;
  color: var(--color-text-muted);
}

.support-ticket-detail__summary dd {
  margin-inline-start: 0;
  margin-top: 4px;
  font-weight: 700;
}

.support-ticket-detail__claim {
  margin-bottom: 1.5rem;
}

.support-ticket-detail__actions {
  margin-block: 1.5rem;
  padding: 1rem;
}

.support-ticket-detail__action-form {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 0.75rem;
  padding-block: 0.75rem;
  border-bottom: 1px solid var(--color-border);
}

.support-ticket-detail__action-form:last-child {
  border-bottom: none;
}

.support-ticket-detail__action-form h3 {
  flex-basis: 100%;
  margin: 0 0 0.25rem;
  font-size: 0.9375rem;
}

.support-ticket-detail__action-form label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}

.support-ticket-detail__internal-note-hint {
  flex-basis: 100%;
  margin: 0 0 0.25rem;
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}

.support-ticket-detail__internal-note-label {
  flex-basis: 100%;
}

.support-ticket-detail__internal-note-label textarea {
  width: 100%;
  resize: vertical;
  font: inherit;
}

.support-ticket-detail__action-form .form-error {
  flex-basis: 100%;
  margin: 0.5rem 0 0;
}

.support-ticket-detail__claim .form-error {
  margin-top: 0.75rem;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.support-ticket-detail__messages {
  margin-top: 2rem;
}

.support-ticket-detail__attachments {
  margin-top: 2rem;
}

.support-ticket-detail__attachment-list {
  display: grid;
  gap: 0.5rem;
  padding-left: 1.25rem;
}

.support-ticket-detail__message-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  list-style: none;
  margin: 1rem 0 0;
  padding: 0;
}

.support-ticket-detail__message {
  max-width: 80%;
  padding: 0.75rem 1rem;
  border-radius: var(--radius-md);
  background: var(--color-surface);
}

.support-ticket-detail__message--admin {
  align-self: flex-end;
  background: var(--color-primary);
  color: var(--color-on-primary);
}

.support-ticket-detail__message--admin .support-ticket-detail__message-meta {
  color: var(--color-primary-soft);
}

.support-ticket-detail__message--internal {
  background: var(--color-warning-bg);
  border: 1px dashed var(--color-warning);
}

.support-ticket-detail__message-meta {
  margin: 0 0 0.25rem;
  font-size: 0.8125rem;
  color: var(--color-text-muted);
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

@media (max-width: 480px) {
  .support-ticket-detail__message {
    max-width: 100%;
  }
}
</style>
