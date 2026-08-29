<script setup lang="ts">
/**
 * 優惠券的適用與排除範圍。
 *
 * 這個元件只負責挑選，不做規則檢查 —— 規則集中在
 * `features/coupons/scope.ts`，由表單一併判斷並控制送出按鈕。
 */
import { computed, ref } from 'vue'
import { isApiError } from '@doselect/web-shared/api'
import CouponProductPicker from './CouponProductPicker.vue'
import { useCategoryOptions } from '../../features/catalog-reference/useCatalogReference'
import { describeApiError } from '../../features/shared/errorMessages'
import type { CouponScopeType } from '../../features/coupons/types'

const props = defineProps<{
  scopeType: CouponScopeType
  categoryPublicIds: string[]
  productPublicIds: string[]
  excludedProductPublicIds: string[]
}>()

const emit = defineEmits<{
  'update:scopeType': [CouponScopeType]
  'update:categoryPublicIds': [string[]]
  'update:productPublicIds': [string[]]
  'update:excludedProductPublicIds': [string[]]
}>()

const restricted = computed(() => props.scopeType === 'restricted')

const categoryTerm = ref('')

// 攤平整棵分類樹要對每個節點各發一個請求，所以只在真的展開「指定範圍」時才載入。
const {
  data: categories,
  isPending: categoriesPending,
  isError: categoriesFailed,
  error: categoriesError,
} = useCategoryOptions(restricted)

const visibleCategories = computed(() => {
  const term = categoryTerm.value.trim().toLowerCase()
  const all = categories.value ?? []
  if (term === '') {
    return all
  }

  return all.filter(option =>
    option.path.toLowerCase().includes(term) || option.code.toLowerCase().includes(term))
})

/**
 * 已選但不在清單裡的分類。
 *
 * 分類清單來自店面的篩選端點，只含啟用中的分類。舊券引用的分類若之後被停用，
 * 它仍然生效，卻不會出現在可勾選的清單裡 —— 不另外列出來的話，管理員會以為
 * 這張券沒有設定分類範圍。
 */
const unlistedCategoryPublicIds = computed(() => {
  const known = new Set((categories.value ?? []).map(option => option.publicId))
  return props.categoryPublicIds.filter(publicId => !known.has(publicId))
})

/** 切到「全站」時保留已選項目，送出時才由 `toScopeRequestFields` 丟掉。 */
const hiddenSelectionCount = computed(() =>
  props.categoryPublicIds.length + props.productPublicIds.length)

function toggleCategory(publicId: string) {
  emit(
    'update:categoryPublicIds',
    props.categoryPublicIds.includes(publicId)
      ? props.categoryPublicIds.filter(selected => selected !== publicId)
      : [...props.categoryPublicIds, publicId],
  )
}
</script>

<template>
  <fieldset class="coupon-scope">
    <legend>適用範圍</legend>

    <label>
      <input
        type="radio"
        name="scopeType"
        value="all"
        :checked="props.scopeType === 'all'"
        @change="emit('update:scopeType', 'all')"
      >
      全站適用
    </label>
    <label>
      <input
        type="radio"
        name="scopeType"
        value="restricted"
        :checked="restricted"
        @change="emit('update:scopeType', 'restricted')"
      >
      指定分類或商品
    </label>

    <p
      v-if="!restricted && hiddenSelectionCount > 0"
      class="scope-hint"
    >
      已選的 {{ hiddenSelectionCount }} 個分類／商品在全站適用下不會送出；
      改回「指定分類或商品」即可復原。
    </p>

    <template v-if="restricted">
      <div class="scope-categories">
        <h4>適用分類</h4>
        <p class="scope-hint">
          只列出啟用中的分類。
        </p>

        <input
          v-model="categoryTerm"
          type="search"
          aria-label="篩選分類"
          placeholder="輸入分類名稱或代碼"
        >

        <p
          v-if="categoriesFailed"
          class="scope-error"
          role="alert"
        >
          分類清單載入失敗，可能不完整，請重新整理後再設定指定範圍：{{
            isApiError(categoriesError) ? describeApiError(categoriesError) : '請稍後再試。'
          }}
        </p>
        <p v-else-if="categoriesPending">
          分類載入中…
        </p>
        <p v-else-if="visibleCategories.length === 0">
          沒有符合的分類。
        </p>
        <ul
          v-else
          class="scope-results"
        >
          <li
            v-for="option in visibleCategories"
            :key="option.publicId"
          >
            <label>
              <input
                type="checkbox"
                :checked="props.categoryPublicIds.includes(option.publicId)"
                @change="toggleCategory(option.publicId)"
              >
              {{ option.path }}（{{ option.code }}）
            </label>
          </li>
        </ul>

        <ul
          v-if="unlistedCategoryPublicIds.length > 0"
          class="scope-selected"
        >
          <li
            v-for="publicId in unlistedCategoryPublicIds"
            :key="publicId"
          >
            <span>{{ publicId }}（已停用或找不到的分類）</span>
            <button
              type="button"
              :aria-label="`移除 ${publicId}`"
              @click="toggleCategory(publicId)"
            >
              移除
            </button>
          </li>
        </ul>
      </div>

      <CouponProductPicker
        label="適用商品"
        hint="只搜尋得到已上架的商品。"
        :model-value="props.productPublicIds"
        @update:model-value="emit('update:productPublicIds', $event)"
      />
    </template>

    <CouponProductPicker
      label="排除商品"
      hint="這些商品不參與折扣。全站適用也可以搭配排除清單。"
      :model-value="props.excludedProductPublicIds"
      @update:model-value="emit('update:excludedProductPublicIds', $event)"
    />
  </fieldset>
</template>

<style scoped>
.coupon-scope {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.scope-categories {
  border: 1px solid #d0d0d0;
  border-radius: 4px;
  padding: 0.75rem;
}

.scope-hint {
  color: #555;
  font-size: 0.875rem;
}

.scope-error {
  color: #b00020;
}

.scope-results,
.scope-selected {
  list-style: none;
  margin: 0.5rem 0;
  max-height: 12rem;
  overflow-y: auto;
  padding: 0;
}

.scope-selected li {
  display: flex;
  gap: 0.5rem;
  justify-content: space-between;
}
</style>
