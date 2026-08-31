<script setup lang="ts">
import { computed } from 'vue'
import { isApiError } from '@doselect/web-shared/api'
import { useSessionStore } from '../../../stores/session'
import { useAddFavoriteMutation, useMyFavoritesQuery, useRemoveFavoriteMutation } from '../queries'

const props = defineProps<{
  productPublicId: string
}>()

const sessionStore = useSessionStore()
const favoritesQuery = useMyFavoritesQuery(computed(() => sessionStore.isAuthenticated))
const addMutation = useAddFavoriteMutation()
const removeMutation = useRemoveFavoriteMutation()

const isFavorited = computed(() =>
  favoritesQuery.data.value?.some(favorite => favorite.product.productPublicId === props.productPublicId) ?? false)

const isPending = computed(() =>
  favoritesQuery.isPending.value || addMutation.isPending.value || removeMutation.isPending.value)

const errorMessage = computed(() => {
  const caught = addMutation.error.value ?? removeMutation.error.value
  if (!caught) return null
  return isApiError(caught) ? caught.message : '收藏操作失敗，請稍後再試。'
})

function toggle(): void {
  if (isPending.value) return
  if (isFavorited.value) {
    removeMutation.mutate(props.productPublicId)
  } else {
    addMutation.mutate(props.productPublicId)
  }
}
</script>

<template>
  <div class="favorite-toggle">
    <RouterLink
      v-if="sessionStore.status === 'anonymous'"
      :to="`/login?redirect=${encodeURIComponent(`/products/${productPublicId}`)}`"
      class="favorite-toggle__login-link"
    >
      登入後收藏
    </RouterLink>
    <template v-else-if="sessionStore.isAuthenticated">
      <button
        type="button"
        class="favorite-toggle__button"
        :class="{ 'favorite-toggle__button--active': isFavorited }"
        :disabled="isPending"
        :aria-pressed="isFavorited"
        @click="toggle"
      >
        {{ isFavorited ? '♥ 已收藏' : '♡ 加入收藏' }}
      </button>
      <p
        v-if="errorMessage"
        class="favorite-toggle__error"
      >
        {{ errorMessage }}
      </p>
    </template>
  </div>
</template>

<style scoped>
.favorite-toggle {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  align-items: flex-start;
}

.favorite-toggle__button {
  background: #fff;
  border: 1px solid #d1d5db;
  border-radius: 0.5rem;
  padding: 0.5rem 0.875rem;
  cursor: pointer;
}

.favorite-toggle__button--active {
  border-color: #ec4899;
  color: #be185d;
}

.favorite-toggle__login-link {
  font-size: 0.875rem;
}

.favorite-toggle__error {
  margin: 0;
  color: #b91c1c;
  font-size: 0.875rem;
}
</style>
