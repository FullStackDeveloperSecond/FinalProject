<script setup lang="ts">
/**
 * C-19 /orders/:orderId/returns/new
 *
 * The order detail page (C-18) that should hand off the order's eligible items and current
 * RowVersion does not exist in this codebase yet (haru's page). Until it does, this page
 * accepts those as optional query parameters (?orderRowVersion=&item=orderItemPublicId:name)
 * pre-filled by whoever links here, and otherwise lets the member fill them in manually so the
 * page is independently usable and testable now. Only the create endpoint itself is contract-
 * accurate; the "known eligible items" list is a placeholder until C-18 ships.
 */
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useCreateReturnMutation } from '../../features/returns/queries'
import { reasonLabels } from '../../features/returns/labels'
import type { CreateReturnItemLine } from '../../features/returns/types'

const route = useRoute()
const router = useRouter()
const orderId = computed(() => String(route.params.orderId))

const mutation = useCreateReturnMutation(orderId)

const orderRowVersionInput = ref(typeof route.query.orderRowVersion === 'string' ? route.query.orderRowVersion : '')
const requestReason = ref('')
const lines = reactive<Array<CreateReturnItemLine & { key: number }>>([
  { key: 0, orderItemPublicId: '', quantity: 1, reasonCode: 'Defective', description: '' },
])
let nextKey = 1

function addLine() {
  lines.push({ key: nextKey++, orderItemPublicId: '', quantity: 1, reasonCode: 'Defective', description: '' })
}

function removeLine(key: number) {
  const index = lines.findIndex((line) => line.key === key)
  if (index !== -1 && lines.length > 1) {
    lines.splice(index, 1)
  }
}

const canSubmit = computed(() =>
  orderRowVersionInput.value.trim().length > 0
  && requestReason.value.trim().length > 0
  && requestReason.value.length <= 1000
  && lines.every((line) => line.orderItemPublicId.trim().length > 0 && Number(line.quantity) > 0)
  && !mutation.isPending.value)

async function handleSubmit() {
  // RowVersion travels as a base64 string on the wire (System.Text.Json's byte[] convention) —
  // no client-side encoding needed, just forward the value as typed/pasted.
  const created = await mutation.mutateAsync({
    items: lines.map((line) => ({
      orderItemPublicId: line.orderItemPublicId,
      quantity: line.quantity,
      reasonCode: line.reasonCode,
      description: line.description,
    })),
    requestReason: requestReason.value.trim(),
    orderRowVersion: orderRowVersionInput.value.trim(),
  })
  await router.push(`/returns/${created.publicId}`)
}
</script>

<template>
  <section aria-labelledby="return-new-title">
    <h1 id="return-new-title">
      申請退貨
    </h1>
    <p>訂單 #{{ orderId }}</p>

    <form
      class="return-form"
      @submit.prevent="handleSubmit"
    >
      <fieldset
        v-for="(line, index) in lines"
        :key="line.key"
        class="return-form__item"
      >
        <legend>退貨商品 {{ index + 1 }}</legend>
        <label>
          <span>訂單品項 ID</span>
          <input
            v-model="line.orderItemPublicId"
            type="text"
            required
            placeholder="訂單明細的 orderItemPublicId"
          >
        </label>
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

      <button
        type="button"
        class="return-form__add"
        @click="addLine"
      >
        ＋ 新增退貨品項
      </button>

      <label class="return-form__field">
        <span>整體退貨說明（1–1000 字）</span>
        <textarea
          v-model="requestReason"
          rows="4"
          maxlength="1000"
          required
        />
      </label>

      <label class="return-form__field">
        <span>訂單目前版本（orderRowVersion，Base64）</span>
        <input
          v-model="orderRowVersionInput"
          type="text"
          required
          placeholder="從訂單詳情頁取得"
        >
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
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
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
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
}

.return-form__add {
  align-self: flex-start;
}

.return-form__error {
  color: #b91c1c;
}
</style>
