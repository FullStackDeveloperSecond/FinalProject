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
  try {
    const created = await mutation.mutateAsync({
      category: form.category,
      subject: form.subject.trim(),
      message: form.message.trim(),
    })
    await router.push(`/support/tickets/${created.publicId}`)
  }
  catch {
    // Failure is already reflected via mutation.isError/error.
  }
}
</script>

<template>
  <section aria-labelledby="support-ticket-new-title">
    <h1 id="support-ticket-new-title">
      建立客服案件
    </h1>
    <p class="view-lede">
      請描述您遇到的問題，客服人員將依照您選擇的分類儘速協助處理。
    </p>

    <form
      class="support-ticket-form card"
      @submit.prevent="handleSubmit"
    >
      <label class="form-field">
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

      <label class="form-field">
        <span>主旨（1–200 字）</span>
        <input
          v-model="form.subject"
          type="text"
          maxlength="200"
          required
        >
      </label>

      <label class="form-field">
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
        class="form-error"
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
  gap: 0.25rem;
  max-width: 36rem;
  margin-top: 1.5rem;
}

.support-ticket-form__counter {
  align-self: flex-end;
  font-weight: 400;
  font-size: 0.8125rem;
  color: var(--color-text-muted);
  margin-top: 0.25rem;
}

.support-ticket-form button {
  align-self: flex-start;
  margin-top: 0.5rem;
}
</style>
