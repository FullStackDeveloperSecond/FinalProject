<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import {
  createListStagger,
  createPageReveal,
  useInjectedMotionPreset,
  useMotionScope,
} from '@doselect/web-shared/motion'
import { canAccessAdminPage } from '../router/access'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'

const auth = useAdminAuthStore()

// 入口卡進場：只透過 template ref 操作本頁節點，不使用全域 selector。
const dashboardRoot = ref<HTMLElement | null>(null)
const headingRef = ref<HTMLElement | null>(null)
const cardItems = ref<HTMLElement[]>([])
const preset = useInjectedMotionPreset()
const motion = useMotionScope(dashboardRoot)

// 與 App.vue 側欄相同的角色判斷；營運報表沒有權限時不列在入口卡上。
const canViewOperationalReports = computed(() => {
  const roles = auth.currentUser?.roles ?? []
  return ['MarketingAnalyst', 'FinanceManager', 'SuperAdmin'].some(role => roles.includes(role))
})

/**
 * 入口卡只做既有 route 的導覽，刻意不顯示任何統計數字 ——
 * 目前沒有正式的儀表板彙總 API，不會為了畫面好看放上假資料。
 */
const sections: { title: string, cards: { title: string, description: string, to: string }[] }[] = [
  {
    title: '客服與售後',
    cards: [
      { title: '客服 SLA 佇列', description: '檢視待處理與逾時的客服案件，並受理未指派的案件。', to: '/support' },
      { title: '案件工作台', description: '依最後活動時間排序的統一案件清單，可依優先度與逾時狀態篩選。', to: '/cases' },
      { title: '退貨案件', description: '審核退貨申請、確認收件與退款處理進度。', to: '/returns' },
      { title: '商品評價審核', description: '審核已驗證購買的評價，核准後才會顯示於商品頁。', to: '/reviews' },
    ],
  },
  {
    title: '商品',
    cards: [
      { title: '商品管理', description: '維護商品、SKU、價格與上下架狀態。', to: '/products' },
      { title: '品牌／分類／標籤管理', description: '維護商品目錄的共用查找資料。', to: '/catalog/lookups' },
      { title: '相容性規則', description: '維護零組件相容性判斷規則。', to: '/catalog/compatibility' },
    ],
  },
  {
    // AI 用量與成本屬於 AI 模組負責範圍，這裡不另外開入口；側欄既有連結維持不變。
    title: '營運',
    cards: [
      { title: '訂單管理', description: '檢視訂單列表與詳情，並執行狀態轉移。', to: '/orders' },
    ],
  },
]

const visibleSections = computed(() => sections
  .map(section => (
    section.title === '營運' && canViewOperationalReports.value
      ? {
          ...section,
          cards: [
            ...section.cards,
            { title: '營運報表', description: '七個營運報表共用查詢殼層，可匯出 CSV／XLSX。', to: '/reports/sales-overview' },
          ],
        }
      : section
  ))
  .map(section => ({
    ...section,
    cards: section.cards.filter(card => canAccessAdminPage(card.to, auth.currentUser?.roles ?? [], auth.isAuthenticated)),
  }))
  .filter(section => section.cards.length > 0))

function playIntro(): void {
  motion.run(({ reducedMotion }) => {
    createPageReveal(headingRef.value, preset.value, { reducedMotion })
    createListStagger(cardItems.value, preset.value, { reducedMotion, delay: 0.04 })
  })
}

onMounted(playIntro)

// 取得登入身分後，營運報表入口才會出現；只有卡片數量真的改變時才重播一次，
// 不對每一次 reactive update 重播整頁進場。
watch(
  () => visibleSections.value.reduce((total, section) => total + section.cards.length, 0),
  (next, previous) => {
    if (next !== previous) {
      playIntro()
    }
  },
)
</script>

<template>
  <section
    ref="dashboardRoot"
    aria-labelledby="page-title"
  >
    <h1
      id="page-title"
      ref="headingRef"
    >
      管理工作台
    </h1>
    <p class="view-lede">
      從你的管理權限出發，快速前往今日需要處理的工作。
    </p>

    <section
      v-for="section in visibleSections"
      :key="section.title"
      class="home-section"
      :aria-label="section.title"
    >
      <h2 class="home-section__title">
        {{ section.title }}
      </h2>
      <div class="home-grid">
        <article
          v-for="card in section.cards"
          :key="card.to"
          ref="cardItems"
          class="home-card card"
        >
          <h3>{{ card.title }}</h3>
          <p>{{ card.description }}</p>
          <RouterLink
            class="home-card__link"
            :to="card.to"
          >
            前往{{ card.title }}
          </RouterLink>
        </article>
      </div>
    </section>
  </section>
</template>

<style scoped>
.home-section {
  margin-top: var(--space-6);
}

.home-section__title {
  margin: 0 0 var(--space-3);
  font-size: var(--fs-caption);
  font-weight: 700;
  letter-spacing: 0.06em;
  color: var(--color-text-faint);
}

.home-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr));
  gap: var(--space-4);
}

.home-card {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--space-2);
  margin-top: 0;
}

.home-card h3 {
  margin: 0;
  font-size: var(--fs-h3);
  line-height: var(--lh-heading);
  color: var(--color-text);
}

.home-card p {
  margin: 0;
  flex: 1;
  font-size: var(--fs-caption);
  color: var(--color-text-muted);
}

.home-card__link {
  display: inline-flex;
  align-items: center;
  min-height: 36px;
  padding: 0 var(--space-4);
  border-radius: var(--radius-sm);
  border: 1px solid var(--color-primary);
  background: var(--color-primary);
  color: var(--color-on-primary);
  font-size: var(--fs-caption);
  font-weight: 700;
  text-decoration: none;
}

.home-card__link:hover {
  background: var(--color-primary-dark);
  border-color: var(--color-primary-dark);
}

.home-card__link:focus-visible {
  outline: 2px solid var(--color-focus-ring);
  outline-offset: 2px;
}
</style>
