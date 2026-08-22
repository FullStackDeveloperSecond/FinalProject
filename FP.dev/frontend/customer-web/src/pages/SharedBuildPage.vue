<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import CompatibilityFindingsList from '../features/builds/components/CompatibilityFindingsList.vue'
import { useSharedBuild } from '../features/builds/useBuilds'

const props = defineProps<{ shareToken: string }>()

const { data: sharedBuild, isPending, isError, error, refetch } = useSharedBuild(() => props.shareToken)

// 失效／已撤銷／已刪除的分享一律回 404，前端不揭露原因（商品、組裝與相容性.md）。
const isUnavailable = () => isApiError(error.value) && error.value.status === 404
</script>

<template>
  <section aria-labelledby="shared-build-page-title">
    <LoadingState
      v-if="isPending"
      label="分享清單載入中"
    />
    <EmptyState
      v-else-if="isError && isUnavailable()"
      title="此組裝清單目前無法使用"
      description="連結可能已撤銷、清單已刪除，或擁有者帳號已停用。"
    >
      <RouterLink to="/">
        回組裝清單首頁
      </RouterLink>
    </EmptyState>
    <ErrorState
      v-else-if="isError"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      @retry="refetch"
    />

    <template v-else-if="sharedBuild">
      <h1 id="shared-build-page-title">
        {{ sharedBuild.name }}
      </h1>
      <p class="shared-build-page__hint">
        這是別人分享給你的組裝清單，僅供瀏覽。
      </p>

      <ul
        class="shared-build-page__items"
        aria-label="零件清單"
      >
        <li
          v-for="item in sharedBuild.items"
          :key="item.publicId"
        >
          {{ item.name }}（{{ item.skuCode }}）× {{ item.quantity }}
        </li>
      </ul>

      <CompatibilityFindingsList
        :overall="sharedBuild.compatibility.overall"
        :results="sharedBuild.compatibility.results"
      />

      <dl class="shared-build-page__totals">
        <dt>合計</dt>
        <dd>NT${{ sharedBuild.totals.grandTotal.toLocaleString('zh-Hant-TW') }}</dd>
      </dl>

      <!--
        canCopy／canAddToCart 只是後端回傳的旗標；目前控制器只提供
        BuildListsController.AddToCart（限清單擁有者本人），沒有分享頁專用的
        「複製到我的清單」或「代下單」端點（BuildListContracts.cs 的
        AddToCartAsync 註解已明確標記為未實作，已於本次會期記錄並反映給組長），
        因此這裡先誠實顯示狀態，不做出實際上打不到 API 的按鈕。
      -->
      <p
        v-if="sharedBuild.canCopy || sharedBuild.canAddToCart"
        class="shared-build-page__note"
      >
        複製清單／直接加入購物車功能尚未開放。
      </p>
    </template>
  </section>
</template>

<style scoped>
.shared-build-page__hint {
  color: #4b5563;
  margin-block-end: 1.5rem;
}

.shared-build-page__items {
  list-style: none;
  margin: 0 0 1.5rem;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.shared-build-page__totals {
  display: grid;
  grid-template-columns: auto auto;
  gap: 0.25rem 1rem;
  margin-block: 1.5rem;
}

.shared-build-page__totals dd {
  margin: 0;
  font-weight: 700;
}

.shared-build-page__note {
  color: #4b5563;
  font-size: 0.875rem;
}
</style>
