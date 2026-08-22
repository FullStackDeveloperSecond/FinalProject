<script setup lang="ts">
import { ErrorState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import BuildItemsEditor, { type EditableBuildItem } from '../features/builds/components/BuildItemsEditor.vue'
import CompatibilityFindingsList from '../features/builds/components/CompatibilityFindingsList.vue'
import { useCompatibilityCheck, useCreateBuildList } from '../features/builds/useBuilds'
import { clearGuestBuildDraft, loadGuestBuildDraft, saveGuestBuildDraft } from '../features/builds/guestBuildDraft'

const router = useRouter()

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

watch([name, items], () => {
  saveGuestBuildDraft({ name: name.value, items: items.value })
  canSave.value = name.value.trim().length > 0 && items.value.length > 0

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
    if (isApiError(error) && error.status === 401) {
      // 沒有登入流程可導向到 /login（此 repo 尚未建置任何登入頁），先導向現有的 401 頁面並保留草稿。
      await router.push('/unauthorized')
      return
    }

    saveError.value = error
  }
}
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
  color: #4b5563;
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
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  font: inherit;
}

.new-build-page__actions {
  margin-block: 1.5rem;
}
</style>
