<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, ref } from 'vue'
import { useMyFavoritesQuery, useRemoveFavoriteMutation } from '../../features/favorites/queries'
import { favoriteAvailabilityLabels, formatFavoritedDate } from '../../features/favorites/labels'

const PAGE_SIZE = 20

const pageNumber = ref(1)
const feedback = ref<string | null>(null)

const favoritesQuery = useMyFavoritesQuery(pageNumber, PAGE_SIZE)
const removeMutation = useRemoveFavoriteMutation()

const totalPages = computed(() => Number(favoritesQuery.data.value?.totalPages ?? 0))

function describeError(error: unknown): string {
  return isApiError(error) ? error.message : '操作失敗，請稍後再試。'
}

function formatTwd(amount: number | string): string {
  return `NT$${Number(amount).toLocaleString('zh-Hant-TW')}`
}

function goToPage(nextPage: number): void {
  pageNumber.value = nextPage
}

function removeFavorite(productPublicId: string): void {
  feedback.value = null
  removeMutation.mutate(productPublicId, {
    onSuccess: () => { feedback.value = '已移除收藏。' },
    onError: (caught) => { feedback.value = describeError(caught) },
  })
}
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
      <p>缺貨商品仍會保留收藏並顯示缺貨；商品下架後保留紀錄但無法購買，重新上架會自動恢復正常顯示。</p>
    </header>

    <LoadingState
      v-if="favoritesQuery.isPending.value"
      label="收藏清單載入中"
    />
    <ErrorState
      v-else-if="favoritesQuery.isError.value"
      :description="describeError(favoritesQuery.error.value)"
      @retry="favoritesQuery.refetch"
    />
    <template v-else-if="favoritesQuery.data.value">
      <p
        v-if="feedback"
        role="status"
        class="favorites-page__feedback"
      >
        {{ feedback }}
      </p>

      <EmptyState
        v-if="favoritesQuery.data.value.items.length === 0"
        title="目前沒有收藏商品"
        description="瀏覽商品時可以加入收藏，方便之後回來查看。"
      />
      <template v-else>
        <p class="favorites-summary">
          共 {{ favoritesQuery.data.value.totalCount }} 項收藏
        </p>
        <div class="favorites-grid">
          <article
            v-for="item in favoritesQuery.data.value.items"
            :key="item.productPublicId"
            class="favorite-card"
          >
            <RouterLink
              v-if="item.availability !== 'delisted'"
              class="favorite-card__link"
              :to="{ name: 'product-detail', params: { productId: item.productPublicId } }"
            >
              <div
                class="favorite-card__image"
                aria-hidden="true"
              >
                <img
                  v-if="item.primaryImage"
                  :src="item.primaryImage.url"
                  :alt="item.primaryImage.alt"
                >
                <span
                  v-else
                  class="favorite-card__image-placeholder"
                >尚無商品圖片</span>
              </div>
              <p class="favorite-card__brand">
                {{ item.brand.name }}
              </p>
              <h3 class="favorite-card__name">
                {{ item.name }}
              </h3>
            </RouterLink>
            <div
              v-else
              class="favorite-card__link favorite-card__link--delisted"
            >
              <div
                class="favorite-card__image"
                aria-hidden="true"
              >
                <span class="favorite-card__image-placeholder">尚無商品圖片</span>
              </div>
              <p class="favorite-card__brand">
                {{ item.brand.name }}
              </p>
              <h3 class="favorite-card__name">
                {{ item.name }}
              </h3>
            </div>

            <p
              v-if="item.price"
              class="favorite-card__price"
            >
              <span class="favorite-card__price-current">{{ formatTwd(item.price.sale ?? item.price.list) }}</span>
              <span
                v-if="item.price.sale != null && Number(item.price.sale) < Number(item.price.list)"
                class="favorite-card__price-original"
              >{{ formatTwd(item.price.list) }}</span>
            </p>

            <span
              class="favorite-card__availability"
              :class="`favorite-card__availability--${item.availability}`"
            >
              {{ favoriteAvailabilityLabels[item.availability] ?? item.availability }}
            </span>

            <div class="favorite-card__footer">
              <small>加入收藏：{{ formatFavoritedDate(item.createdAtUtc) }}</small>
              <button
                type="button"
                :disabled="removeMutation.isPending.value"
                @click="removeFavorite(item.productPublicId)"
              >
                取消收藏
              </button>
            </div>
          </article>
        </div>

        <nav
          v-if="totalPages > 1"
          class="favorites-pagination"
          aria-label="分頁"
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
        </nav>
      </template>
    </template>
  </section>
</template>

<style scoped>
.favorites-page { display: grid; gap: 1.5rem; }
.favorites-page__feedback { padding: .75rem; border-radius: .5rem; background: #ecfdf5; color: #166534; }
.favorites-summary { margin: 0; color: #6b7280; font-size: .875rem; }

.favorites-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr));
  gap: 1rem;
}

.favorite-card {
  display: flex;
  flex-direction: column;
  gap: .375rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: .75rem;
  background: #fff;
}

.favorite-card__link {
  display: flex;
  flex-direction: column;
  gap: .375rem;
  color: inherit;
  text-decoration: none;
}

.favorite-card__link--delisted {
  opacity: .7;
}

.favorite-card__image {
  display: flex;
  align-items: center;
  justify-content: center;
  aspect-ratio: 4 / 3;
  border-radius: .5rem;
  background: #f3f4f6;
  overflow: hidden;
}

.favorite-card__image img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.favorite-card__image-placeholder {
  color: #9ca3af;
  font-size: .875rem;
}

.favorite-card__brand { margin: 0; color: #6b7280; font-size: .75rem; }
.favorite-card__name { margin: 0; font-size: 1rem; }

.favorite-card__price {
  margin: 0;
  display: flex;
  align-items: baseline;
  gap: .5rem;
}

.favorite-card__price-current { font-weight: 700; font-size: 1.125rem; }
.favorite-card__price-original { color: #9ca3af; text-decoration: line-through; font-size: .875rem; }

.favorite-card__availability {
  align-self: flex-start;
  padding: .125rem .5rem;
  border-radius: 999px;
  font-size: .75rem;
}

.favorite-card__availability--inStock { background: #dcfce7; color: #166534; }
.favorite-card__availability--lowStock { background: #fef3c7; color: #92400e; }
.favorite-card__availability--outOfStock { background: #fee2e2; color: #991b1b; }
.favorite-card__availability--delisted { background: #e5e7eb; color: #374151; }

.favorite-card__footer {
  margin-top: auto;
  padding-top: .5rem;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: .5rem;
  font-size: .8rem;
  color: #6b7280;
}

.favorites-pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  margin-block-start: 1rem;
}
</style>
