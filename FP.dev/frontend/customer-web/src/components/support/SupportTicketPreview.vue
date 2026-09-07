<script setup lang="ts">
import { ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { useSupportTicketQuery } from '../../features/support/queries'
import { categoryLabels, formatDateTime, statusLabels } from '../../features/support/labels'
import { apiBaseUrl } from '../../api/client'

/**
 * 案件並排檢視的唯讀預覽：摘要、往來訊息與附件。
 *
 * 只讀取顧客本人可見的既有 API（`useSupportTicketQuery`），不重寫任何權限判斷，
 * 也不在此提供回覆／取消等寫入動作 —— 那些留在既有的案件詳細頁，
 * 以免把權限與 RowVersion 邏輯複製成第二份。
 * 顧客端 API 不回傳客服內部備註，因此此處也不可能顯示內部備註。
 */
const props = defineProps<{ ticketId: string }>()

const { data: ticket, isPending, isError, error, refetch } = useSupportTicketQuery(() => props.ticketId)

function senderLabel(senderType: string): string {
  if (senderType === 'member') {
    return '您'
  }
  return senderType === 'admin' ? '客服人員' : '系統'
}
</script>

<template>
  <LoadingState
    v-if="isPending"
    label="案件載入中"
  />
  <ErrorState
    v-else-if="isError"
    :description="isApiError(error) ? error.message : '請稍後再試一次。'"
    :correlation-id="isApiError(error) ? error.correlationId : undefined"
    :trace-id="isApiError(error) ? error.traceId : undefined"
    @retry="refetch()"
  />
  <div
    v-else-if="ticket"
    class="ticket-preview"
  >
    <dl class="ticket-preview__summary">
      <dt>分類</dt>
      <dd>{{ categoryLabels[ticket.category] }}</dd>
      <dt>狀態</dt>
      <dd><span class="status-pill">{{ statusLabels[ticket.status] }}</span></dd>
      <dt>建立時間</dt>
      <dd>{{ formatDateTime(ticket.createdAtUtc) }}</dd>
      <dt>最後活動</dt>
      <dd>{{ formatDateTime(ticket.lastActivityAtUtc) }}</dd>
    </dl>

    <section aria-label="往來訊息">
      <h3 class="ticket-preview__section-title">
        往來訊息（{{ ticket.messages.length }}）
      </h3>
      <p
        v-if="ticket.messages.length === 0"
        class="inline-note"
      >
        目前還沒有往來訊息。
      </p>
      <ol
        v-else
        class="ticket-preview__thread"
      >
        <li
          v-for="message in ticket.messages"
          :key="message.publicId"
          :class="['ticket-preview__message', `ticket-preview__message--${message.senderType}`]"
        >
          <p class="ticket-preview__message-meta">
            {{ senderLabel(message.senderType) }}・{{ formatDateTime(message.sentAtUtc) }}
          </p>
          <p>{{ message.body }}</p>
        </li>
      </ol>
    </section>

    <section
      v-if="(ticket.attachments ?? []).length"
      aria-label="附件"
    >
      <h3 class="ticket-preview__section-title">
        附件（{{ (ticket.attachments ?? []).length }}）
      </h3>
      <ul class="ticket-preview__attachments">
        <li
          v-for="attachment in (ticket.attachments ?? [])"
          :key="attachment.publicId"
        >
          <a :href="`${apiBaseUrl}/api/v1/private-attachments/${attachment.publicId}/content`">
            {{ attachment.originalFileName }}
          </a>
        </li>
      </ul>
    </section>

    <RouterLink
      class="btn-link"
      :to="`/support/tickets/${ticket.publicId}`"
    >
      開啟完整案件（回覆／附件／取消）
    </RouterLink>
  </div>
</template>

<style scoped>
.ticket-preview {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}

.ticket-preview__summary {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: var(--space-2) var(--space-4);
  margin: 0;
  padding: var(--space-4);
  background: var(--color-surface-strong);
  border-radius: var(--radius-md);
  font-size: var(--fs-caption);
}

.ticket-preview__summary dt {
  color: var(--color-text-muted);
}

.ticket-preview__summary dd {
  margin: 0;
  color: var(--color-text);
}

.ticket-preview__section-title {
  margin: 0 0 var(--space-3);
  font-size: var(--fs-h3);
  line-height: var(--lh-heading);
  color: var(--color-text);
}

.ticket-preview__thread {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.ticket-preview__message {
  padding: var(--space-3) var(--space-4);
  border: 1px solid var(--color-border-soft);
  border-radius: var(--radius-md);
  background: var(--color-surface);
}

.ticket-preview__message p:last-child {
  margin: 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

/* 顧客自己的訊息用主色淡底；客服用薄荷淡底；系統訊息維持中性。 */
.ticket-preview__message--member {
  background: var(--color-primary-soft);
  border-color: var(--color-success-border);
}

.ticket-preview__message--admin {
  background: var(--color-mint-soft);
  border-color: var(--color-mint-line);
}

.ticket-preview__message--system {
  background: var(--color-surface-strong);
}

.ticket-preview__message-meta {
  margin: 0 0 var(--space-1);
  font-size: var(--fs-caption);
  color: var(--color-text-muted);
}

.ticket-preview__attachments {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  font-size: var(--fs-caption);
}
</style>
