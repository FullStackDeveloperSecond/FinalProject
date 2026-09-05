<script setup lang="ts">
/**
 * C-19 /orders/:orderId/returns/new
 *
 * The order detail page (C-18) that should hand off this order's eligible return items and its
 * current RowVersion does not exist in this codebase yet (haru's page). An ordinary customer
 * must never be asked to type an internal orderItemPublicId or a Base64 RowVersion by hand, so
 * this page does not render an editable form until it receives trusted handoff data.
 *
 * Trusted handoff contract (client-side navigation convention only — no new backend API):
 * the linking page navigates here with query parameters
 *   - `orderRowVersion`: the order's current RowVersion, Base64-encoded
 *   - `items`: a JSON-encoded array of `{ orderItemPublicId, skuName, maxQuantity }` describing
 *     this customer's own already-verified eligible order items
 * Until both are present and well-formed, the page shows a dependency notice, keeps the route
 * reachable for integration testing, and disables formal submission. Once C-18 exists, it is
 * C-18's responsibility to construct this query — this page does not fetch or invent order data
 * on its own.
 */
import { EmptyState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useCreateReturnMutation } from '../../features/returns/queries'
import { reasonLabels } from '../../features/returns/labels'

interface HandoffItem {
  orderItemPublicId: string
  skuName: string
  maxQuantity: number
}

interface LineState {
  key: number
  orderItemPublicId: string
  skuName: string
  quantity: number
  reasonCode: string
  description: string
}

function parseHandoffItems(raw: unknown): HandoffItem[] {
  if (typeof raw !== 'string' || raw.length === 0) {
    return []
  }

  try {
    const parsed: unknown = JSON.parse(raw)
    if (!Array.isArray(parsed)) {
      return []
    }

    return parsed.filter((entry): entry is HandoffItem =>
      typeof entry?.orderItemPublicId === 'string' && entry.orderItemPublicId.length > 0
      && typeof entry?.skuName === 'string' && entry.skuName.length > 0
      && typeof entry?.maxQuantity === 'number' && entry.maxQuantity > 0)
  }
  catch {
    return []
  }
}

const route = useRoute()
const router = useRouter()
const orderId = computed(() => String(route.params.orderId))

const mutation = useCreateReturnMutation(orderId)

const orderRowVersion = typeof route.query.orderRowVersion === 'string' && route.query.orderRowVersion.length > 0
  ? route.query.orderRowVersion
  : null
const handoffItems = parseHandoffItems(route.query.items)
const hasTrustedHandoff = orderRowVersion !== null && handoffItems.length > 0

const requestReason = ref('')
const lines = reactive<LineState[]>(handoffItems.map((item, index) => ({
  key: index,
  orderItemPublicId: item.orderItemPublicId,
  skuName: item.skuName,
  quantity: 1,
  reasonCode: 'Defective',
  description: '',
})))

function removeLine(key: number) {
  const index = lines.findIndex((line) => line.key === key)
  if (index !== -1 && lines.length > 1) {
    lines.splice(index, 1)
  }
}

const canSubmit = computed(() =>
  hasTrustedHandoff
  && requestReason.value.trim().length > 0
  && requestReason.value.length <= 1000
  && lines.length > 0
  && lines.every((line) => Number(line.quantity) > 0)
  && !mutation.isPending.value)

async function handleSubmit() {
  if (!hasTrustedHandoff || orderRowVersion === null) {
    return
  }

  const created = await mutation.mutateAsync({
    items: lines.map((line) => ({
      orderItemPublicId: line.orderItemPublicId,
      quantity: line.quantity,
      reasonCode: line.reasonCode,
      description: line.description,
    })),
    requestReason: requestReason.value.trim(),
    orderRowVersion,
  })
  await router.push(`/returns/${created.publicId}`)
}
</script>

<template>
  <section
    class="return-new"
    aria-labelledby="return-new-title"
  >
    <h1 id="return-new-title">
      申請退貨
    </h1>
    <p class="view-lede">
      訂單 #{{ orderId }}
    </p>

    <EmptyState
      v-if="!hasTrustedHandoff"
      title="等待訂單明細（C-18）整合"
      description="退貨申請目前需要從訂單明細頁面進入，以確認可退品項與訂單版本。此功能上線後，請由「我的訂單」中的訂單明細頁面點選退貨即可開始申請；您不需要也不應該自行輸入商品編號或訂單版本。"
    />

    <form
      v-else
      class="return-form card"
      @submit.prevent="handleSubmit"
    >
      <fieldset
        v-for="(line, index) in lines"
        :key="line.key"
        class="return-form__item"
      >
        <legend>退貨商品 {{ index + 1 }}</legend>
        <p class="return-form__readonly-item">
          {{ line.skuName }}
        </p>
        <label>
          <span>數量</span>
          <input
            v-model.number="line.quantity"
            type="number"
            min="1"
            required
          >
        </label>
        <label>
          <span>退貨原因</span>
          <select v-model="line.reasonCode">
            <option
              v-for="(label, code) in reasonLabels"
              :key="code"
              :value="code"
            >
              {{ label }}
            </option>
          </select>
        </label>
        <label>
          <span>說明（選填，最多 500 字）</span>
          <textarea
            v-model="line.description"
            rows="2"
            maxlength="500"
          />
        </label>
        <button
          v-if="lines.length > 1"
          type="button"
          @click="removeLine(line.key)"
        >
          移除此品項
        </button>
      </fieldset>

      <label class="return-form__field">
        <span>整體退貨說明（1–1000 字）</span>
        <textarea
          v-model="requestReason"
          rows="4"
          maxlength="1000"
          required
        />
      </label>

      <p
        v-if="mutation.isError.value"
        class="return-form__error"
        role="alert"
      >
        {{ isApiError(mutation.error.value) ? mutation.error.value.message : '送出失敗，請稍後再試。' }}
      </p>

      <button
        type="submit"
        :disabled="!canSubmit"
      >
        {{ mutation.isPending.value ? '送出中…' : '送出退貨申請' }}
      </button>
    </form>
  </section>
</template>

<style scoped>
.return-form {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  max-width: 40rem;
  margin-top: 1.5rem;
}

.return-form__item {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
}

.return-form__readonly-item {
  margin: 0;
  font-weight: 700;
}

.return-form__item label,
.return-form__field {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  font-weight: 700;
}

.return-form__item input,
.return-form__item select,
.return-form__item textarea,
.return-form__field input,
.return-form__field textarea {
  font-weight: 400;
  font: inherit;
  padding: 0.5rem 0.625rem;
  border: 1px solid var(--color-border);
  border-radius: 0.375rem;
}

.return-form__error {
  color: var(--color-danger);
}

/* --- DoSelect 視覺系統：退貨申請 --- */
.return-new > h1 {
  margin: 0 0 var(--space-2);
  font-size: var(--fs-h1);
  line-height: var(--lh-heading);
}

.return-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}

.return-form__item {
  border-radius: var(--radius-md);
  background: var(--color-surface-strong);
}

.return-form__item legend {
  font-size: var(--fs-caption);
  font-weight: 700;
  color: var(--color-text-muted);
}
</style>
