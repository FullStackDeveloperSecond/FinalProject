<script setup lang="ts">
import { ErrorState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import BuildItemsEditor, { type EditableBuildItem } from '../features/builds/components/BuildItemsEditor.vue'
import CompatibilityFindingsList from '../features/builds/components/CompatibilityFindingsList.vue'
import { useCompatibilityCheck, useCreateBuildList } from '../features/builds/useBuilds'
import { validateBuildItems } from '../features/builds/types'
import {
  clearGuestBuildDraft,
  consumePendingBuildSaveResume,
  loadGuestBuildDraft,
  markPendingBuildSaveResume,
  saveGuestBuildDraft,
} from '../features/builds/guestBuildDraft'
import { useSessionStore } from '../stores/session'

const router = useRouter()
const route = useRoute()
const sessionStore = useSessionStore()

const draft = loadGuestBuildDraft()
const name = ref(draft.name)
const items = ref<EditableBuildItem[]>(draft.items)

const compatibilityCheck = useCompatibilityCheck()
const createBuildList = useCreateBuildList()

let debounceHandle: ReturnType<typeof setTimeout> | undefined

function runCompatibilityCheck(): void {
  if (items.value.length === 0) {
    return
  }

  compatibilityCheck.mutate({
    items: items.value.map((item) => ({ skuPublicId: item.skuPublicId, quantity: item.quantity })),
  })
}

const canSave = ref(false)
// 組長 PR #35 round-3 review, P1-2: mirrors EfCompatibilityCheckService.MergeAndValidateItems's
// own bounds (1–20 items, 1–8 per SKU) — must be checked before "儲存為我的清單" is even
// clickable, not just left for the backend to reject after the fact.
const itemsValidation = ref(validateBuildItems([]))

watch([name, items], () => {
  saveGuestBuildDraft({ name: name.value, items: items.value })
  itemsValidation.value = validateBuildItems(items.value)
  canSave.value = name.value.trim().length > 0 && itemsValidation.value.isValid

  clearTimeout(debounceHandle)
  debounceHandle = setTimeout(runCompatibilityCheck, 500)
}, { deep: true, immediate: true })

const saveError = ref<unknown>(null)

async function save(): Promise<void> {
  saveError.value = null
  try {
    const buildList = await createBuildList.mutateAsync({
      name: name.value,
      items: items.value.map((item) => ({ skuPublicId: item.skuPublicId, quantity: item.quantity })),
    })
    clearGuestBuildDraft()
    await router.push(`/builds/${buildList.publicId}`)
  } catch (error) {
    // 組長 PR #35 review, item 2: a guest without a session used to be sent to /unauthorized —
    // a dead end that abandons the draft. Redirect to login instead, preserving this exact page
    // (draft included, since it only clears from localStorage on a real 200) as the return
    // target; `autoResumeAfterLogin` below finishes the save once they're back and authenticated.
    if (isApiError(error) && error.status === 401) {
      markPendingBuildSaveResume()
      await router.push({ path: '/login', query: { redirect: route.fullPath } })
      return
    }

    saveError.value = error
  }
}

// 組長 PR #35 review, item 2: "登入成功後再把 localStorage 草稿建立成新的會員清單" — once the
// shopper is back on this exact page (LoginForm's redirect target) and the session has actually
// resolved to authenticated, finish the save automatically instead of making them press the
// button again. Guarded to fire at most once per page load so it can't loop if the create call
// itself keeps failing for some other reason.
//
// 組長 PR #35 round-2 review, P1-1: "session authenticated + a draft exists" isn't proof this page
// load is actually that specific guest-save -> login -> return round trip — an already-logged-in
// member just visiting /builds/new, or one who switched accounts while an old draft was still in
// localStorage, both satisfy the same two conditions and would have their own account silently
// import whatever stale draft happens to be sitting around. `consumePendingBuildSaveResume()`
// only returns true when `save()`'s own 401 handler set the marker moments before this exact
// redirect — a one-shot signal for this one round trip, not a standing "always auto-import" rule.
let hasAttemptedAutoResume = false
watch(() => sessionStore.status, (status) => {
  if (status !== 'authenticated' || hasAttemptedAutoResume || !canSave.value) {
    return
  }
  hasAttemptedAutoResume = true
  if (!consumePendingBuildSaveResume()) {
    return
  }
  void save()
}, { immediate: true })
</script>

<template>
  <section aria-labelledby="new-build-page-title">
    <h1 id="new-build-page-title">
      新增組裝清單
    </h1>
    <p class="new-build-page__hint">
      在登入並儲存之前，這份清單只會暫存在此瀏覽器中。
    </p>

    <div class="new-build-page__field">
      <label for="build-name">清單名稱</label>
      <input
        id="build-name"
        v-model="name"
        type="text"
        maxlength="160"
        placeholder="例如：文書機、電競主機"
      >
    </div>

    <BuildItemsEditor
      :items="items"
      :disabled="createBuildList.isPending.value"
      @update:items="(next) => { items = next }"
    />

    <CompatibilityFindingsList
      v-if="compatibilityCheck.data.value"
      :overall="compatibilityCheck.data.value.overall"
      :results="compatibilityCheck.data.value.results"
    />
    <ErrorState
      v-else-if="compatibilityCheck.isError.value"
      title="相容性檢查失敗"
      :correlation-id="isApiError(compatibilityCheck.error.value) ? compatibilityCheck.error.value.correlationId : undefined"
      @retry="runCompatibilityCheck"
    />

    <ul
      v-if="items.length > 0 && !itemsValidation.isValid"
      class="new-build-page__items-errors"
    >
      <li
        v-for="error in itemsValidation.errors"
        :key="error"
      >
        {{ error }}
      </li>
    </ul>

    <div class="new-build-page__actions">
      <button
        type="button"
        :disabled="!canSave || createBuildList.isPending.value"
        @click="save"
      >
        儲存為我的清單
      </button>
    </div>

    <ErrorState
      v-if="saveError"
      title="儲存失敗"
      :correlation-id="isApiError(saveError) ? saveError.correlationId : undefined"
      :description="isApiError(saveError) ? saveError.message : undefined"
      @retry="save"
    />
  </section>
</template>

<style scoped>
.new-build-page__hint {
  color: var(--color-text-muted);
  margin-block-end: 1.5rem;
}

.new-build-page__field {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  max-width: 24rem;
  margin-block-end: 1.5rem;
}

.new-build-page__field input {
  min-height: 2.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  font: inherit;
}

.new-build-page__items-errors {
  margin: 1rem 0 0;
  padding-left: 1.25rem;
  color: var(--color-danger);
  font-size: 0.875rem;
}

.new-build-page__actions {
  margin-block: 1.5rem;
}
</style>
