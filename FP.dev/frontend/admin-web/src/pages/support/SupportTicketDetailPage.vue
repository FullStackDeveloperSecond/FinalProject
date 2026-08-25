<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useClaimSupportTicketMutation, useSupportTicketDetailQuery } from '../../features/support/queries'
import { categoryLabels, formatDateTime, priorityLabels, senderTypeLabels, statusLabels } from '../../features/support/labels'
import { apiBaseUrl } from '../../api/client'

const route = useRoute()
const ticketId = computed(() => String(route.params.ticketId))

const { data: ticket, isPending, isError, error, refetch } = useSupportTicketDetailQuery(ticketId)
const claimMutation = useClaimSupportTicketMutation(ticketId)

// Claim is only offered when the backend's AvailableActions list contains the exact "claim"
// token (AdminSupportTicketService.ComputeAvailableActions). This is a usability hint only —
// the server's SupportTicketHandle policy and store-level conditional check remain the
// authoritative gate regardless of what this list contains. Guarded with Array.isArray so an
// older, malformed, or partial response that omits/nulls availableActions fails closed instead
// of throwing.
const canClaim = computed(() => {
  const actions = ticket.value?.availableActions
  return Array.isArray(actions) && actions.includes('claim')
})

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
  color: #fff;
}

.support-ticket-detail__message--admin .support-ticket-detail__message-meta {
  color: rgba(255, 255, 255, 0.85);
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
