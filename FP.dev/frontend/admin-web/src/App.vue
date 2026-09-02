<script setup lang="ts">
import { computed, defineAsyncComponent, provide } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAdminAuthStore } from './features/auth/stores/useAdminAuthStore'
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

// GSAP 動態視覺探索：與 Customer 共用同一組 preset 與同一個 dev-only 切換機制。
const { presetId, preset, canSwitch, select } = useMotionPresetSelection(adminDefaultMotionPresetId)
const prefersReducedMotion = useMotionPreference()
provide(motionPresetKey, preset)

const isAuthPage = computed(() => route.path.startsWith('/login'))
const canViewOperationalReports = computed(() => {
  const roles = auth.currentUser?.roles ?? []
  return ['MarketingAnalyst', 'FinanceManager', 'SuperAdmin'].some((role) => roles.includes(role))
})

/**
 * 側欄的優惠券入口只給看得到那個頁面的角色。
 *
 * 與 router 的 requiredRoles 同一份清單（Coupon.Manage）。
 */
const couponRoles = ['FinanceManager', 'MarketingAnalyst', 'SuperAdmin']
// 與 router 的 requiredRoles 同一份清單（Shipping.Read／Shipping.Manage）。
const shippingReadRoles = ['OrderManager', 'CatalogManager', 'SuperAdmin']
const canViewShipping = computed(() =>
  shippingReadRoles.some(role => auth.currentUser?.roles?.includes(role) ?? false))
const shippingManageRoles = ['OrderManager', 'SuperAdmin']
const canManageShipping = computed(() =>
  shippingManageRoles.some(role => auth.currentUser?.roles?.includes(role) ?? false))

// 組長 PR #78 round-2 review item 2 的原則：不給一個點下去只會被導到 /forbidden 的入口。
// 兩個匯入頁的角色與後端 Policy 對齊——商品匯入是 CatalogImport.*（CatalogManager／SuperAdmin），
// 庫存匯入是 InventoryAdjust.*（InventoryManager／SuperAdmin）。Route guard 仍是真正的邊界。
const catalogImportRoles = ['CatalogManager', 'SuperAdmin']
const canImportCatalog = computed(() =>
  catalogImportRoles.some(role => auth.currentUser?.roles?.includes(role) ?? false))
const inventoryImportRoles = ['InventoryManager', 'SuperAdmin']
const canImportInventory = computed(() =>
  inventoryImportRoles.some(role => auth.currentUser?.roles?.includes(role) ?? false))
// 組長 PR #114 裁定 B1：對帳案件頁（A-29）的入口只給 InventoryManager／SuperAdmin，與 route meta 和後端
// AdminInventoryController 的 InventoryManager Policy 相同。Route guard 仍是真正的邊界。
const inventoryReconciliationRoles = ['InventoryManager', 'SuperAdmin']
const canReconcileInventory = computed(() =>
  inventoryReconciliationRoles.some(role => auth.currentUser?.roles?.includes(role) ?? false))

const canManageCoupons = computed(() =>
  couponRoles.some(role => auth.currentUser?.roles?.includes(role) ?? false))
const invoiceRoles = ['FinanceManager', 'SuperAdmin']
const canManageInvoices = computed(() =>
  invoiceRoles.some(role => auth.currentUser?.roles?.includes(role) ?? false))

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
    <header class="site-header">
      <RouterLink
        class="brand-link"
        to="/"
      >
        <BrandMark />
        <!-- 正式商標 2 是方形徽章，40px 下裡面的字樣讀不出來，品牌名以文字承載 -->
        <span class="brand-link__text">
          <span class="brand-link__name">DoSelect 懂選</span>
          <span class="brand-link__scope">管理後台</span>
        </span>
      </RouterLink>
      <div class="site-header__end">
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
        class="admin-sidebar"
        aria-label="管理功能導覽"
      >
        <nav class="admin-sidebar__nav">
          <p class="admin-sidebar__group-title">
            總覽
          </p>
          <RouterLink to="/">
            首頁
          </RouterLink>

          <p class="admin-sidebar__group-title">
            商品
          </p>
          <RouterLink to="/catalog/lookups">
            品牌／分類／標籤管理
          </RouterLink>
          <RouterLink to="/products">
            商品管理
          </RouterLink>
          <RouterLink
            v-if="canImportCatalog"
            to="/products/import"
          >
            商品匯入
          </RouterLink>
          <RouterLink to="/catalog/specifications">
            分類規格範本
          </RouterLink>
          <RouterLink to="/catalog/compatibility">
            相容性規則
          </RouterLink>
          <!--
            只對看得到這個頁面的角色顯示。Route guard 仍然會擋越權，所以這不是
            安全邊界；但讓 CatalogManager 看到一個點下去只會被導到 /forbidden 的
            入口，等於把選單當成沒有意義的清單。
            這裡只處理新加入的優惠券入口，不順便重構整個側欄（alex #64 P3）。
          -->
          <RouterLink
            v-if="canManageCoupons"
            to="/coupons"
          >
            優惠券管理
          </RouterLink>
          <RouterLink to="/inventory">
            庫存管理
          </RouterLink>
          <RouterLink
            v-if="canImportInventory"
            to="/inventory/imports"
          >
            庫存匯入
          </RouterLink>
          <!--
            組長 PR #78 round-2 review item 2：兩個入口的角色不同——門市是 ShippingRead
            （OrderManager／CatalogManager／SuperAdmin 都能看），包裹限制是 ShippingManage
            （只有 OrderManager／SuperAdmin）。無條件顯示等於給 CatalogManager 一個點下去只會被
            導到 /forbidden 的連結。Route guard 仍是真正的邊界，這裡只是不給無意義的入口。
          -->
          <RouterLink
            v-if="canViewShipping"
            to="/shipping/stores"
          >
            示範超商門市
          </RouterLink>
          <RouterLink
            v-if="canManageShipping"
            to="/shipping/package-limits"
          >
            包裹限制版本
          </RouterLink>
          <RouterLink
            v-if="canManageShipping"
            to="/shipping/batches"
          >
            批次出貨
          </RouterLink>
          <RouterLink to="/inventory/reservations">
            庫存保留佇列
          </RouterLink>
          <RouterLink
            v-if="canReconcileInventory"
            to="/inventory/reconciliation-cases"
          >
            庫存對帳案件
          </RouterLink>
          <RouterLink
            v-if="canManageInvoices"
            to="/invoices"
          >
            模擬發票管理
          </RouterLink>
          <p class="admin-sidebar__group-title">
            客服與售後
          </p>
          <RouterLink to="/support">
            客服 SLA 佇列
          </RouterLink>
          <RouterLink to="/cases">
            案件工作台
          </RouterLink>
          <RouterLink to="/returns">
            退貨案件
          </RouterLink>
          <RouterLink to="/reviews">
            商品評價審核
          </RouterLink>

          <p class="admin-sidebar__group-title">
            營運
          </p>
          <RouterLink to="/ai/usage">
            AI 用量與成本
          </RouterLink>
          <RouterLink
            v-if="canViewOperationalReports"
            to="/reports/sales-overview"
          >
            營運報表
          </RouterLink>
        </nav>
      </aside>
      <main class="site-main">
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
