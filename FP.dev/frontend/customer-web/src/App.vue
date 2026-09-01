<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, provide, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSessionStore } from './stores/session'
import { useCartIdentityCacheCleanup } from './features/cart/useCart'
import { BrandMark } from '@doselect/web-shared/components'
import {
  customerDefaultMotionPresetId,
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
const sessionStore = useSessionStore()
const isSupportSection = computed(() => route.path === '/support' || route.path.startsWith('/support/'))

// 組長 PR #29 round-6 review, P1 (point 3): registered here — mounted for the SPA's entire
// lifetime — rather than inside CartPage.vue, so an identity change (login/logout/account switch)
// evicts the previous identity's cart cache regardless of which page happens to be open at the
// moment it changes.
useCartIdentityCacheCleanup()

// 窄畫面把主導覽收起來，避免導覽列擠壓內容或造成頁面級橫向捲動。
const navOpen = ref(false)

// GSAP 動態視覺探索：A／B／C 方案由 App 統一選定後 provide 給頁面。
// `canSwitch` 在 production build 是常數 false，切換介面會被整段 tree-shake 掉。
const { presetId, preset, canSwitch, select } = useMotionPresetSelection(customerDefaultMotionPresetId)
const prefersReducedMotion = useMotionPreference()
provide(motionPresetKey, preset)

onMounted(() => {
  void sessionStore.refresh()
})

async function handleLogout(): Promise<void> {
  await sessionStore.logout()
  await router.push('/')
}
</script>

<template>
  <a
    class="skip-link"
    href="#main-content"
  >
    跳到主要內容
  </a>
  <div class="app-shell">
    <header class="site-header">
      <div class="header-bar">
        <RouterLink
          class="brand-link"
          to="/"
        >
          <BrandMark />
          <span class="brand-link__text">DoSelect<span class="brand-link__sub">懂選</span></span>
        </RouterLink>

        <button
          type="button"
          class="nav-toggle"
          :aria-expanded="navOpen"
          aria-controls="primary-nav"
          @click="navOpen = !navOpen"
        >
          選單
        </button>

        <nav
          id="primary-nav"
          class="primary-nav"
          :class="{ 'primary-nav--open': navOpen }"
          aria-label="主要導覽"
        >
          <RouterLink to="/">
            首頁
          </RouterLink>
          <RouterLink to="/products">
            商品
          </RouterLink>
          <RouterLink to="/ai-search">
            AI 懂選
          </RouterLink>
          <RouterLink to="/cart">
            購物車
          </RouterLink>
          <RouterLink to="/account/builds">
            我的組裝清單
          </RouterLink>
          <RouterLink to="/builds/new">
            新增組裝清單
          </RouterLink>
          <RouterLink
            to="/support"
            :aria-current="isSupportSection ? 'page' : undefined"
            :class="{ 'router-link-active': isSupportSection }"
          >
            客服中心
          </RouterLink>
          <template v-if="sessionStore.isAuthenticated">
            <RouterLink to="/account/reviews">
              我的評價
            </RouterLink>
            <RouterLink to="/account">
              會員資料
            </RouterLink>
            <RouterLink to="/account/addresses">
              收件地址
            </RouterLink>
            <span class="site-header__member">{{ sessionStore.user?.displayName }}</span>
            <button
              type="button"
              class="site-header__logout"
              @click="handleLogout"
            >
              登出
            </button>
          </template>
          <RouterLink
            v-else-if="sessionStore.status === 'anonymous'"
            to="/register"
          >
            登入／註冊
          </RouterLink>
          <button
            v-else-if="sessionStore.status === 'error'"
            type="button"
            class="site-header__identity-retry"
            @click="sessionStore.refresh()"
          >
            無法確認登入狀態，點此重試
          </button>
        </nav>
      </div>
    </header>
    <main
      id="main-content"
      class="site-main"
      tabindex="-1"
    >
      <div class="view-shell">
        <RouterView />
      </div>
    </main>
    <footer class="site-footer">
      <p class="site-footer__brand">
        DoSelect 懂選
      </p>
      <p>畢業專題展示系統｜商品、付款與物流資料皆為示範用途</p>
      <p class="site-footer__links">
        <RouterLink to="/support">
          客服中心
        </RouterLink>
        <RouterLink to="/products">
          全部商品
        </RouterLink>
      </p>
    </footer>
    <component
      :is="MotionDevSwitcher"
      v-if="canSwitch && MotionDevSwitcher"
      :preset-id="presetId"
      :reduced-motion="prefersReducedMotion"
      @select="select"
    />
  </div>
</template>
