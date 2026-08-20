<script setup lang="ts">
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive } from 'vue'
import { useRouter } from 'vue-router'
import { useCreateSupportTicketMutation } from '../../features/support/queries'
import { categoryLabels } from '../../features/support/labels'
import type { SupportTicketCategory } from '../../features/support/types'

const router = useRouter()
const mutation = useCreateSupportTicketMutation()

const form = reactive({
  category: 'order' as SupportTicketCategory,
  subject: '',
  message: '',
})

const messageLength = computed(() => form.message.length)
const canSubmit = computed(() =>
  form.subject.trim().length > 0
  && form.subject.length <= 200
  && form.message.trim().length > 0
  && form.message.length <= 4000
  && !mutation.isPending.value)

async function handleSubmit() {
  const created = await mutation.mutateAsync({
    category: form.category,
    subject: form.subject.trim(),
    message: form.message.trim(),
  })
  await router.push(`/support/tickets/${created.publicId}`)
}
</script>

<template>
  <section aria-labelledby="support-ticket-new-title">
    <h1 id="support-ticket-new-title">
      建立客服案件
    </h1>

    <form
      class="support-ticket-form"
      @submit.prevent="handleSubmit"
    >
      <label class="support-ticket-form__field">
        <span>問題分類</span>
        <select v-model="form.category">
          <option
            v-for="(label, value) in categoryLabels"
            :key="value"
            :value="value"
          >
            {{ label }}
          </option>
        </select>
      </label>

      <label class="support-ticket-form__field">
        <span>主旨（1–200 字）</span>
        <input
          v-model="form.subject"
          type="text"
          maxlength="200"
          required
        >
      </label>

      <label class="support-ticket-form__field">
        <span>問題說明（1–4000 字）</span>
        <textarea
          v-model="form.message"
          rows="6"
          maxlength="4000"
          required
        />
        <span class="support-ticket-form__counter">{{ messageLength }} / 4000</span>
      </label>

      <p
        v-if="mutation.isError.value"
        class="support-ticket-form__error"
        role="alert"
      >
        {{ isApiError(mutation.error.value) ? mutation.error.value.message : '建立失敗，請稍後再試。' }}
      </p>

      <button
        type="submit"
        :disabled="!canSubmit"
      >
        {{ mutation.isPending.value ? '送出中…' : '送出案件' }}
      </button>
    </form>
  </section>
</template>

<style scoped>
.support-ticket-form {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  max-width: 36rem;
  margin-top: 1.5rem;
}

.support-ticket-form__field {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  font-weight: 700;
}

.support-ticket-form__field select,
.support-ticket-form__field input,
.support-ticket-form__field textarea {
  font-weight: 400;
  font: inherit;
  padding: 0.5rem 0.625rem;
  border: 1px solid #d1d5db;
  border-radius: 0.375rem;
}

.support-ticket-form__counter {
  align-self: flex-end;
  font-weight: 400;
  font-size: 0.8125rem;
  color: #6b7280;
}

.support-ticket-form__error {
  color: #b91c1c;
}

.support-ticket-form button {
  align-self: flex-start;
}
</style>
