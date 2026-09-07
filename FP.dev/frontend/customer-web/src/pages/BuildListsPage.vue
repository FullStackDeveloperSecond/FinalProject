<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useBuildLists } from '../features/builds/useBuilds'

// 組長 PR #35 review, item 7: the API already returns paging info (pageNumber/totalPages) —
// the page just never surfaced any way to move past the first 20 lists. Query-string-driven
// (mirrors ProductsPage.vue's own pattern) so the current page is bookmarkable/shareable and
// survives back/forward navigation.
const route = useRoute()
const router = useRouter()
const pageNumber = computed(() => {
  const raw = route.query.page
  const parsed = typeof raw === 'string' ? Number(raw) : NaN
  return Number.isFinite(parsed) && parsed >= 1 ? Math.trunc(parsed) : 1
})
const pageSize = 20

const query = computed(() => ({ pageNumber: pageNumber.value, pageSize }))
const { data: result, isPending, isError, error, refetch } = useBuildLists(query)

const totalPages = computed(() => Number(result.value?.totalPages ?? 0))

function goToPage(nextPage: number): void {
  void router.push({ query: { ...route.query, page: String(nextPage) } })
}

const overallLabels: Record<string, string> = {
  compatible: '相容',
  warning: '有警告',
  blocked: '不相容',
  insufficientData: '資料不足',
}

function formatTwd(amount: number): string {
  return `NT$${amount.toLocaleString('zh-Hant-TW')}`
}
</script>

<template>
  <section aria-labelledby="build-lists-page-title">
    <div class="build-lists-page__header">
      <h1 id="build-lists-page-title">
        我的組裝清單
      </h1>
      <RouterLink to="/builds/new">
        新增組裝清單
      </RouterLink>
    </div>

    <LoadingState
      v-if="isPending"
      label="組裝清單載入中"
    />
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />
    <EmptyState
      v-else-if="result && result.items.length === 0"
      title="還沒有任何組裝清單"
      description="從新增組裝清單開始，挑選零件並檢查相容性。"
    >
      <RouterLink to="/builds/new">
        建立第一個組裝清單
      </RouterLink>
    </EmptyState>

    <ul
      v-else-if="result"
      class="build-lists-page__list"
      aria-label="組裝清單"
    >
      <li
        v-for="item in result.items"
        :key="item.publicId"
        class="build-lists-page__item"
      >
        <RouterLink :to="`/builds/${item.publicId}`">
          {{ item.name }}
        </RouterLink>
        <span class="build-lists-page__meta">
          {{ item.itemCount }} 項零件・{{ overallLabels[item.compatibilityOverall] ?? item.compatibilityOverall }}・{{ formatTwd(Number(item.grandTotal)) }}
          <template v-if="item.isShared">
            ・已分享
          </template>
        </span>
      </li>
    </ul>

    <div
      v-if="totalPages > 1"
      class="build-lists-page__pagination"
    >
      <button
        type="button"
        :disabled="pageNumber <= 1"
        @click="goToPage(pageNumber - 1)"
      >
        上一頁
      </button>
      <span>第 {{ pageNumber }} / {{ totalPages }} 頁</span>
      <button
        type="button"
        :disabled="pageNumber >= totalPages"
        @click="goToPage(pageNumber + 1)"
      >
        下一頁
      </button>
    </div>
  </section>
</template>

<style scoped>
.build-lists-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-block-end: 1.5rem;
}

.build-lists-page__list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.build-lists-page__item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding: 1rem;
  border: 1px solid var(--color-border-soft);
  border-radius: 0.5rem;
}

.build-lists-page__meta {
  color: var(--color-text-muted);
  font-size: 0.875rem;
}

.build-lists-page__pagination {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-block-start: 1.5rem;
}
</style>
