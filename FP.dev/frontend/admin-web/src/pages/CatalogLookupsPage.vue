<script setup lang="ts">
/**
 * PR #24 review round 2: the confirmed Route contract (M功能桌面UI與Route規格.md A-08) is a
 * single `/admin/catalog/lookups` page for brand／category／tag paginated management — this
 * had drifted into three separate routes (/brands, /categories, /tags). Rather than rewrite
 * the three pages' logic, this composes them as sections under the one contracted route.
 */
import BrandsPage from './BrandsPage.vue'
import CategoriesPage from './CategoriesPage.vue'
import TagsPage from './TagsPage.vue'
</script>

<template>
  <div class="catalog-lookups-page">
    <BrandsPage />
    <CategoriesPage />
    <TagsPage />
  </div>
</template>

<style scoped>
.catalog-lookups-page {
  display: flex;
  flex-direction: column;
  gap: 3rem;
}
</style>
