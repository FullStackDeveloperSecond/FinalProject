<script setup lang="ts">
import { defineAsyncComponent, reactive, ref, watch } from 'vue'
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { useProfileQuery, useUpdateProfileMutation } from '../../features/members/queries'

const profileQuery = useProfileQuery()
const updateMutation = useUpdateProfileMutation()
const sections = [
  { key: 'orders', label: '我的訂單', component: defineAsyncComponent(() => import('../../features/orders/OrderListPage.vue')) },
  { key: 'builds', label: '我的組裝清單', component: defineAsyncComponent(() => import('../BuildListsPage.vue')) },
  { key: 'favorites', label: '我的收藏', component: defineAsyncComponent(() => import('../favorites/FavoritesPage.vue')) },
  { key: 'reviews', label: '我的評價', component: defineAsyncComponent(() => import('../reviews/MyReviewsPage.vue')) },
  { key: 'addresses', label: '收件地址', component: defineAsyncComponent(() => import('./AddressesPage.vue')) },
]
const activeSection = ref('profile')

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
      locale: 'zh-TW',
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

</script>

<template>
  <section
    class="profile-page"
    aria-labelledby="profile-title"
  >
    <h1 id="profile-title">
      會員中心
    </h1>

    <nav
      class="profile-page__navigation"
      aria-label="會員中心功能"
    >
      <button
        v-for="section in sections"
        :key="section.key"
        type="button"
        :aria-pressed="activeSection === section.key"
        :disabled="isEditing"
        @click="activeSection = section.key"
      >
        {{ section.label }}
      </button>
    </nav>

    <template v-if="activeSection !== 'profile'">
      <button
        type="button"
        class="profile-page__back"
        @click="activeSection = 'profile'"
      >
        回會員資料
      </button>
      <component :is="sections.find(section => section.key === activeSection)?.component" />
    </template>
    <div
      v-else
      class="profile-page__content"
    >
      <h2>會員資料</h2>

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
        @submit.prevent="save"
      >
        <div class="form-field">
          <label for="profile-display-name">顯示名稱</label>
          <input
            id="profile-display-name"
            v-model="form.displayName"
            type="text"
            required
            maxlength="100"
          >
        </div>
        <div class="form-field">
          <label for="profile-phone">手機號碼（選填）</label>
          <input
            id="profile-phone"
            v-model="form.phone"
            type="tel"
            minlength="6"
            maxlength="32"
          >
        </div>
        <div class="form-field">
          <span>介面語言：繁體中文</span>
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
          <dt>介面語言</dt>
          <dd>繁體中文</dd>
        </div>
        <button
          type="button"
          @click="startEditing"
        >
          編輯會員資料
        </button>
      </dl>
    </div>
  </section>
</template>

<style scoped>
.profile-page {
  display: grid;
  gap: var(--space-3);
  max-width: 64rem;
  margin-inline: auto;
}

.profile-page__navigation {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
  gap: var(--space-3);
}

.profile-page__navigation button {
  display: grid;
  gap: .35rem;
  padding: .6rem;
  border: 1px solid var(--color-border);
  border-radius: .75rem;
  color: inherit;
  text-decoration: none;
  background: var(--color-surface);
}

.profile-page__navigation button:hover,
.profile-page__navigation button:focus-visible,
.profile-page__navigation [aria-pressed="true"] {
  border-color: currentColor;
}

.profile-page__content h2 { margin-top: .5rem; }
.profile-page__back { justify-self: start; }

.profile-page__navigation span {
  color: var(--color-text-muted);
  font-size: .875rem;
}

.profile-page__summary {
  display: grid;
  gap: var(--space-3);
  max-width: 32rem;
}

.profile-page__row {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  margin: 0;
}

.profile-page__row dt {
  color: var(--color-text-muted);
}

.profile-page__row dd {
  margin: 0;
  font-weight: 600;
}

.profile-page__form {
  display: grid;
  gap: var(--space-3);
  max-width: 32rem;
}

.profile-page__actions {
  display: flex;
  gap: 0.5rem;
}

.profile-page__error {
  color: var(--color-danger);
}
</style>
