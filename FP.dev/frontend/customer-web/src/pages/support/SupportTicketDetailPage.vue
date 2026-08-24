<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import {
  useAddSupportMessageMutation,
  useCancelSupportTicketMutation,
  useSupportTicketQuery,
} from '../../features/support/queries'
import { categoryLabels, formatDateTime, priorityLabels, statusLabels } from '../../features/support/labels'

const route = useRoute()
const ticketId = computed(() => String(route.params.ticketId))

const { data: ticket, isPending, isError, error, refetch } = useSupportTicketQuery(ticketId)
const addMessageMutation = useAddSupportMessageMutation(ticketId)
const cancelMutation = useCancelSupportTicketMutation(ticketId)

const newMessageBody = ref('')
const cancelReason = ref('')
const showCancelForm = ref(false)

const canAddMessage = computed(() => ticket.value?.availableActions.includes('addMessage') ?? false)
const canCancel = computed(() => ticket.value?.availableActions.includes('cancel') ?? false)

async function handleAddMessage() {
  if (!ticket.value || !newMessageBody.value.trim()) {
    return
  }

  await addMessageMutation.mutateAsync({
    body: newMessageBody.value.trim(),
    rowVersion: ticket.value.rowVersion,
  })
  newMessageBody.value = ''
}

async function handleCancel() {
  if (!ticket.value || !cancelReason.value.trim()) {
    return
  }

  await cancelMutation.mutateAsync({
    reasonCode: cancelReason.value.trim(),
    rowVersion: ticket.value.rowVersion,
  })
  showCancelForm.value = false
  cancelReason.value = ''
}
</script>

<template>
  <section aria-labelledby="support-ticket-detail-title">
    <RouterLink to="/support/tickets">
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

      <dl class="support-ticket-detail__summary">
        <div>
          <dt>分類</dt>
          <dd>{{ categoryLabels[ticket.category] }}</dd>
        </div>
        <div>
          <dt>狀態</dt>
          <dd>{{ statusLabels[ticket.status] }}</dd>
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

      <form
        v-if="canAddMessage"
        class="support-ticket-detail__reply-form"
        @submit.prevent="handleAddMessage"
      >
        <label>
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
          class="support-ticket-detail__error"
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
          class="support-ticket-detail__cancel-toggle"
          @click="showCancelForm = true"
        >
          取消這個案件
        </button>
        <form
          v-else
          @submit.prevent="handleCancel"
        >
          <label>
            <span>取消原因</span>
            <input
              v-model="cancelReason"
              type="text"
              required
            >
          </label>
          <p
            v-if="cancelMutation.isError.value"
            class="support-ticket-detail__error"
            role="alert"
          >
            {{ isApiError(cancelMutation.error.value) ? cancelMutation.error.value.message : '取消失敗，請重新整理後再試一次。' }}
          </p>
          <div class="support-ticket-detail__cancel-actions">
            <button
              type="submit"
              class="support-ticket-detail__cancel-confirm"
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
.support-ticket-detail__summary {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
  gap: 1rem;
  margin-block: 1.5rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.75rem;
}

.support-ticket-detail__summary dt {
  font-size: 0.75rem;
  color: #6b7280;
}

.support-ticket-detail__summary dd {
  margin-inline-start: 0;
  font-weight: 700;
}

.support-ticket-detail__message-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.support-ticket-detail__message {
  padding: 0.75rem 1rem;
  border-radius: 0.5rem;
  background: #f9fafb;
}

.support-ticket-detail__message--member {
  background: #eff6ff;
}

.support-ticket-detail__message-meta {
  margin: 0 0 0.25rem;
  font-size: 0.8125rem;
  color: #6b7280;
}

.support-ticket-detail__reply-form,
.support-ticket-detail__cancel form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-width: 36rem;
  margin-top: 1.5rem;
}

.support-ticket-detail__reply-form label,
.support-ticket-detail__cancel label {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  font-weight: 700;
}

.support-ticket-detail__reply-form textarea,
.support-ticket-detail__cancel input {
  font-weight: 400;
  font: inherit;
  padding: 0.5rem 0.625rem;
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
}

.support-ticket-detail__reply-form button {
  align-self: flex-start;
}

.support-ticket-detail__cancel {
  margin-top: 1.5rem;
}

.support-ticket-detail__cancel-toggle {
  background: #fff;
  color: #b91c1c;
  border-color: #b91c1c;
}

.support-ticket-detail__cancel-actions {
  display: flex;
  gap: 0.75rem;
}

.support-ticket-detail__cancel-confirm {
  background: #b91c1c;
  border-color: #991b1b;
}

.support-ticket-detail__error {
  color: #b91c1c;
}
</style>
