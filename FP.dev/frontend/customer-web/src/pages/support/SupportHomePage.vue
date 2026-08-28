<script setup lang="ts">
import { ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref } from 'vue'
import {
  useAiConsentQuery,
  useAiOrdersQuery,
  useAiUsageQuery,
  useGrantAiConsentMutation,
  useSendAiSupportMessageMutation,
  useWithdrawAiConsentMutation,
} from '../../features/aiSupport/queries'
import { useSupportTicketsQuery } from '../../features/support/queries'

const consentAccepted = ref(false)
const message = ref('')
const conversationPublicId = ref<string | null>(null)
const selectedOrderIds = ref<string[]>([])
const selectedTicketIds = ref<string[]>([])

const consentQuery = useAiConsentQuery()
const consentGranted = computed(() => consentQuery.data.value?.state === 'granted')
const usageQuery = useAiUsageQuery(() => consentGranted.value)
const ordersQuery = useAiOrdersQuery(() => consentGranted.value)
const ticketsQuery = useSupportTicketsQuery({ pageNumber: 1, pageSize: 10 })
const grantMutation = useGrantAiConsentMutation()
const withdrawMutation = useWithdrawAiConsentMutation()
const sendMutation = useSendAiSupportMessageMutation()

const remaining = computed(() => Number(
  sendMutation.data.value?.usage.remainingRequests
    ?? Math.max(0, Number(usageQuery.data.value?.requestLimit ?? 20)
      - Number(usageQuery.data.value?.usedRequests ?? 0)),
))
const sendErrorCode = computed(() => isApiError(sendMutation.error.value)
  ? sendMutation.error.value.code
  : null)
const shouldOfferHumanSupport = computed(() => [
  'ai_consent_required',
  'ai_usage_limit_exceeded',
  'ai_service_unavailable',
  'ai_budget_protection_active',
].includes(sendErrorCode.value ?? ''))

async function grantConsent() {
  if (!consentAccepted.value) return
  await grantMutation.mutateAsync({
    policyVersion: Number(consentQuery.data.value?.policyVersion),
    locale: 'zh-TW',
    accepted: true,
  })
}

async function withdrawConsent() {
  await withdrawMutation.mutateAsync()
  conversationPublicId.value = null
  sendMutation.reset()
}

async function sendMessage() {
  const content = message.value.trim()
  if (!content) return
  const answer = await sendMutation.mutateAsync({
    conversationPublicId: conversationPublicId.value,
    message: content,
    referencedOrderPublicIds: selectedOrderIds.value,
    referencedSupportTicketPublicIds: selectedTicketIds.value,
    locale: 'zh-TW',
  })
  conversationPublicId.value = answer.conversationPublicId
  message.value = ''
}

function toggleSelection(values: string[], value: string, checked: boolean) {
  if (checked && !values.includes(value) && values.length < 3) values.push(value)
  if (!checked) {
    const index = values.indexOf(value)
    if (index >= 0) values.splice(index, 1)
  }
}
</script>

<template>
  <section
    class="ai-support"
    aria-labelledby="support-home-title"
  >
    <div class="ai-support__heading">
      <div>
        <h1 id="support-home-title">
          AI 客服
        </h1>
        <p class="view-lede">
          詢問商品、訂單與退換貨規則；AI 不會替你取消訂單或執行任何操作。
        </p>
      </div>
      <RouterLink
        class="btn-link"
        to="/support/tickets"
      >
        我的客服案件
      </RouterLink>
    </div>

    <LoadingState
      v-if="consentQuery.isPending.value"
      message="正在讀取 AI 同意狀態…"
    />
    <ErrorState
      v-else-if="consentQuery.isError.value"
      title="無法讀取 AI 同意狀態"
      :description="isApiError(consentQuery.error.value) ? consentQuery.error.value.message : '請稍後再試。'"
      @retry="consentQuery.refetch()"
    />

    <article
      v-else-if="!consentGranted"
      class="card ai-support__consent"
    >
      <h2>使用外部 AI 前需要你的同意</h2>
      <p>你的提問、你主動選取的訂單摘要與客服案件公開訊息，會在後端移除個資並通過安全檢查後送往 OpenAI。內部備註、附件、姓名、電話、地址與 Email 不會提供給 AI。</p>
      <p>你可以隨時撤回同意；若不同意，仍可建立一般客服案件。</p>
      <label class="ai-support__check">
        <input
          v-model="consentAccepted"
          type="checkbox"
        >
        我已閱讀並同意上述外部 AI 處理方式
      </label>
      <div class="ai-support__actions">
        <button
          type="button"
          :disabled="!consentAccepted || grantMutation.isPending.value"
          @click="grantConsent"
        >
          {{ grantMutation.isPending.value ? '處理中…' : '同意並開始使用' }}
        </button>
        <RouterLink
          class="btn-link"
          to="/support/tickets/new"
        >
          改由人工客服協助
        </RouterLink>
      </div>
      <p
        v-if="grantMutation.isError.value"
        role="alert"
        class="form-error"
      >
        {{ isApiError(grantMutation.error.value) ? grantMutation.error.value.message : '無法保存同意，請稍後再試。' }}
      </p>
    </article>

    <template v-else>
      <div class="ai-support__status card">
        <p><strong>今日剩餘：</strong>{{ remaining }} / {{ Number(usageQuery.data.value?.requestLimit ?? 20) }} 則</p>
        <button
          type="button"
          class="ai-support__withdraw"
          :disabled="withdrawMutation.isPending.value"
          @click="withdrawConsent"
        >
          撤回 AI 同意
        </button>
      </div>

      <form
        class="card ai-support__form"
        @submit.prevent="sendMessage"
      >
        <label class="form-field">
          <span>你的問題</span>
          <textarea
            v-model="message"
            required
            minlength="1"
            maxlength="2000"
            rows="5"
            placeholder="例如：我的螢幕偶爾閃爍，可以怎麼處理？"
          />
          <small>{{ message.length }} / 2000</small>
        </label>

        <details v-if="ordersQuery.data.value?.items.length || ticketsQuery.data.value?.items.length">
          <summary>加入我的訂單或客服案件脈絡（各最多 3 筆）</summary>
          <fieldset
            v-if="ordersQuery.data.value?.items.length"
            class="ai-support__references"
          >
            <legend>訂單</legend>
            <label
              v-for="order in ordersQuery.data.value.items"
              :key="order.publicId"
            >
              <input
                type="checkbox"
                :checked="selectedOrderIds.includes(order.publicId)"
                :disabled="!selectedOrderIds.includes(order.publicId) && selectedOrderIds.length >= 3"
                @change="toggleSelection(selectedOrderIds, order.publicId, ($event.target as HTMLInputElement).checked)"
              >
              {{ order.orderNumber }}（{{ order.orderStatus }}）
            </label>
          </fieldset>
          <fieldset
            v-if="ticketsQuery.data.value?.items.length"
            class="ai-support__references"
          >
            <legend>客服案件</legend>
            <label
              v-for="ticket in ticketsQuery.data.value.items"
              :key="ticket.publicId"
            >
              <input
                type="checkbox"
                :checked="selectedTicketIds.includes(ticket.publicId)"
                :disabled="!selectedTicketIds.includes(ticket.publicId) && selectedTicketIds.length >= 3"
                @change="toggleSelection(selectedTicketIds, ticket.publicId, ($event.target as HTMLInputElement).checked)"
              >
              {{ ticket.ticketNumber }}－{{ ticket.subject }}
            </label>
          </fieldset>
        </details>

        <button
          type="submit"
          :disabled="!message.trim() || sendMutation.isPending.value || remaining <= 0"
        >
          {{ sendMutation.isPending.value ? 'AI 回答中…' : '送出問題' }}
        </button>
        <p
          v-if="sendMutation.isError.value"
          role="alert"
          class="form-error"
        >
          {{ isApiError(sendMutation.error.value) ? sendMutation.error.value.message : 'AI 暫時無法回答。' }}
        </p>
        <RouterLink
          v-if="shouldOfferHumanSupport"
          class="btn-link"
          to="/support/tickets/new"
        >
          建立人工客服案件
        </RouterLink>
      </form>

      <article
        v-if="sendMutation.data.value"
        class="card ai-support__answer"
        aria-live="polite"
      >
        <h2>AI 回答</h2>
        <p class="ai-support__answer-text">
          {{ sendMutation.data.value.answer }}
        </p>
        <section
          v-if="sendMutation.data.value.citations.length"
          aria-labelledby="ai-citations-title"
        >
          <h3 id="ai-citations-title">
            參考來源
          </h3>
          <ul>
            <li
              v-for="citation in sendMutation.data.value.citations"
              :key="`${citation.type}-${citation.resourcePublicId}-${citation.label}`"
            >
              {{ citation.label }}
            </li>
          </ul>
        </section>
        <p class="ai-support__disclaimer">
          AI 回答可能有誤；重要資訊請以訂單頁、正式政策或人工客服確認。
        </p>
      </article>
    </template>
  </section>
</template>

<style scoped>
.ai-support { display: grid; gap: 1.25rem; }
.ai-support__heading, .ai-support__actions, .ai-support__status { display: flex; align-items: center; justify-content: space-between; gap: 1rem; flex-wrap: wrap; }
.ai-support__heading h1, .ai-support__consent h2, .ai-support__answer h2 { margin: 0; }
.ai-support__consent, .ai-support__form, .ai-support__answer { display: grid; gap: 1rem; }
.ai-support__check { display: flex; align-items: flex-start; gap: .6rem; }
.ai-support__withdraw { background: transparent; color: var(--color-text-muted); border: 1px solid var(--color-border); }
.ai-support__references { display: grid; gap: .5rem; margin-top: 1rem; border: 0; padding: 0; }
.ai-support__references label { display: flex; align-items: center; gap: .5rem; }
.ai-support__answer-text { white-space: pre-wrap; line-height: 1.7; }
.ai-support__disclaimer { color: var(--color-text-muted); font-size: .9rem; }
</style>
