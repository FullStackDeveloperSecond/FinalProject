<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import {
  useAddSupportMessageMutation,
  useCancelSupportTicketMutation,
  useSupportTicketQuery,
  useUploadSupportAttachmentMutation,
} from '../../features/support/queries'
import { categoryLabels, formatDateTime, priorityLabels, statusLabels } from '../../features/support/labels'
import {
  formatFileSize,
  getChangeEventFile,
  isAcceptedAttachment,
  maxAttachmentSizeBytes,
} from '../../features/support/attachments'
import type { AttachmentChangeEvent, AttachmentFile, AttachmentInputElement } from '../../features/support/attachments'
import { apiBaseUrl } from '../../api/client'
import type { SupportAttachmentDto } from '../../features/support/types'

const route = useRoute()
const ticketId = computed(() => String(route.params.ticketId))

const { data: ticket, isPending, isError, error, refetch } = useSupportTicketQuery(ticketId)
const addMessageMutation = useAddSupportMessageMutation(ticketId)
const cancelMutation = useCancelSupportTicketMutation(ticketId)
const uploadAttachmentMutation = useUploadSupportAttachmentMutation(ticketId)

const newMessageBody = ref('')
const cancelReason = ref('')
const showCancelForm = ref(false)

const canAddMessage = computed(() => ticket.value?.availableActions.includes('addMessage') ?? false)
const canCancel = computed(() => ticket.value?.availableActions.includes('cancel') ?? false)

const attachmentInput = ref<AttachmentInputElement | null>(null)
const selectedAttachment = ref<AttachmentFile | null>(null)
const attachmentValidationError = ref('')
const uploadedAttachments = ref<SupportAttachmentDto[]>([])
const displayedAttachments = computed(() => {
  const byId = new Map<string, SupportAttachmentDto>()
  for (const attachment of [...(ticket.value?.attachments ?? []), ...uploadedAttachments.value]) {
    byId.set(attachment.publicId, attachment)
  }
  return [...byId.values()]
})

function resetAttachmentInput() {
  if (attachmentInput.value) {
    attachmentInput.value.value = ''
  }
}

function handleAttachmentChange(event: AttachmentChangeEvent) {
  const file = getChangeEventFile(event)
  uploadAttachmentMutation.reset()
  attachmentValidationError.value = ''
  selectedAttachment.value = null

  if (!file) {
    return
  }

  if (!isAcceptedAttachment(file)) {
    attachmentValidationError.value = '僅支援 PNG、JPEG 或 PDF 格式的檔案。'
    resetAttachmentInput()
    return
  }

  if (file.size > maxAttachmentSizeBytes) {
    attachmentValidationError.value = '檔案大小不可超過 10 MB。'
    resetAttachmentInput()
    return
  }

  selectedAttachment.value = file
}

async function handleUploadAttachment() {
  if (!selectedAttachment.value) {
    return
  }

  try {
    const uploaded = await uploadAttachmentMutation.mutateAsync(selectedAttachment.value)
    uploadedAttachments.value.push(uploaded)
    selectedAttachment.value = null
    resetAttachmentInput()
  }
  catch {
    // Failure is already reflected via uploadAttachmentMutation.isError/error.
  }
}

async function handleAddMessage() {
  if (!ticket.value || !newMessageBody.value.trim()) {
    return
  }

  try {
    await addMessageMutation.mutateAsync({
      body: newMessageBody.value.trim(),
      rowVersion: ticket.value.rowVersion,
    })
    newMessageBody.value = ''
  }
  catch {
    // Failure is already reflected via addMessageMutation.isError/error.
  }
}

async function handleCancel() {
  if (!ticket.value || !cancelReason.value.trim()) {
    return
  }

  try {
    await cancelMutation.mutateAsync({
      reasonCode: cancelReason.value.trim(),
      rowVersion: ticket.value.rowVersion,
    })
    showCancelForm.value = false
    cancelReason.value = ''
  }
  catch {
    // Failure is already reflected via cancelMutation.isError/error.
  }
}
</script>

<template>
  <section aria-labelledby="support-ticket-detail-title">
    <RouterLink
      class="support-ticket-detail__back"
      to="/support/tickets"
    >
      ← 返回客服案件列表
    </RouterLink>

    <LoadingState v-if="isPending" />
    <ErrorState
      v-else-if="isError"
      :title="isApiError(error) && error.status === 404 ? '找不到這個案件' : '無法載入案件'"
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
          <dt>首次人工回覆期限</dt>
          <dd>{{ formatDateTime(ticket.firstResponseDueAtUtc) }}</dd>
        </div>
        <div>
          <dt>目標結案期限</dt>
          <dd>{{ formatDateTime(ticket.resolutionDueAtUtc) }}</dd>
        </div>
      </dl>

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
          description="您送出的第一則訊息會顯示在這裡。"
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
            ]"
          >
            <p class="support-ticket-detail__message-meta">
              {{ message.senderType === 'member' ? '您' : message.senderType === 'admin' ? '客服人員' : '系統' }}
              ・{{ formatDateTime(message.sentAtUtc) }}
            </p>
            <p>{{ message.body }}</p>
          </li>
        </ul>
      </section>

      <section
        v-if="canAddMessage || displayedAttachments.length"
        class="support-ticket-detail__attachments card"
        aria-labelledby="support-ticket-attachments-title"
      >
        <h2 id="support-ticket-attachments-title">
          案件附件
        </h2>
        <p
          v-if="canAddMessage"
          class="inline-note"
        >
          僅接受 PNG、JPEG 或 PDF 檔案，單一檔案不可超過 10 MB。
        </p>

        <form
          v-if="canAddMessage"
          class="support-ticket-detail__attachment-form"
          @submit.prevent="handleUploadAttachment"
        >
          <label
            class="form-field"
            for="support-ticket-attachment-input"
          >
            <span>選擇檔案</span>
            <input
              id="support-ticket-attachment-input"
              ref="attachmentInput"
              type="file"
              accept=".png,.jpg,.jpeg,.pdf,image/png,image/jpeg,application/pdf"
              @change="handleAttachmentChange"
            >
          </label>

          <p
            v-if="attachmentValidationError"
            class="form-error"
            role="alert"
          >
            {{ attachmentValidationError }}
          </p>
          <p
            v-else-if="uploadAttachmentMutation.isError.value"
            class="form-error"
            role="alert"
          >
            {{ isApiError(uploadAttachmentMutation.error.value)
              ? uploadAttachmentMutation.error.value.message
              : '上傳失敗，請稍後再試一次。' }}
          </p>
          <p
            v-else-if="selectedAttachment"
            class="inline-note"
          >
            已選擇「{{ selectedAttachment.name }}」（{{ formatFileSize(selectedAttachment.size) }}）
          </p>

          <button
            type="submit"
            :disabled="!selectedAttachment || uploadAttachmentMutation.isPending.value"
          >
            {{ uploadAttachmentMutation.isPending.value ? '上傳中…' : '上傳附件' }}
          </button>
        </form>

        <ul
          v-if="displayedAttachments.length"
          class="support-ticket-detail__attachment-list"
        >
          <li
            v-for="attachment in displayedAttachments"
            :key="attachment.publicId"
          >
            <span class="tag">已上傳</span>
            <a
              :href="`${apiBaseUrl}/api/v1/private-attachments/${attachment.publicId}/content`"
              class="support-ticket-detail__attachment-link"
            >{{ attachment.originalFileName }}</a>
            （{{ formatFileSize(Number(attachment.fileSizeBytes)) }}・{{ formatDateTime(attachment.createdAtUtc) }}）
          </li>
        </ul>
      </section>

      <form
        v-if="canAddMessage"
        class="support-ticket-detail__reply-form"
        @submit.prevent="handleAddMessage"
      >
        <label class="form-field">
          <span>新增訊息</span>
          <textarea
            v-model="newMessageBody"
            rows="3"
            maxlength="4000"
            required
          />
        </label>
        <p
          v-if="addMessageMutation.isError.value"
          class="form-error"
          role="alert"
        >
          {{ isApiError(addMessageMutation.error.value) ? addMessageMutation.error.value.message : '送出失敗，請重新整理後再試一次。' }}
        </p>
        <button
          type="submit"
          :disabled="addMessageMutation.isPending.value || !newMessageBody.trim()"
        >
          {{ addMessageMutation.isPending.value ? '送出中…' : '送出訊息' }}
        </button>
      </form>

      <div
        v-if="canCancel"
        class="support-ticket-detail__cancel"
      >
        <button
          v-if="!showCancelForm"
          type="button"
          class="btn-danger"
          @click="showCancelForm = true"
        >
          取消這個案件
        </button>
        <form
          v-else
          @submit.prevent="handleCancel"
        >
          <label class="form-field">
            <span>取消原因</span>
            <input
              v-model="cancelReason"
              type="text"
              required
            >
          </label>
          <p
            v-if="cancelMutation.isError.value"
            class="form-error"
            role="alert"
          >
            {{ isApiError(cancelMutation.error.value) ? cancelMutation.error.value.message : '取消失敗，請重新整理後再試一次。' }}
          </p>
          <div class="support-ticket-detail__cancel-actions">
            <button
              type="submit"
              class="btn-danger-solid"
              :disabled="cancelMutation.isPending.value || !cancelReason.trim()"
            >
              {{ cancelMutation.isPending.value ? '處理中…' : '確認取消案件' }}
            </button>
            <button
              type="button"
              @click="showCancelForm = false"
            >
              返回
            </button>
          </div>
        </form>
      </div>
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

.support-ticket-detail__messages {
  margin-top: 2rem;
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

.support-ticket-detail__message--member {
  align-self: flex-end;
  background: var(--color-primary);
  color: #fff;
}

.support-ticket-detail__message--member .support-ticket-detail__message-meta {
  color: rgba(255, 255, 255, 0.85);
}

.support-ticket-detail__message-meta {
  margin: 0 0 0.25rem;
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}

.support-ticket-detail__attachments {
  margin-top: 2rem;
}

.support-ticket-detail__attachment-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-width: 32rem;
}

.support-ticket-detail__attachment-form button {
  align-self: flex-start;
}

.support-ticket-detail__attachment-list {
  list-style: none;
  margin: 1rem 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  font-size: 0.9rem;
  color: var(--color-text);
}

.support-ticket-detail__reply-form,
.support-ticket-detail__cancel form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-width: 36rem;
  margin-top: 1.5rem;
}

.support-ticket-detail__reply-form button {
  align-self: flex-start;
}

.support-ticket-detail__cancel {
  margin-top: 1.5rem;
}

.support-ticket-detail__cancel-actions {
  display: flex;
  gap: 0.75rem;
}

@media (max-width: 480px) {
  .support-ticket-detail__message {
    max-width: 100%;
  }
}
</style>
