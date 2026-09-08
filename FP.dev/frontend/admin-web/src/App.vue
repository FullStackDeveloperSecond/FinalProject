<script setup lang="ts">
import { computed, defineAsyncComponent, provide, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { UiButton } from '@doselect/web-shared/ui'
import { useAdminAuthStore } from './features/auth/stores/useAdminAuthStore'
import { canAccessAdminPage } from './router/access'
import { adminNavigation } from './navigation'
import AdminIcon from './components/AdminIcon.vue'
import { BrandMark } from '@doselect/web-shared/components'
import {
  adminDefaultMotionPresetId,
  motionPresetKey,
  useMotionPreference,
  useMotionPresetSelection,
} from '@doselect/web-shared/motion'

// 切換器只在明確啟用 motion debug 的 dev 進入模組圖。production build 中條件為 false，
// 因此 Rollup 會把整個動態 import 分支連同元件與其字串一起移除 ——
// 正式產物裡不存在任何實驗模式選單。
const motionDebugEnabled = import.meta.env.DEV === true
  && import.meta.env.VITE_ENABLE_MOTION_DEBUG === 'true'
const MotionDevSwitcher = motionDebugEnabled
  ? defineAsyncComponent(() => import('@doselect/web-shared/motion/MotionDevSwitcher.vue'))
  : null

const route = useRoute()
const router = useRouter()
const auth = useAdminAuthStore()
const sidebarOpen = ref(false)
watch(() => route.fullPath, () => { sidebarOpen.value = false })

// GSAP 動態視覺探索：與 Customer 共用同一組 preset 與同一個 dev-only 切換機制。
const { presetId, preset, canSwitch, select } = useMotionPresetSelection(adminDefaultMotionPresetId)
const prefersReducedMotion = useMotionPreference()
provide(motionPresetKey, preset)

const isAuthPage = computed(() => route.path.startsWith('/login'))
function canAccess(path: string): boolean {
  return canAccessAdminPage(path, auth.currentUser?.roles ?? [], auth.isAuthenticated)
}

const visibleGroups = computed(() => adminNavigation.map(group => ({ ...group, items: group.items.filter(item => canAccess(item.to)) })).filter(group => group.items.length > 0))
const expandedGroups = ref<Record<string, boolean>>({})
watch(() => [route.path, visibleGroups.value] as const, () => {
  const group = visibleGroups.value.find(group => group.items.some(item => route.path === item.to || route.path.startsWith(item.to + '/')))
  if (group) expandedGroups.value[group.id] = true
}, { immediate: true })
async function onLogout(): Promise<void> {
  await auth.logout()
  await router.push('/login')
}
</script>

<template>
  <div
    v-if="isAuthPage"
    class="app-shell app-shell--bare"
  >
    <RouterView />
  </div>

  <div
    v-else
    class="app-shell"
  >
    <a
      class="skip-link"
      href="#main-content"
    >跳到主要內容</a>
    <header class="site-header">
      <RouterLink
        class="brand-link"
        to="/"
      >
        <!-- 正式商標 2 是方形徽章，40px 下裡面的字樣讀不出來，品牌名以文字承載；
             標記本身是裝飾，避免螢幕閱讀器把品牌名念兩次 -->
        <BrandMark decorative />
        <span class="brand-link__text">
          <span class="brand-link__name">DoSelect 懂選</span>
          <span class="brand-link__scope">管理後台</span>
        </span>
      </RouterLink>
      <div class="site-header__end">
        <button
          type="button"
          class="admin-nav-toggle"
          :aria-expanded="sidebarOpen"
          aria-controls="admin-navigation"
          @click="sidebarOpen = !sidebarOpen"
        >
          管理選單
        </button>
        <span class="demo-badge">DEMO DATA</span>
        <span
          v-if="auth.currentUser"
          class="current-user"
        >{{ auth.currentUser.displayName }}</span>
        <RouterLink
          v-if="auth.isAuthenticated"
          to="/security/totp-rebind"
          class="totp-rebind-link"
        >
          重新綁定 TOTP
        </RouterLink>
        <UiButton
          v-if="auth.isAuthenticated"
          type="button"
          class="logout-button"
          label="登出"
          @click="onLogout"
        />
      </div>
    </header>
    <div class="admin-frame">
      <aside
        id="admin-navigation"
        class="admin-sidebar"
        :class="{ 'admin-sidebar--open': sidebarOpen }"
        aria-label="管理功能導覽"
      >
        <nav class="admin-sidebar__nav">
          <RouterLink
            v-if="canAccess('/')"
            to="/"
            class="admin-overview"
          >
            <AdminIcon name="home" />首頁
          </RouterLink>
          <section
            v-for="group in visibleGroups"
            :key="group.id"
            class="admin-nav-group"
            :class="'admin-tone--' + group.id"
          >
            <button
              type="button"
              class="admin-nav-group__toggle"
              :aria-expanded="!!expandedGroups[group.id]"
              :aria-controls="'admin-group-' + group.id"
              @click="expandedGroups[group.id] = !expandedGroups[group.id]"
            >
              <AdminIcon :name="group.icon" /><span>{{ group.title }}</span>
              <svg
                class="admin-nav-group__chevron"
                :class="{ 'is-open': expandedGroups[group.id] }"
                width="18"
                height="18"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                aria-hidden="true"
              ><path d="m6 9 6 6 6-6" /></svg>
            </button>
            <div
              v-show="expandedGroups[group.id]"
              :id="'admin-group-' + group.id"
              class="admin-nav-group__items"
            >
              <RouterLink
                v-for="item in group.items"
                :key="item.to"
                :to="item.to"
              >
                <AdminIcon
                  :name="item.icon"
                  :size="18"
                /><span>{{ item.title }}</span>
              </RouterLink>
            </div>
          </section>
        </nav>
      </aside>
      <main
        id="main-content"
        class="site-main"
        tabindex="-1"
      >
        <RouterView />
      </main>
    </div>
    <component
      :is="MotionDevSwitcher"
      v-if="canSwitch && MotionDevSwitcher"
      :preset-id="presetId"
      :reduced-motion="prefersReducedMotion"
      @select="select"
    />
  </div>
</template>
