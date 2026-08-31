<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { SUPPORTED_LOCALES } from '../../features/members/api'
import { useProfileQuery, useUpdateProfileMutation } from '../../features/members/queries'

const profileQuery = useProfileQuery()
const updateMutation = useUpdateProfileMutation()

const isEditing = ref(false)
const form = reactive({
  displayName: '',
  phone: '',
  locale: 'zh-TW',
})
const submitError = ref<string | null>(null)

watch(profileQuery.data, (profile) => {
  if (!profile || isEditing.value) {
    return
  }
  form.displayName = profile.displayName
  form.phone = profile.phone ?? ''
  form.locale = profile.locale
}, { immediate: true })

function startEditing(): void {
  const profile = profileQuery.data.value
  if (!profile) {
    return
  }
  form.displayName = profile.displayName
  form.phone = profile.phone ?? ''
  form.locale = profile.locale
  submitError.value = null
  isEditing.value = true
}

function cancelEditing(): void {
  isEditing.value = false
  submitError.value = null
}

async function save(): Promise<void> {
  const profile = profileQuery.data.value
  if (!profile) {
    return
  }

  submitError.value = null
  try {
    await updateMutation.mutateAsync({
      displayName: form.displayName.trim(),
      phone: form.phone.trim() || null,
      locale: form.locale,
      rowVersion: profile.rowVersion,
    })
    isEditing.value = false
  } catch (error) {
    submitError.value = describeError(error)
  }
}

function describeError(error: unknown): string {
  if (isApiError(error) && error.code === 'concurrency_conflict') {
    return '會員資料已被更新，請重新整理後再試一次。'
  }
  return isApiError(error) ? error.message : '更新會員資料時發生錯誤，請稍後再試。'
}

const localeLabel = computed(() => {
  const profile = profileQuery.data.value
  if (!profile) {
    return ''
  }
  return SUPPORTED_LOCALES.find(option => option.value === profile.locale)?.label ?? profile.locale
})
</script>

<template>
  <section
    class="profile-page"
    aria-labelledby="profile-title"
  >
    <h1 id="profile-title">
      會員資料
    </h1>

    <LoadingState
      v-if="profileQuery.isPending.value"
      label="會員資料載入中"
    />
    <ErrorState
      v-else-if="profileQuery.isError.value"
      :description="describeError(profileQuery.error.value)"
      @retry="profileQuery.refetch"
    />
    <EmptyState
      v-else-if="!profileQuery.data.value"
      title="找不到會員資料"
    />

    <form
      v-else-if="isEditing"
      class="profile-page__form"
      novalidate
      @submit.prevent="save"
    >
      <div class="form-field">
        <label for="profile-display-name">顯示名稱</label>
        <input
          id="profile-display-name"
          v-model="form.displayName"
          type="text"
          required
        >
      </div>
      <div class="form-field">
        <label for="profile-phone">手機號碼（選填）</label>
        <input
          id="profile-phone"
          v-model="form.phone"
          type="tel"
        >
      </div>
      <div class="form-field">
        <label for="profile-locale">語言偏好</label>
        <select
          id="profile-locale"
          v-model="form.locale"
        >
          <option
            v-for="option in SUPPORTED_LOCALES"
            :key="option.value"
            :value="option.value"
          >
            {{ option.label }}
          </option>
        </select>
      </div>

      <p
        v-if="submitError"
        class="profile-page__error"
        role="alert"
      >
        {{ submitError }}
      </p>

      <div class="profile-page__actions">
        <button
          type="submit"
          :disabled="updateMutation.isPending.value"
        >
          {{ updateMutation.isPending.value ? '儲存中…' : '儲存' }}
        </button>
        <button
          type="button"
          :disabled="updateMutation.isPending.value"
          @click="cancelEditing"
        >
          取消
        </button>
      </div>
    </form>

    <dl
      v-else
      class="profile-page__summary"
    >
      <div class="profile-page__row">
        <dt>Email</dt>
        <dd>{{ profileQuery.data.value.emailMasked }}</dd>
      </div>
      <div class="profile-page__row">
        <dt>顯示名稱</dt>
        <dd>{{ profileQuery.data.value.displayName }}</dd>
      </div>
      <div class="profile-page__row">
        <dt>手機號碼</dt>
        <dd>{{ profileQuery.data.value.phone ?? '未設定' }}</dd>
      </div>
      <div class="profile-page__row">
        <dt>語言偏好</dt>
        <dd>{{ localeLabel }}</dd>
      </div>
      <button
        type="button"
        @click="startEditing"
      >
        編輯會員資料
      </button>
    </dl>
  </section>
</template>

<style scoped>
.profile-page {
  display: grid;
  gap: 1.5rem;
  max-width: 32rem;
}

.profile-page__summary {
  display: grid;
  gap: 0.75rem;
}

.profile-page__row {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  margin: 0;
}

.profile-page__row dt {
  color: #6b7280;
}

.profile-page__row dd {
  margin: 0;
  font-weight: 600;
}

.profile-page__form {
  display: grid;
  gap: 0.75rem;
}

.profile-page__actions {
  display: flex;
  gap: 0.5rem;
}

.profile-page__error {
  color: #b91c1c;
}
</style>
