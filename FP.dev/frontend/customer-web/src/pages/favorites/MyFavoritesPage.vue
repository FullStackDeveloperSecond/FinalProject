<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed } from 'vue'
import { useMyFavoritesQuery, useRemoveFavoriteMutation } from '../../features/favorites/queries'
import { favoriteAvailabilityLabels } from '../../features/favorites/labels'
import type { Favorite } from '../../features/favorites/types'

const favoritesQuery = useMyFavoritesQuery()
const removeMutation = useRemoveFavoriteMutation()

function describeError(error: unknown): string {
  return isApiError(error) ? error.message : '操作失敗，請稍後再試。'
}

function formatTwd(amount: number | string): string {
  return `NT$${Number(amount).toLocaleString('zh-Hant-TW')}`
}

function remove(favorite: Favorite): void {
  removeMutation.mutate(favorite.product.productPublicId)
}

const isRemoving = computed(() => removeMutation.isPending.value)
</script>

<template>
  <section
    class="favorites-page"
    aria-labelledby="favorites-title"
  >
    <header>
      <h1 id="favorites-title">
        我的收藏
      </h1>
      <p>商品缺貨或下架時仍會保留收藏；已下架商品不能從這裡加入購物車。</p>
    </header>

    <LoadingState
      v-if="favoritesQuery.isPending.value"
      label="收藏資料載入中"
    />
    <ErrorState
      v-else-if="favoritesQuery.isError.value"
      :description="describeError(favoritesQuery.error.value)"
      @retry="favoritesQuery.refetch"
    />
    <template v-else>
      <EmptyState
        v-if="favoritesQuery.data.value?.length === 0"
        title="目前沒有收藏商品"
        description="逛逛商品頁面，點選「加入收藏」即可加入這裡。"
      />
      <ul
        v-else
        class="favorites-list"
      >
        <li
          v-for="favorite in favoritesQuery.data.value"
          :key="favorite.product.productPublicId"
          class="favorite-card"
        >
          <div class="favorite-card__info">
            <RouterLink
              v-if="favorite.product.availability !== 'unlisted'"
              :to="{ name: 'product-detail', params: { productId: favorite.product.productPublicId } }"
              class="favorite-card__name"
            >
              {{ favorite.product.name }}
            </RouterLink>
            <span
              v-else
              class="favorite-card__name"
            >{{ favorite.product.name }}</span>
            <p class="favorite-card__price">
              <span class="favorite-card__price-current">
                {{ formatTwd(favorite.product.salePrice ?? favorite.product.listPrice) }}
              </span>
              <span
                v-if="favorite.product.salePrice != null && Number(favorite.product.salePrice) < Number(favorite.product.listPrice)"
                class="favorite-card__price-original"
              >
                {{ formatTwd(favorite.product.listPrice) }}
              </span>
            </p>
            <span
              class="favorite-card__availability"
              :class="`favorite-card__availability--${favorite.product.availability}`"
            >
              {{ favoriteAvailabilityLabels[favorite.product.availability] ?? favorite.product.availability }}
            </span>
          </div>
          <button
            type="button"
            :disabled="isRemoving"
            @click="remove(favorite)"
          >
            移除收藏
          </button>
        </li>
      </ul>
      <p
        v-if="removeMutation.isError.value"
        class="favorites-page__error"
      >
        {{ describeError(removeMutation.error.value) }}
      </p>
    </template>
  </section>
</template>

<style scoped>
.favorites-page {
  display: grid;
  gap: 1.5rem;
  max-width: 52rem;
}

.favorites-list {
  display: grid;
  gap: 0.75rem;
  padding: 0;
  list-style: none;
}

.favorite-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.75rem;
}

.favorite-card__info {
  display: grid;
  gap: 0.375rem;
}

.favorite-card__name {
  font-weight: 600;
  color: inherit;
  text-decoration: none;
}

a.favorite-card__name:hover {
  text-decoration: underline;
}

.favorite-card__price {
  margin: 0;
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
}

.favorite-card__price-current {
  font-weight: 700;
}

.favorite-card__price-original {
  color: #9ca3af;
  text-decoration: line-through;
  font-size: 0.875rem;
}

.favorite-card__availability {
  align-self: flex-start;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  font-size: 0.75rem;
}

.favorite-card__availability--available {
  background: #dcfce7;
  color: #166534;
}

.favorite-card__availability--outOfStock {
  background: #fef3c7;
  color: #92400e;
}

.favorite-card__availability--unlisted {
  background: #fee2e2;
  color: #991b1b;
}

.favorites-page__error {
  color: #b91c1c;
}
</style>
