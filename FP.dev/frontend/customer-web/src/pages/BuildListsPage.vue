<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { reactive } from 'vue'
import { useBuildLists } from '../features/builds/useBuilds'

const query = reactive({ pageNumber: 1, pageSize: 20 })
const { data: result, isPending, isError, error, refetch } = useBuildLists(query)

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
          {{ item.itemCount }} 項零件・{{ overallLabels[item.compatibilityOverall] }}・{{ formatTwd(item.grandTotal) }}
          <template v-if="item.isShared">
            ・已分享
          </template>
        </span>
      </li>
    </ul>
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
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
}

.build-lists-page__meta {
  color: #4b5563;
  font-size: 0.875rem;
}
</style>
