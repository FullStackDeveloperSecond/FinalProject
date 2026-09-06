<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import {
  createListStagger,
  createPageReveal,
  useInjectedMotionPreset,
  useMotionScope,
} from '@doselect/web-shared/motion'
import { canAccessAdminPage } from '../router/access'
import { adminNavigation } from '../navigation'
import AdminIcon from '../components/AdminIcon.vue'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'

const auth = useAdminAuthStore()

// 入口卡進場：只透過 template ref 操作本頁節點，不使用全域 selector。
const dashboardRoot = ref<HTMLElement | null>(null)
const headingRef = ref<HTMLElement | null>(null)
const cardItems = ref<HTMLElement[]>([])
const preset = useInjectedMotionPreset()
const motion = useMotionScope(dashboardRoot)

const visibleSections = computed(() => adminNavigation.map(group => ({
  ...group,
  cards: group.items.filter(item => canAccessAdminPage(item.to, auth.currentUser?.roles ?? [], auth.isAuthenticated)),
})).filter(group => group.cards.length > 0))
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
      class="home-section admin-dashboard-group"
      :class="'admin-tone--' + section.id"
      :aria-label="section.title"
    >
      <h2 class="home-section__title">
        <AdminIcon
          :name="section.icon"
          :size="26"
        />{{ section.title }}
      </h2><p class="admin-dashboard-group__description">
        {{ section.description }}
      </p>
      <div class="home-grid">
        <article
          v-for="card in section.cards"
          :key="card.to"
          ref="cardItems"
          class="home-card card"
        >
          <span class="admin-card-icon"><AdminIcon
            :name="card.icon"
            :size="26"
          /></span><h3>{{ card.title }}</h3>
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
