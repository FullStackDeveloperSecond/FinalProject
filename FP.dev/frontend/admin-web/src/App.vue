<script setup lang="ts">
import { computed, defineAsyncComponent, provide, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAdminAuthStore } from './features/auth/stores/useAdminAuthStore'
import { canAccessAdminPage } from './router/access'
import { BrandMark } from '@doselect/web-shared/components'
import {
  adminDefaultMotionPresetId,
  motionPresetKey,
  useMotionPreference,
  useMotionPresetSelection,
} from '@doselect/web-shared/motion'

// 切換器只在 dev 進入模組圖。`import.meta.env.DEV` 在 production build 被折成 false，
// 因此 Rollup 會把整個動態 import 分支連同元件與其字串一起移除 ——
// 正式產物裡不存在任何實驗模式選單。
const MotionDevSwitcher = import.meta.env.DEV
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
        <button
          v-if="auth.isAuthenticated"
          type="button"
          class="logout-button"
          @click="onLogout"
        >
          登出
        </button>
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
          <p class="admin-sidebar__group-title">
            總覽
          </p>
          <RouterLink
            v-if="canAccess('/')"
            to="/"
          >
            首頁
          </RouterLink>

          <p
            v-if="['/products', '/inventory', '/shipping/stores', '/coupons', '/invoices'].some(canAccess)"
            class="admin-sidebar__group-title"
          >
            商品
          </p>
          <RouterLink
            v-if="canAccess('/catalog/lookups')"
            to="/catalog/lookups"
          >
            品牌／分類／標籤管理
          </RouterLink>
          <RouterLink
            v-if="canAccess('/products')"
            to="/products"
          >
            商品管理
          </RouterLink>
          <RouterLink
            v-if="canAccess('/products/import')"
            to="/products/import"
          >
            商品匯入
          </RouterLink>
          <RouterLink
            v-if="canAccess('/catalog/specifications')"
            to="/catalog/specifications"
          >
            分類規格範本
          </RouterLink>
          <RouterLink
            v-if="canAccess('/catalog/compatibility')"
            to="/catalog/compatibility"
          >
            相容性規則
          </RouterLink>
          
          <RouterLink
            v-if="canAccess('/coupons')"
            to="/coupons"
          >
            優惠券管理
          </RouterLink>
          <RouterLink
            v-if="canAccess('/inventory')"
            to="/inventory"
          >
            庫存管理
          </RouterLink>
          <RouterLink
            v-if="canAccess('/inventory/imports')"
            to="/inventory/imports"
          >
            庫存匯入
          </RouterLink>
          
          <RouterLink
            v-if="canAccess('/shipping/stores')"
            to="/shipping/stores"
          >
            示範超商門市
          </RouterLink>
          <RouterLink
            v-if="canAccess('/shipping/package-limits')"
            to="/shipping/package-limits"
          >
            包裹限制版本
          </RouterLink>
          <RouterLink
            v-if="canAccess('/shipping/batches')"
            to="/shipping/batches"
          >
            批次出貨
          </RouterLink>
          <RouterLink
            v-if="canAccess('/inventory/reservations')"
            to="/inventory/reservations"
          >
            庫存保留佇列
          </RouterLink>
          <RouterLink
            v-if="canAccess('/inventory/reconciliation-cases')"
            to="/inventory/reconciliation-cases"
          >
            庫存對帳案件
          </RouterLink>
          <RouterLink
            v-if="canAccess('/invoices')"
            to="/invoices"
          >
            模擬發票管理
          </RouterLink>
          <p class="admin-sidebar__group-title">
            客服與售後
          </p>
          <RouterLink
            v-if="canAccess('/support')"
            to="/support"
          >
            客服 SLA 佇列
          </RouterLink>
          <RouterLink
            v-if="canAccess('/cases')"
            to="/cases"
          >
            案件工作台
          </RouterLink>
          <RouterLink
            v-if="canAccess('/returns')"
            to="/returns"
          >
            退貨案件
          </RouterLink>
          <RouterLink
            v-if="canAccess('/reviews')"
            to="/reviews"
          >
            商品評價審核
          </RouterLink>

          <p
            v-if="['/orders', '/refunds', '/ai/usage', '/reports/sales-overview'].some(canAccess)"
            class="admin-sidebar__group-title"
          >
            營運
          </p>
          <RouterLink
            v-if="canAccess('/orders')"
            to="/orders"
          >
            訂單管理
          </RouterLink>
          <RouterLink
            v-if="canAccess('/refunds')"
            to="/refunds"
          >
            退款管理
          </RouterLink>
          <RouterLink
            v-if="canAccess('/ai/usage')"
            to="/ai/usage"
          >
            AI 用量與成本
          </RouterLink>
          <RouterLink
            v-if="canAccess('/reports/sales-overview')"
            to="/reports/sales-overview"
          >
            營運報表
          </RouterLink>
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
